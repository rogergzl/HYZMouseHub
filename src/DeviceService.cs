using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        private const int MaxRecentErrors = 40;
        private const long MaxLogBytes = 64 * 1024;

        private readonly List<IDeviceProvider> _providers = new List<IDeviceProvider>();
        private readonly object _snapshotLock = new object();
        private readonly object _refreshLock = new object();
        private readonly object _errorLock = new object();
        private readonly Dictionary<string, HeldReading> _held =
            new Dictionary<string, HeldReading>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _recentErrors = new List<string>();
        private readonly Dictionary<string, string> _lastError =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

        /// <summary>最近的设备读取错误（新→旧），供设置界面显示；不抛异常。</summary>
        public List<string> RecentErrors()
        {
            lock (_errorLock)
            {
                List<string> result = new List<string>(_recentErrors);
                result.Reverse();
                return result;
            }
        }

        /// <summary>相同错误连续出现时只记一条，恢复后再次失败才重新记录。</summary>
        private void RecordError(string key, string deviceName, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            string clean = message.Trim();
            if (clean.Length == 0) return;
            lock (_errorLock)
            {
                string previous;
                if (_lastError.TryGetValue(key, out previous) && previous == clean) return;
                _lastError[key] = clean;

                string line = DateTime.Now.ToString("HH:mm:ss") + "  " + deviceName + "：" + clean;
                _recentErrors.Add(line);
                while (_recentErrors.Count > MaxRecentErrors) _recentErrors.RemoveAt(0);
                try
                {
                    string path = AppConfig.ReadErrorLogPath;
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > MaxLogBytes)
                    {
                        string[] lines = File.ReadAllLines(path);
                        int start = Math.Max(0, lines.Length - 200);
                        StringBuilder sb = new StringBuilder();
                        for (int i = start; i < lines.Length; i++) sb.AppendLine(lines[i]);
                        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                    }
                    File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch { }
            }
        }

        private void ClearError(string key)
        {
            lock (_errorLock)
            {
                if (_lastError.ContainsKey(key)) _lastError.Remove(key);
            }
        }

        public void Refresh()
        {
            lock (_refreshLock)
            {
                List<HidDeviceDescriptor> catalog;
                try { catalog = HidNative.Enumerate(); }
                catch (Exception ex)
                {
                    RecordError("HID枚举", "HID 枚举", ex.Message);
                    catalog = new List<HidDeviceDescriptor>();
                }

                // 按物理设备拆分目录：同一接收器的多个 HID 接口仍在一个子集里，
                // 但两个同款接收器不会被 Provider 内部的 VID:PID 去重合并掉。
                Dictionary<string, List<HidDeviceDescriptor>> groups = PartitionByPhysicalDevice(catalog);
                List<DeviceSnapshot> next = new List<DeviceSnapshot>();
                foreach (IDeviceProvider provider in _providers)
                {
                    List<DeviceSnapshot> found = new List<DeviceSnapshot>();
                    foreach (KeyValuePair<string, List<HidDeviceDescriptor>> group in groups)
                    {
                        List<DeviceInfo> devices;
                        try { devices = provider.Discover(group.Value); }
                        catch (Exception ex)
                        {
                            RecordError("发现:" + provider.Name, provider.Name, ex.Message);
                            continue;
                        }

                        foreach (DeviceInfo device in devices)
                        {
                            DeviceSnapshot s = new DeviceSnapshot();
                            s.Info = device;
                            try { s.Battery = provider.ReadBattery(device); }
                            catch (Exception ex)
                            {
                                s.Battery.Error = ex.Message;
                            }
                            found.Add(s);
                        }
                    }
                    // 先按真实读数合并重复条目，再注入缓存读数：反过来做，
                    // 被合并掉的那条会先拿到缓存电量，看上去“两台都活着”，合并就失效了。
                    CollapseMirroredDevices(found);
                    DisambiguateIds(found, provider);
                    foreach (DeviceSnapshot s in found)
                    {
                        if (string.IsNullOrEmpty(s.Battery.Error) && s.Battery.Percent >= 0)
                            ClearError(s.Info.Id);
                        // 休眠是无线设备的常态，不算读取失败，别写进诊断记录把日志刷满。
                        else if (!string.IsNullOrEmpty(s.Battery.Error) && !s.Battery.Sleeping)
                            RecordError(s.Info.Id, s.Info.ShortName, s.Battery.Error);
                        ApplyHoldOver(s);
                    }
                    next.AddRange(found);
                }
                lock (_snapshotLock) { _snapshots = next; }
            }
        }

        /// <summary>
        /// 一只鼠标会同时暴露多个 HID 接口，路径里的设备实例又不总相同；按实例拆组后
        /// 同一只鼠标会被当成两台（一台读得到、一台“休眠”）。这里按 VID:PID + 类型收口：
        /// 最多只有一台能读到时合并成一台；两台都能实读才认定是同款双接收器，保留分开。
        /// </summary>
        private static void CollapseMirroredDevices(List<DeviceSnapshot> found)
        {
            if (found.Count < 2) return;

            Dictionary<string, List<DeviceSnapshot>> buckets =
                new Dictionary<string, List<DeviceSnapshot>>(StringComparer.OrdinalIgnoreCase);
            foreach (DeviceSnapshot s in found)
            {
                // 型号名不参与分组：同一个硬件的不同接口，取到的产品名不一定完全一致。
                string key = s.Info.Provider + "|" + s.Info.VidPid + "|" + (int)s.Info.Kind;
                List<DeviceSnapshot> list;
                if (!buckets.TryGetValue(key, out list))
                {
                    list = new List<DeviceSnapshot>();
                    buckets.Add(key, list);
                }
                list.Add(s);
            }

            foreach (KeyValuePair<string, List<DeviceSnapshot>> bucket in buckets)
            {
                List<DeviceSnapshot> group = bucket.Value;
                if (group.Count < 2) continue;

                // 只有本次真正读到的才算活；缓存读数不算，否则永远合并不掉。
                int live = 0;
                foreach (DeviceSnapshot s in group)
                    if (s.Battery.Percent >= 0 && !s.Battery.Cached) live++;
                if (live >= 2) continue;

                // 保留没有 # 后缀的那条：旧配置、勾选状态、托盘顺序都还指着原 ID。
                DeviceSnapshot keep = group[0];
                foreach (DeviceSnapshot s in group)
                    if (s.Info.Id.IndexOf('#') < 0) { keep = s; break; }

                DeviceSnapshot source = null;
                foreach (DeviceSnapshot s in group)
                    if (s.Battery.Percent >= 0) { source = s; break; }
                if (source != null && source != keep) CopyReading(source, keep);

                foreach (DeviceSnapshot s in group)
                    if (s != keep) found.Remove(s);
            }
        }

        private static void CopyReading(DeviceSnapshot from, DeviceSnapshot to)
        {
            to.Battery.Percent = from.Battery.Percent;
            to.Battery.Charging = from.Battery.Charging;
            to.Battery.Online = from.Battery.Online;
            to.Battery.Millivolts = from.Battery.Millivolts;
            to.Battery.Detail = from.Battery.Detail;
            to.Battery.Error = from.Battery.Error;
            to.Battery.Cached = from.Battery.Cached;
            to.Battery.CachedAt = from.Battery.CachedAt;
            to.Battery.HealthText = from.Battery.HealthText;
            to.Battery.UpdatedAt = from.Battery.UpdatedAt;
        }

        /// <summary>
        /// 同 VID:PID 的多个接收器原本会合并成一个设备。首个保留原 ID 以兼容旧配置，
        /// 其余用 HID 路径短哈希作后缀，保证各自有独立角标 / 可见性 / 顺序 / 阈值。
        /// </summary>
        private static void DisambiguateIds(List<DeviceSnapshot> snapshots, IDeviceProvider provider)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IProviderCacheRekey rekey = provider as IProviderCacheRekey;
            foreach (DeviceSnapshot s in snapshots)
            {
                string id = s.Info.Id;
                if (seen.Add(id)) continue;
                string hash = HidNative.ShortHash(s.Info.HidPath);
                string candidate;
                int n = 1;
                do
                {
                    candidate = id + "#" + hash + (n > 1 ? "-" + n : "");
                    n++;
                }
                while (!seen.Add(candidate));
                s.Info.Id = candidate;
                if (rekey != null) rekey.Rekey(id, candidate);
            }
        }

        /// <summary>
        /// HID 路径形如 \\?\hid#vid_...&pid_...&mi_01#a&1A2B3C&0&0004#{guid}，
        /// 第二段去掉末尾接口号就是设备实例键：同一硬件的各接口相同，不同接收器不同。
        /// </summary>
        private static Dictionary<string, List<HidDeviceDescriptor>> PartitionByPhysicalDevice(
            List<HidDeviceDescriptor> catalog)
        {
            Dictionary<string, List<HidDeviceDescriptor>> groups =
                new Dictionary<string, List<HidDeviceDescriptor>>(StringComparer.OrdinalIgnoreCase);
            foreach (HidDeviceDescriptor h in catalog)
            {
                string key = PhysicalKey(h.Path);
                List<HidDeviceDescriptor> list;
                if (!groups.TryGetValue(key, out list))
                {
                    list = new List<HidDeviceDescriptor>();
                    groups.Add(key, list);
                }
                list.Add(h);
            }
            return groups;
        }

        private static string PhysicalKey(string path)
        {
            if (string.IsNullOrEmpty(path)) return "path:" + HidNative.ShortHash(path);
            string[] parts = path.Split('#');
            if (parts.Length < 3) return "path:" + HidNative.ShortHash(path);
            string instance = parts[2];
            int last = instance.LastIndexOf('&');
            return "inst:" + (last > 0 ? instance.Substring(0, last) : instance);
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
