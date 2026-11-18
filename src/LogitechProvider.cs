using System;
using System.Collections.Generic;
using System.Text;

namespace HyzMouseHub
{
    // Logitech Lightspeed / Unifying / Bolt receivers speaking HID++ 2.0.
    // Battery: feature 0x1000 func 0 or 0x1004 func 1 (percentage).
    // DPI: feature 0x2202 func 5/6 (XY) or 0x2201 func 2/3 (single axis).
    internal sealed class LogitechProvider : DeviceProviderBase
    {
        private const ushort LogitechVid = 0x046D;
        private const ushort FeatureBatteryLevel = 0x1000;
        private const ushort FeatureBatteryVoltage = 0x1004;
        private const ushort FeatureAdjustableDpi = 0x2201;
        private const ushort FeatureExtendedDpi = 0x2202;

        private const byte ReportLong = 0x11;
        private const byte SwId = 0x0E;

        private static readonly Dictionary<string, byte> IndexCache = new Dictionary<string, byte>();

        public override string Name
        {
            get { return "Logitech HID++"; }
        }

        public override List<DeviceInfo> Discover(List<HidDeviceDescriptor> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();
            HashSet<string> seen = new HashSet<string>();
            foreach (HidDeviceDescriptor h in catalog)
            {
                if (h.VendorId != LogitechVid) continue;
                if (h.UsagePage != 0xFF00 || h.Usage != 0x0002) continue;
                if (h.OutputReportLength != 20) continue;
                string key = "LOGI:" + h.VendorId.ToString("X4") + ":" + h.ProductId.ToString("X4");
                if (!seen.Add(key)) continue;

                DeviceInfo d = new DeviceInfo();
                d.Id = key;
                d.Provider = Name;
                d.Brand = "Logitech";
                d.Model = ResolveModel(h);
                d.Name = "Logitech " + d.Model;
                d.VendorId = h.VendorId;
                d.ProductId = h.ProductId;
                d.HidPath = h.Path;
                d.SupportsBattery = true;
                d.SupportsDpi = true;
                d.DpiMin = 200;
                d.DpiMax = 26000;
                d.DpiStep = 1;
                d.DpiNote = "HID++ 0x2202 / 0x2201";
                found.Add(d);
            }
            return found;
        }

        private static string ResolveModel(HidDeviceDescriptor h)
        {
            switch (h.ProductId)
            {
                case 0xC53F: return "G304 / G305";
                case 0xC547:
                case 0xC54D:
                case 0xC09B: return "G PRO X SUPERLIGHT 2";
                case 0xC548: return "M750M";
                case 0xC539: return "G102";
                case 0xC52B: return "Unifying Receiver";
                default:
                    string name = h.ProductName ?? "";
                    if (name.IndexOf("keyboard", StringComparison.OrdinalIgnoreCase) >= 0) return "Keyboard";
                    return string.IsNullOrEmpty(name) ? h.VidPid : name;
            }
        }

        public override BatteryReading ReadBattery(DeviceInfo device)
        {
            BatteryReading r = new BatteryReading();
            r.UpdatedAt = DateTime.Now;
            try
            {
                using (HidSession s = new HidSession(device.HidPath, 20, 20))
                {
                    s.Flush(10);
                    byte dev = ResolveDeviceIndex(s, device.HidPath);
                    if (dev == 0) { r.Error = "设备休眠"; return r; }

                    bool responded;
                    byte feat = GetFeature(s, dev, FeatureBatteryLevel, out responded);
                    byte func = 0;
                    if (feat == 0 && !responded) { r.Error = "设备休眠"; return r; }

                    if (feat == 0)
                    {
                        feat = GetFeature(s, dev, FeatureBatteryVoltage, out responded);
                        func = 1;
                        if (feat == 0 && !responded) { r.Error = "设备休眠"; return r; }
                        if (feat == 0) { r.Online = true; r.Error = "该设备不上报百分比电量"; return r; }
                    }

                    byte[] reply = Call(s, dev, feat, (byte)(func << 4), null, 700);
                    if (reply == null) { r.Error = "设备休眠"; return r; }
                    if (reply[3] == 0xFF) { r.Error = "HID++ 错误 0x" + reply[5].ToString("X2"); return r; }
                    int level = reply[4];
                    if (level > 100) { r.Error = "读数异常"; r.Detail = Hex(reply); return r; }
                    r.Percent = level;
                    r.Charging = (reply[5] & 0x01) != 0;
                    r.Online = true;
                    r.Detail = "HID++ 2.0";
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }
            return r;
        }

        public override DpiReading ReadDpi(DeviceInfo device)
        {
            try
            {
                using (HidSession s = new HidSession(device.HidPath, 20, 20))
                {
                    return ReadDpiCore(s, device.HidPath);
                }
            }
            catch (Exception ex)
            {
                DpiReading r = new DpiReading();
                r.Error = ex.Message;
                return r;
            }
        }

        private static DpiReading ReadDpiCore(HidSession s, string path)
        {
            DpiReading r = new DpiReading();
            {
                s.Flush(10);
                byte dev = ResolveDeviceIndex(s, path);
                if (dev == 0) { r.Error = "设备休眠"; return r; }

                    bool responded;
                    byte ext = GetFeature(s, dev, FeatureExtendedDpi, out responded);
                    if (ext > 0)
                    {
                        byte[] reply = Call(s, dev, ext, 0x50, null, 700); // func 5
                        if (reply != null && reply.Length >= 11 && reply[3] != 0xFF)
                        {
                            int x = (reply[5] << 8) | reply[6];
                            int y = (reply[9] << 8) | reply[10];
                            if (ValidDpi(x) && ValidDpi(y))
                            {
                                r.X = x;
                                r.Y = y;
                                return r;
                            }
                        }
                    }

                    byte std = GetFeature(s, dev, FeatureAdjustableDpi, out responded);
                    if (std == 0) { r.Error = "设备未声明可调 DPI"; return r; }
                    byte[] basic = Call(s, dev, std, 0x20, null, 700); // func 2
                    if (basic == null || basic.Length < 7 || basic[3] == 0xFF)
                    {
                        r.Error = "DPI 查询无应答";
                        return r;
                    }
                    int dpi = (basic[5] << 8) | basic[6];
                    if (!ValidDpi(dpi)) { r.Error = "DPI 读数异常"; return r; }
                    r.X = dpi;
                    r.Y = dpi;
            }
            return r;
        }

        public override bool WriteDpi(DeviceInfo device, int x, int y, int stage)
        {
            using (HidSession s = new HidSession(device.HidPath, 20, 20))
            {
                s.Flush(10);
                byte dev = ResolveDeviceIndex(s, device.HidPath);
                if (dev == 0) return false;
                bool responded;

                byte ext = GetFeature(s, dev, FeatureExtendedDpi, out responded);
                if (ext > 0)
                {
                    byte[] data = new byte[]
                    {
                        0x00,
                        (byte)(x >> 8), (byte)(x & 0xff),
                        (byte)(y >> 8), (byte)(y & 0xff)
                    };
                    byte[] reply = Call(s, dev, ext, 0x60, data, 500); // func 6
                    if (reply != null && reply.Length >= 4 && reply[3] != 0xFF)
                    {
                        DpiReading verify = ReadDpiCore(s, device.HidPath);
                        if (verify.X == x && (y == x || verify.Y == y)) return true;
                    }
                }

                byte std = GetFeature(s, dev, FeatureAdjustableDpi, out responded);
                if (std == 0) return false;
                byte[] basicData = new byte[] { 0x00, (byte)(x >> 8), (byte)(x & 0xff) };
                byte[] basicReply = Call(s, dev, std, 0x30, basicData, 500); // func 3
                if (basicReply == null || basicReply.Length < 4 || basicReply[3] == 0xFF) return false;
                DpiReading check = ReadDpiCore(s, device.HidPath);
                return check.X == x;
            }
        }

        private static bool ValidDpi(int dpi)
        {
            return dpi >= 200 && dpi <= 40000;
        }

        private static byte ResolveDeviceIndex(HidSession s, string path)
        {
            byte cached;
            if (IndexCache.TryGetValue(path, out cached)) return cached;
            long until = Environment.TickCount + 4000;
            for (byte i = 1; i <= 4; i++)
            {
                if (Environment.TickCount - until >= 0) break;
                byte[] req = NewRequest(i, 0x00, 0x01);
                if (!s.Write(req, 300)) continue;
                if (WaitFor(s, i, 0x00, req[3], Math.Min(1200, (int)(until - Environment.TickCount))) != null)
                {
                    IndexCache[path] = i;
                    return i;
                }
            }
            return 0;
        }

        private static byte GetFeature(HidSession s, byte dev, ushort feature, out bool responded)
        {
            responded = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                byte[] req = NewRequest(dev, 0x00, 0x00);
                req[4] = (byte)(feature >> 8);
                req[5] = (byte)(feature & 0xff);
                if (!s.Write(req, 300)) continue;
                byte[] reply = WaitFor(s, dev, 0x00, req[3], attempt == 0 ? 400 : 600);
                if (reply != null)
                {
                    responded = true;
                    return reply[4];
                }
            }
            return 0;
        }

        private static byte[] Call(HidSession s, byte dev, byte featureIndex, byte functionByte, byte[] data, int timeoutMs)
        {
            byte[] req = NewRequest(dev, featureIndex, (byte)(functionByte >> 4));
            if (data != null) Array.Copy(data, 0, req, 4, Math.Min(data.Length, 16));
            if (!s.Write(req, 400)) return null;
            return WaitFor(s, dev, featureIndex, req[3], timeoutMs);
        }

        private static byte[] NewRequest(byte dev, byte featureIndex, byte function)
        {
            byte[] req = new byte[20];
            req[0] = ReportLong;
            req[1] = dev;
            req[2] = featureIndex;
            req[3] = (byte)(((function & 0x0f) << 4) | (SwId & 0x0f));
            return req;
        }

        private static byte[] WaitFor(HidSession s, byte dev, byte featureIndex, byte functionByte, int timeoutMs)
        {
            long until = Environment.TickCount + timeoutMs;
            while (true)
            {
                int left = (int)(until - Environment.TickCount);
                if (left <= 0) return null;
                byte[] p = s.Read(Math.Min(60, left));
                if (p == null || p.Length < 7) continue;
                if (p[1] != dev || p[2] != featureIndex || p[3] != functionByte) continue;
                if (p[3] == 0xFF) continue;
                return p;
            }
        }

        private static string Hex(byte[] b)
        {
            if (b == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("X2")).Append(' ');
            return sb.ToString().Trim();
        }
    }
}
