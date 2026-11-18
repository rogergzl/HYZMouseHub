using System;
using System.Collections.Generic;
using System.Threading;

namespace HyzMouseHub
{
    public sealed class DeviceService
    {
        private readonly List<IDeviceProvider> _providers = new List<IDeviceProvider>();
        private readonly object _snapshotLock = new object();
        private readonly object _refreshLock = new object();
        private List<DeviceSnapshot> _snapshots = new List<DeviceSnapshot>();

        public DeviceService()
        {
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
                        next.Add(s);
                    }
                }
                lock (_snapshotLock) { _snapshots = next; }
            }
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
