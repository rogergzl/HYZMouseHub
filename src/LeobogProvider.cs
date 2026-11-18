using System;
using System.Collections.Generic;
using System.Threading;

namespace HyzMouseHub
{
    // LEOBOG Hi98 Pro / Hi75c Pro 2.4G receiver:
    //   VID 0x3554 / PID 0xFA09 / UsagePage 0xFF02 / Usage 0x0002 / 20-byte reports.
    //   Request : 13 CA 00 ... [sum8 of bytes 0..18 at byte 19]
    //   Response: 13 4A 01 00 02 <percent> <charge> ... [sum8 at byte 19]
    //   charge  : 0x01 = discharging, 0x10 = charging.
    // Protocol source: GearPower profiles/leobog-hi98pro.json (not verified on our hardware).
    internal sealed class LeobogProvider : DeviceProviderBase
    {
        private const ushort LeobogVid = 0x3554;
        private const ushort KeyboardPid = 0xFA09;
        private const int ReportLength = 20;

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, string[]> Paths = new Dictionary<string, string[]>();

        public override string Name
        {
            get { return "LEOBOG HID"; }
        }

        public override List<DeviceInfo> Discover(List<HidDeviceDescriptor> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();
            List<string> candidates = new List<string>();
            string product = "";

            foreach (HidDeviceDescriptor h in catalog)
            {
                if (h.VendorId != LeobogVid || h.ProductId != KeyboardPid) continue;
                if (h.UsagePage != 0xFF02 || h.Usage != 0x0002) continue;
                if (h.InputReportLength != ReportLength || h.OutputReportLength != ReportLength) continue;
                if (!candidates.Contains(h.Path)) candidates.Add(h.Path);
                if (product.Length == 0 && !string.IsNullOrEmpty(h.ProductName)) product = h.ProductName;
            }
            if (candidates.Count == 0) return found;

            string model = product.IndexOf("HI98", StringComparison.OrdinalIgnoreCase) >= 0
                ? "HI98PRO" : "Hi75c Pro";
            DeviceInfo d = new DeviceInfo();
            d.Id = "LEOBOG:3554:FA09";
            d.Provider = Name;
            d.Brand = "LEOBOG";
            d.Model = model;
            d.Kind = DeviceKind.Keyboard;
            d.Name = "LEOBOG " + model;
            d.VendorId = LeobogVid;
            d.ProductId = KeyboardPid;
            d.HidPath = BestPath(d.Id, candidates);
            d.SupportsBattery = true;
            d.SupportsDpi = false;
            d.Sensor = "";
            d.DpiNote = "键盘仅显示电量；20 字节 0x13/0xCA 协议";

            lock (CacheLock) { Paths[d.Id] = candidates.ToArray(); }
            found.Add(d);
            return found;
        }

        private static string BestPath(string id, List<string> fallback)
        {
            lock (CacheLock)
            {
                string[] saved;
                if (Paths.TryGetValue(id, out saved) && saved.Length > 0) return saved[0];
            }
            return fallback.Count > 0 ? fallback[0] : "";
        }

        private static byte[] Exchange(string path)
        {
            byte[] request = new byte[ReportLength];
            request[0] = 0x13;
            request[1] = 0xCA;
            request[19] = Sum8(request, 0, 19);

            try
            {
                using (HidSession session = new HidSession(path, ReportLength, ReportLength))
                {
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        bool sent = session.Write(request, 350);
                        if (!sent) session.SetOutputReport(request);
                        Thread.Sleep(25);
                        byte[] response = session.Read(350);
                        if (IsValid(response)) return response;
                        Thread.Sleep(40);
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool IsValid(byte[] r)
        {
            if (r == null || r.Length != ReportLength) return false;
            if (r[0] != 0x13 || r[1] != 0x4A || r[2] != 0x01 || r[3] != 0x00 || r[4] != 0x02) return false;
            if (r[5] > 100) return false;
            return r[19] == Sum8(r, 0, 19);
        }

        private static byte Sum8(byte[] buf, int start, int count)
        {
            int sum = 0;
            for (int i = 0; i < count; i++) sum += buf[start + i];
            return (byte)(sum & 0xFF);
        }

        public override BatteryReading ReadBattery(DeviceInfo device)
        {
            BatteryReading r = new BatteryReading();
            r.UpdatedAt = DateTime.Now;

            string[] paths;
            lock (CacheLock)
            {
                if (!Paths.TryGetValue(device.Id, out paths)) paths = new string[] { device.HidPath };
                else paths = (string[])paths.Clone();
            }

            byte[] response = null;
            foreach (string path in paths)
            {
                response = Exchange(path);
                if (response != null) break;
            }
            if (response == null)
            {
                r.Error = "未应答，请切到 2.4G 并按键唤醒";
                return r;
            }

            int percent = response[5];
            // 参考实现中 0x10（充电）/100% 时数值可能是占位值，仍显示但标注来源。
            r.Percent = percent;
            r.Charging = response[6] == 0x10;
            r.Online = true;
            r.Detail = "LEOBOG 2.4G" + (r.Charging ? " · 充电中" : "");
            return r;
        }
    }
}
