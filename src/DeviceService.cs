using System;
using System.Collections.Generic;
using System.Threading;

namespace HyzMouseHub
{
    /// <summary>
    /// 设备状态中间层：唯一持有设备快照的地方，界面只读取 Snapshot()，不自行缓存设备数值。
    /// 设备无应答时沿用上一次读数，连续无响应的刷新周期数超过保留窗口才判定为休眠。
    /// </summary>
    internal sealed class DeviceService
    {
        /// <summary>某台设备上一次成功读到的电量，用于休眠保留窗口。</summary>
        private sealed class HeldReading
        {
            public int Percent;
            public bool Charging;
            public int Millivolts;
            public string Detail = "";
            public string HealthText = "";
            public DateTime At;
            // 连续多少个刷新周期没有读到数据。判定休眠按这个计数走，
            // 而不是靠在两次刷新之间插一次额外的探查。
            public int MissedCycles;
        }

        private readonly List<IDeviceProvider> _providers = new List<IDeviceProvider>();
        private readonly object _snapshotLock = new object();
        private readonly object _refreshLock = new object();
        private readonly Dictionary<string, HeldReading> _held =
            new Dictionary<string, HeldReading>(StringComparer.OrdinalIgnoreCase);
        private readonly AppState _state;
        private List<DeviceSnapshot> _snapshots = new List<DeviceSnapshot>();

        public DeviceService(AppState state)
        {
            _state = state;
            _providers.Add(new AtkProvider());
            _providers.Add(new RazerProvider());
            _providers.Add(new LeobogProvider());
            _providers.Add(new EposProvider());
            _providers.Add(new LogitechProvider()); // 保留兼容，不再维护
        }

        public List<DeviceSnapshot> Snapshot()
        {
            lock (_snapshotLock)
            {
                return new List<DeviceSnapshot>(_snapshots);
            }
        }

        public void Refresh()
        {
            lock (_refreshLock)
            {
                List<HidDeviceDescriptor> catalog;
                try { catalog = HidNative.Enumerate(); }
                catch { catalog = new List<HidDeviceDescriptor>(); }

                List<DeviceSnapshot> next = new List<DeviceSnapshot>();
                foreach (IDeviceProvider provider in _providers)
                {
                    List<DeviceInfo> devices;
                    try { devices = provider.Discover(catalog); }
                    catch { continue; }

                    foreach (DeviceInfo device in devices)
                    {
                        DeviceSnapshot s = new DeviceSnapshot();
                        s.Info = device;
                        try { s.Battery = provider.ReadBattery(device); }
                        catch (Exception ex)
                        {
                            s.Battery.Error = ex.Message;
                        }
                        ApplyHoldOver(s);
                        next.Add(s);
                    }
                }
                lock (_snapshotLock) { _snapshots = next; }
            }
        }

        /// <summary>
        /// 本次没读到电量时，若上一次读数还在保留窗口内，就沿用它并把读数标记为缓存。
        /// 窗口到期后清掉缓存，界面才会显示“休眠”并不再显示电量。
        /// </summary>
        private void ApplyHoldOver(DeviceSnapshot snapshot)
        {
            BatteryReading battery = snapshot.Battery;
            string id = snapshot.Info.Id;

            if (battery.Percent >= 0)
            {
                // 实时读到的值：更新缓存基准。
                HeldReading fresh = new HeldReading();
                fresh.Percent = battery.Percent;
                fresh.Charging = battery.Charging;
                fresh.Millivolts = battery.Millivolts;
                fresh.Detail = battery.Detail;
                fresh.HealthText = battery.HealthText;
                fresh.At = DateTime.Now;
                fresh.MissedCycles = 0;
                _held[id] = fresh;
                return;
            }

            HeldReading held;
            if (!_held.TryGetValue(id, out held)) return;

            // 按刷新周期累计“没有活动”的次数：窗口分钟数 ÷ 刷新间隔 = 允许连续无响应的周期数。
            // 这样判定只依赖真实的轮询结果，不必额外插探查，也不会因为全屏暂停而误判。
            held.MissedCycles++;
            int windowMinutes = _state == null ? AppConfig.SleepTimeoutDefault : _state.SleepTimeoutMinutes;
            int refreshSeconds = _state == null ? 15 : _state.RefreshSeconds;
            int allowedCycles = Math.Max(1, (Math.Max(AppConfig.SleepTimeoutMin, windowMinutes) * 60) / Math.Max(5, refreshSeconds));
            if (held.MissedCycles > allowedCycles)
            {
                _held.Remove(id);
                return;
            }
            TimeSpan elapsed = DateTime.Now - held.At;

            battery.Percent = held.Percent;
            battery.Charging = held.Charging;
            battery.Millivolts = held.Millivolts;
            battery.HealthText = held.HealthText;
            battery.Cached = true;
            battery.CachedAt = held.At;
            string ago = ElapsedText(elapsed);
            battery.Detail = string.IsNullOrEmpty(held.Detail)
                ? "上次读数 " + ago + " · 连续 " + held.MissedCycles + " 个周期无响应"
                : held.Detail + " · 上次读数 " + ago;
        }

        private static string ElapsedText(TimeSpan elapsed)
        {
            int minutes = (int)Math.Floor(elapsed.TotalMinutes);
            if (minutes <= 0) return "刚刚";
            if (minutes < 60) return minutes + " 分钟前";
            return (int)(minutes / 60) + " 小时前";
        }

        public DeviceSnapshot Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (DeviceSnapshot s in Snapshot())
            {
                if (string.Equals(s.Info.Id, id, StringComparison.OrdinalIgnoreCase)) return s;
            }
            return null;
        }

        public IDeviceProvider ProviderFor(DeviceInfo device)
        {
            if (device == null) return null;
            foreach (IDeviceProvider p in _providers)
            {
                if (string.Equals(p.Name, device.Provider, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        public DpiReading ReadDpi(DeviceInfo device)
        {
            IDeviceProvider p = ProviderFor(device);
            if (p == null)
            {
                DpiReading r = new DpiReading();
                r.Error = "未找到设备驱动";
                return r;
            }
            try { return p.ReadDpi(device); }
            catch (Exception ex)
            {
                DpiReading r = new DpiReading();
                r.Error = ex.Message;
                return r;
            }
        }

        public bool WriteDpi(DeviceInfo device, int x, int y, int stage)
        {
            IDeviceProvider p = ProviderFor(device);
            if (p == null) return false;
            try { return p.WriteDpi(device, x, y, stage); }
            catch { return false; }
        }

        public static List<HidDeviceDescriptor> ListHid()
        {
            return HidNative.Enumerate();
        }
    }
}
