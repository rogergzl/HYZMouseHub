using System;
using System.Collections.Generic;
using System.Threading;

namespace HyzMouseHub
{
    // EPOS / Sennheiser dongles (VID 0x1395).
    //
    // 1395:0300 / 1395:0303 use the "Capricorn" 107-byte vendor HID protocol:
    //   report id 0x01, magic 0xFEEB, kind 0x0004, node 4 (headset),
    //   body = group 0x0012 / command 0x0003, battery is the third u16 word.
    //   CRC-8: poly 0x07, init 0xFF, MSB-first, no final xor.
    //
    // 1395:0089 (GSA 70, older firmware) and 1395:008A (GSP 670 headset over USB)
    // have no public battery protocol. We read a standard HID Battery Strength
    // feature report when one is declared; otherwise the device is listed with
    // an honest "unknown" state. Use --probe=1395:0089 to export raw reports.
    internal sealed class EposProvider : DeviceProviderBase, IProviderCacheRekey
    {
        private const ushort EposVid = 0x1395;
        private const int CapricornLength = 107;

        private sealed class Target
        {
            public DeviceInfo Info;
            public bool Capricorn;
            public string[] Paths;
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, Target> Targets = new Dictionary<string, Target>();

        public void Rekey(string oldId, string newId)
        {
            if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase)) return;
            lock (CacheLock)
            {
                Target target;
                if (!Targets.TryGetValue(oldId, out target)) return;
                Targets.Remove(oldId);
                Targets[newId] = target;
            }
        }


        public override string Name
        {
            get { return "EPOS HID"; }
        }

        public override List<DeviceInfo> Discover(List<HidDeviceDescriptor> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();
            Dictionary<ushort, List<HidDeviceDescriptor>> groups = new Dictionary<ushort, List<HidDeviceDescriptor>>();

            foreach (HidDeviceDescriptor h in catalog)
            {
                if (h.VendorId != EposVid) continue;
                bool capricornInterface = h.UsagePage == 0xFFFF && h.InputReportLength >= 64;
                bool batteryInterface = h.UsagePage == 0x0C && (h.Usage == 0x50 || h.Usage == 0x60);
                if (!capricornInterface && !batteryInterface && !IsKnownDongle(h.ProductId)) continue;

                List<HidDeviceDescriptor> list;
                if (!groups.TryGetValue(h.ProductId, out list))
                {
                    list = new List<HidDeviceDescriptor>();
                    groups.Add(h.ProductId, list);
                }
                list.Add(h);
            }

            foreach (KeyValuePair<ushort, List<HidDeviceDescriptor>> kv in groups)
            {
                ushort pid = kv.Key;
                bool capricorn = pid == 0x0300 || pid == 0x0303;
                string id = "EPOS:1395:" + pid.ToString("X4");
                string model = pid == 0x0089 ? "GSA 70"
                             : pid == 0x008A ? "GSP 670"
                             : pid == 0x0300 ? "GSA 70 (Capricorn)"
                             : pid == 0x0303 ? "H3PRO" : (" Dongle " + pid.ToString("X4")).Trim();

                List<string> bestPaths = new List<string>();
                foreach (HidDeviceDescriptor h in kv.Value)
                {
                    bool wanted = capricorn
                        ? (h.UsagePage == 0xFFFF && h.InputReportLength >= 64)
                        : (h.UsagePage == 0x0C && (h.Usage == 0x50 || h.Usage == 0x60));
                    if (wanted && !bestPaths.Contains(h.Path)) bestPaths.Add(h.Path);
                }
                if (bestPaths.Count == 0)
                    foreach (HidDeviceDescriptor h in kv.Value)
                        if (!bestPaths.Contains(h.Path)) bestPaths.Add(h.Path);

                DeviceInfo d = new DeviceInfo();
                d.Id = id;
                d.Provider = Name;
                d.Brand = "EPOS";
                d.Model = model;
                d.Kind = DeviceKind.Headset;
                d.Name = "EPOS " + model;
                d.VendorId = EposVid;
                d.ProductId = pid;
                d.HidPath = bestPaths[0];
                d.SupportsBattery = true;
                d.SupportsDpi = false;
                d.Sensor = "";
                d.DpiNote = capricorn
                    ? "Capricorn HID 0x12/0x03 读取电量"
                    : (bestPaths.Count > 0 && HasBatteryInterface(kv.Value)
                        ? "标准 HID 电量报告"
                        : "GSP 670 电量协议未公开；可运行 --probe=1395:0089 导出报告协助适配");

                Target t = new Target();
                t.Info = d;
                t.Capricorn = capricorn;
                t.Paths = bestPaths.ToArray();
                lock (CacheLock) { Targets[id] = t; }
                found.Add(d);
            }
            return found;
        }

        private static bool IsKnownDongle(ushort pid)
        {
            return pid == 0x0089 || pid == 0x008A || pid == 0x0300 || pid == 0x0303;
        }

        private static bool HasBatteryInterface(List<HidDeviceDescriptor> list)
        {
            foreach (HidDeviceDescriptor h in list)
                if (h.UsagePage == 0x0C && (h.Usage == 0x50 || h.Usage == 0x60)) return true;
            return false;
        }

        public override BatteryReading ReadBattery(DeviceInfo device)
        {
            BatteryReading r = new BatteryReading();
            r.UpdatedAt = DateTime.Now;

            Target target;
            lock (CacheLock) { Targets.TryGetValue(device.Id, out target); }
            if (target == null) { r.Error = "设备条目已失效，请重新扫描"; return r; }

            if (target.Capricorn) return ReadCapricorn(target);

            foreach (string path in target.Paths)
            {
                int percent = ReadStandardBattery(path);
                if (percent >= 0)
                {
                    r.Percent = percent;
                    r.Online = true;
                    r.Detail = "EPOS HID 电量";
                    return r;
                }
            }
            r.Error = "该型号电量协议未公开，未读取到标准电量报告";
            return r;
        }

        private static BatteryReading ReadCapricorn(Target target)
        {
            BatteryReading r = new BatteryReading();
            r.UpdatedAt = DateTime.Now;

            byte[] body = new byte[] { 0x12, 0x00, 0x03, 0x00 };
            byte[] request = new byte[CapricornLength];
            request[0] = 0x01;
            request[1] = 0xEB;
            request[2] = 0xFE;
            request[3] = 0x01;
            request[5] = 0x04;  // kind: response/request
            request[7] = 0x04;  // node: headset
            request[9] = 0x04;  // body length
            request[11] = Crc8(body);
            Array.Copy(body, 0, request, 13, body.Length);

            foreach (string path in target.Paths)
            {
                try
                {
                    using (HidSession session = new HidSession(path, CapricornLength, CapricornLength))
                    {
                        session.Flush(10);
                        if (!session.Write(request, 500) && !session.SetOutputReport(request)) continue;
                        int deadline = Environment.TickCount + 1500;
                        while (Environment.TickCount < deadline)
                        {
                            byte[] reply = session.Read(120);
                            if (reply == null || reply.Length < 19) continue;
                            if (reply[0] != 0x01 || reply[1] != 0xEB || reply[2] != 0xFE) continue;
                            if (reply[5] != 0x04 || reply[7] != 0x04) continue;
                            if (reply[13] != 0x12 || reply[15] != 0x03) continue;
                            int percent = reply[17] | (reply[18] << 8);
                            if (percent < 0 || percent > 100) continue;
                            r.Percent = percent;
                            r.Online = true;
                            r.Detail = "EPOS Capricorn";
                            return r;
                        }
                    }
                }
                catch { }
            }
            r.Error = "耳机未应答，请开机并连接接收器";
            return r;
        }

        private static int ReadStandardBattery(string path)
        {
            for (byte reportId = 0; reportId < 4; reportId++)
            {
                try
                {
                    using (HidSession session = new HidSession(path, 1, 1))
                    {
                        byte[] buf = new byte[9];
                        buf[0] = reportId;
                        if (!session.GetFeature(buf)) continue;
                        for (int i = 1; i < buf.Length; i++)
                        {
                            if (buf[i] >= 1 && buf[i] <= 100) return buf[i];
                        }
                    }
                }
                catch { }
            }
            return -1;
        }

        private static byte Crc8(byte[] data)
        {
            byte crc = 0xFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                    crc = (byte)((crc & 0x80) != 0 ? ((crc << 1) ^ 0x07) : (crc << 1));
            }
            return crc;
        }
    }
}
