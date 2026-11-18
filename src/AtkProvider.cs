using System;
using System.Collections.Generic;
using System.Text;

namespace HyzMouseHub
{
    // ATK/VXE 0x373B vendor receiver, 17-byte report 0x08 on FF02/0002.
    // Battery command is shared with Voltide; EEPROM DPI commands are from
    // OpenMouse (MIT), which derived them from ATK Hub 3.2.x.
    internal sealed class AtkProvider : DeviceProviderBase
    {
        private const ushort VidAtk = 0x373B;
        private const ushort VendorPage = 0xFF02;
        private const int FrameSize = 17;
        private const byte ReportId = 0x08;
        private const byte CmdBattery = 0x04;
        private const byte CmdIdentity = 0x10;
        private const byte CmdReadEeprom = 0x08;
        private const byte CmdWriteEeprom = 0x07;
        private const int ChecksumTarget = 0x55;

        private static readonly Dictionary<string, string> SensorMap = BuildSensorMap();
        private static readonly Dictionary<string, string> IdentityCache = new Dictionary<string, string>();

        public override string Name
        {
            get { return "ATK HID"; }
        }

        public override List<DeviceInfo> Discover(List<HidDeviceDescriptor> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();
            HashSet<string> seen = new HashSet<string>();
            foreach (HidDeviceDescriptor h in catalog)
            {
                if (h.VendorId != VidAtk) continue;
                if (h.UsagePage != VendorPage) continue;
                if (h.InputReportLength != FrameSize || h.OutputReportLength != FrameSize) continue;
                string key = "ATK:" + h.VendorId.ToString("X4") + ":" + h.ProductId.ToString("X4");
                if (!seen.Add(key)) continue;

                DeviceInfo d = new DeviceInfo();
                d.Id = key;
                d.Provider = Name;
                d.Brand = "ATK";
                d.Model = ResolveModel(h);
                d.Kind = d.Model.IndexOf("耳机", StringComparison.Ordinal) >= 0 ? DeviceKind.Headset : DeviceKind.Mouse;
                d.Name = "ATK " + d.Model;
                d.VendorId = h.VendorId;
                d.ProductId = h.ProductId;
                d.HidPath = h.Path;
                d.SupportsBattery = true;
                d.SupportsDpi = true; // confirmed after CID/MID identity is read
                d.DpiMin = 50;
                d.DpiMax = 42000;
                d.DpiStep = 10;
                d.DpiNote = "写入前会读取 CID/MID，未知型号自动禁用";
                found.Add(d);
            }
            return found;
        }

        private static string ResolveModel(HidDeviceDescriptor h)
        {
            string p = (h.ProductName ?? "").ToUpperInvariant();
            if (p.Contains("HEADSET") || h.ProductId == 0x1262) return "耳机接收器";
            if (h.ProductId == 0x1278) return "8K 接收器";
            if (h.ProductId == 0x11D9) return "F1 Ultimate 2.0 接收器";
            if (string.IsNullOrEmpty(p)) return h.VidPid;
            return p;
        }

        public override BatteryReading ReadBattery(DeviceInfo device)
        {
            BatteryReading r = new BatteryReading();
            r.UpdatedAt = DateTime.Now;
            try
            {
                using (HidSession s = new HidSession(device.HidPath, FrameSize, FrameSize))
                {
                    byte[] response = Exchange(s, Build(CmdBattery, new byte[0], 16), CmdBattery, 900);
                    if (response == null)
                    {
                        r.Error = "设备休眠 / 未应答";
                        return r;
                    }
                    if (response.Length < 10)
                    {
                        r.Error = "报文过短";
                        return r;
                    }
                    int percent = response[6];
                    if (percent > 100)
                    {
                        r.Error = "电量读数异常";
                        r.Detail = Hex(response);
                        return r;
                    }
                    r.Percent = percent;
                    r.Charging = response[7] == 0x01;
                    r.Millivolts = (response[8] << 8) | response[9];
                    r.Online = true;
                    r.Detail = r.Millivolts > 0 ? "2.4G · " + r.Millivolts + " mV" : "2.4G";
                    r.HealthText = r.Millivolts > 0
                        ? "电压 " + r.Millivolts + " mV（硬件仅提供瞬时电压，不提供电池健康度）"
                        : "硬件不提供电池健康度（SoH / 循环次数）";
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
            DpiReading r = new DpiReading();
            try
            {
                using (HidSession s = new HidSession(device.HidPath, FrameSize, FrameSize))
                {
                    string sensor = ResolveSensor(s, device);
                    if (string.IsNullOrEmpty(sensor))
                    {
                        r.Error = "未知 CID/MID，DPI 已安全禁用";
                        return r;
                    }
                    byte[] system = ReadEeprom(s, 0x0000, 6);
                    if (system == null || system.Length < 6)
                    {
                        r.Error = "读取档位信息失败";
                        return r;
                    }
                    int stageCount = Math.Min(6, Math.Max(1, (int)system[2]));
                    int stage = Math.Min(stageCount, Math.Max(1, system[4] + 1));
                    int len = AtkCodecs.StageLength(sensor);
                    int address = DpiAddress(sensor, stage - 1);
                    byte[] row = ReadEeprom(s, address, len);
                    int x, y;
                    if (row == null || !AtkCodecs.Unpack(sensor, row, out x, out y))
                    {
                        r.Error = "读取 DPI 失败或校验不通过";
                        return r;
                    }
                    r.X = x;
                    r.Y = y;
                    r.Stage = stage;
                    r.StageCount = stageCount;
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }
            return r;
        }

        public override bool WriteDpi(DeviceInfo device, int x, int y, int stage)
        {
            using (HidSession s = new HidSession(device.HidPath, FrameSize, FrameSize))
            {
                string sensor = ResolveSensor(s, device);
                if (string.IsNullOrEmpty(sensor)) return false;
                byte[] system = ReadEeprom(s, 0x0000, 6);
                if (system == null || system.Length < 6) return false;
                int stageCount = Math.Min(6, Math.Max(1, (int)system[2]));
                int index = stage <= 0 ? Math.Min(stageCount, Math.Max(1, system[4] + 1)) - 1 : stage - 1;
                if (index < 0 || index >= stageCount) return false;

                x = AtkCodecs.Clamp(sensor, x);
                y = AtkCodecs.Clamp(sensor, y);
                byte[] row = AtkCodecs.Pack(sensor, x, y);
                if (row == null) return false;

                int address = DpiAddress(sensor, index);
                if (!WriteEeprom(s, address, row)) return false;
                byte[] confirm = ReadEeprom(s, address, row.Length);
                int rx, ry;
                if (confirm == null || !AtkCodecs.Unpack(sensor, confirm, out rx, out ry) || rx != x || ry != y)
                    return false;

                if (system[4] != index)
                {
                    byte[] next = (byte[])system.Clone();
                    next[4] = (byte)index;
                    if (!WriteEeprom(s, 0x0000, next)) return false;
                    byte[] confirmSystem = ReadEeprom(s, 0x0000, 6);
                    if (confirmSystem == null || confirmSystem[4] != index) return false;
                }
                return true;
            }
        }

        private static int DpiAddress(string sensor, int index)
        {
            AtkCodecs.Profile p = AtkCodecs.GetProfile(sensor);
            if (p != null && p.Family == AtkCodecs.Family.Paw3955Master)
                return 0x1B00 + index * 6;
            return 0x000C + index * 4;
        }

        private string ResolveSensor(HidSession s, DeviceInfo device)
        {
            string cached;
            if (IdentityCache.TryGetValue(device.HidPath, out cached))
            {
                device.Sensor = cached;
                ApplySensorLimits(device, cached);
                return cached;
            }

            byte[] response = Exchange(s, Build(CmdIdentity, new byte[] { 0 }, 16), CmdIdentity, 1200);
            if (response == null || response.Length < 7)
            {
                device.Sensor = "";
                device.DpiNote = "CID/MID 无应答，DPI 已禁用";
                return "";
            }
            string key = response[5].ToString() + "," + response[6].ToString();
            string sensor;
            if (!SensorMap.TryGetValue(key, out sensor) || string.IsNullOrEmpty(sensor))
            {
                device.Sensor = "";
                device.DpiNote = "CID/MID " + key + " 未识别，DPI 已禁用";
                return "";
            }
            IdentityCache[device.HidPath] = sensor;
            device.Sensor = sensor;
            ApplySensorLimits(device, sensor);
            return sensor;
        }

        private static void ApplySensorLimits(DeviceInfo device, string sensor)
        {
            AtkCodecs.Profile p = AtkCodecs.GetProfile(sensor);
            if (p == null) return;
            device.Sensor = sensor;
            device.DpiMin = p.Min;
            device.DpiMax = p.Max;
            device.DpiStep = p.Family == AtkCodecs.Family.Stepped ? p.XStep : (p.Family == AtkCodecs.Family.Paw3955Master ? 10 : 10);
            device.DpiNote = "传感器 " + sensor;
        }

        private static byte[] ReadEeprom(HidSession s, int address, int length)
        {
            if (length < 1 || length > 8) return null;
            byte[] body = new byte[] { 0, (byte)(address >> 8), (byte)(address & 0xff), (byte)length };
            byte[] frame = Build(CmdReadEeprom, body, 16);
            byte[] response = Exchange(s, frame, CmdReadEeprom, 600);
            if (response == null || response.Length < 5) return null;
            if (response[2] != (byte)(address >> 8) || response[3] != (byte)(address & 0xff)) return null;
            int declared = response[4];
            if (declared < length) return null;
            byte[] data = new byte[length];
            Array.Copy(response, 5, data, 0, length);
            return data;
        }

        private static bool WriteEeprom(HidSession s, int address, byte[] data)
        {
            if (data == null || data.Length < 1 || data.Length > 8) return false;
            byte[] body = new byte[4 + data.Length];
            body[0] = 0;
            body[1] = (byte)(address >> 8);
            body[2] = (byte)(address & 0xff);
            body[3] = (byte)data.Length;
            Array.Copy(data, 0, body, 4, data.Length);
            byte[] frame = Build(CmdWriteEeprom, body, 16);
            byte[] response = Exchange(s, frame, CmdWriteEeprom, 600);
            if (response == null || response.Length < 5 + data.Length) return false;
            if (response[2] != body[1] || response[3] != body[2] || response[4] != body[3]) return false;
            for (int i = 0; i < data.Length; i++)
            {
                if (response[5 + i] != data[i]) return false;
            }
            return true;
        }

        private static byte[] Build(byte cmd, byte[] body, int payloadLength)
        {
            byte[] frame = new byte[FrameSize];
            frame[0] = ReportId;
            frame[1] = cmd;
            if (body != null)
            {
                int copy = Math.Min(body.Length, payloadLength - 2);
                Array.Copy(body, 0, frame, 2, copy);
            }
            int sum = 0;
            for (int i = 0; i < FrameSize - 1; i++) sum += frame[i];
            frame[FrameSize - 1] = (byte)((ChecksumTarget - (sum & 0xff)) & 0xff);
            return frame;
        }

        private static byte[] Exchange(HidSession s, byte[] request, byte expectedCmd, int timeoutMs)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!s.SetOutputReport(request)) return null;
                long until = Environment.TickCount + timeoutMs;
                while (true)
                {
                    int left = (int)(until - Environment.TickCount);
                    if (left <= 0) break;
                    byte[] response = s.Read(Math.Min(60, left));
                    if (response != null && response.Length == FrameSize && IsValidFrame(response) &&
                        response[0] == ReportId && (response[1] == expectedCmd || response[1] == CmdWriteEeprom))
                        return response;
                }
            }
            return null;
        }

        private static bool IsValidFrame(byte[] frame)
        {
            int sum = 0;
            for (int i = 0; i < frame.Length; i++) sum += frame[i];
            return (sum & 0xff) == ChecksumTarget;
        }

        private static string Hex(byte[] b)
        {
            if (b == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("X2")).Append(' ');
            return sb.ToString().Trim();
        }

        // ATK Hub 3.2.27 device table, reduced to CID/MID pairs whose sensor is
        // unambiguous. Ambiguous pairs are refused rather than guessed.
        private static Dictionary<string, string> BuildSensorMap()
        {
            string table =
                "0,0=PAW3395Ultra;1,2=PAW3950Ultra;1,3=PAW3950Ultra;1,5=PAW3950Ultra;" +
                "1,7=PAW3950Ultra;1,11=PAW3395Ultra;1,14=PAW3955Master;1,16=PAW3950Ultra;" +
                "1,19=PAW3950Ultra;1,20=PAW3395Ultra;1,22=PAW3950Ultra;1,23=PAW3950Ultra;" +
                "1,24=PAW3950Ultra;1,25=PAW3950Ultra;1,26=PAW3950Ultra;1,27=PAW3950Ultra;" +
                "1,28=PAW3950Ultra;1,30=PAW3950Ultra;1,31=PAW3955Master;1,32=PAW3950Ultra;" +
                "1,33=PAW3950Ultra;1,37=PAW3950Ultra;1,38=PAW3950Ultra;1,39=PAW3955Master;" +
                "1,40=PAW3955Master;1,42=PAW3955Master;1,45=PAW3955Master;1,46=PAW3955Master;" +
                "1,47=PAW3955Master;1,48=PAW3955Master;1,49=PAW3955Master;1,50=PAW3955Master;" +
                "1,51=PAW3955Master;1,52=PAW3955Master;1,53=PAW3955Master;1,54=PAW3955Master;" +
                "1,55=PAW3955Master;1,58=PAW3955Master;1,59=PAW3955Master;1,60=PAW3955Master;" +
                "1,61=PAW3955Master;1,62=PAW3955Master;1,63=PAW3955Master;1,64=PAW3955Master;" +
                "1,66=PAW3955Master;1,67=PAW3955Master;1,71=PAW3955Master;1,75=PAW3955Master;" +
                "1,76=PAW3955Master;1,85=PAW3950Ultra;1,87=PAW3950DM;" +
                "2,4=PAW3395Ultra;2,6=PAW3395SE;2,8=PAW3395SE;2,9=PAW3311;" +
                "2,11=PAW3395;2,12=PAW3395;2,13=PAW3950Ultra;2,14=PAW3950Ultra;" +
                "2,15=CORE26K;2,16=PAW3395SE;2,17=PAW3950Ultra;2,21=PAW3315;" +
                "2,22=CORE26K;2,23=PAW3950Ultra;2,25=PAW3950Ultra;2,26=PAW3950DM;" +
                "2,27=PAW3395;2,28=CORE26K;2,30=CORE26K;2,32=PAW3315;" +
                "2,33=CORE26K;2,34=PAW3950Ultra;2,35=PAW3950Ultra;2,36=PAW3950;" +
                "2,37=PAW3950;2,38=PAW3950;2,39=PAW3950;2,40=PAW3950;2,41=PAW3950;" +
                "2,42=PAW3950Ultra;2,43=PAW3950Ultra;2,44=PAW3950Ultra;2,45=PAW3950Ultra;" +
                "2,46=PAW3950Ultra;2,47=PAW3950;2,48=PAW3395;2,49=PAW3395;" +
                "2,50=PAW3950Ultra;2,52=PAW3395;2,53=PAW3950Ultra;2,54=PAW3395;" +
                "2,55=PAW3950;2,56=PAW3950Ultra;2,57=PAW3395SE;2,63=PAW3311;" +
                "2,64=PAW3950;2,65=PAW3950Ultra;2,66=PAW3950;2,67=PAW3395SE;" +
                "2,70=PAW3950Ultra;2,72=PAW3311;2,73=PAW3395SE;2,76=PAW3395;" +
                "2,77=PAW3395SE;2,78=PAW3395;2,79=PAW3395SE;2,83=PAW3395;" +
                "2,84=PAW3395SE;2,85=PAW3395Ultra;2,87=PAW3950Ultra;2,88=PAW3311;" +
                "2,89=PAW3395;2,90=PAW3395SE;2,92=PAW3311;2,93=PAW3395;" +
                "2,94=PAW3395SE;2,95=PAW3311;2,96=PAW3395;2,97=PAW3395SE;" +
                "2,98=PAW3311;2,99=PAW3395SE;2,100=PAW3395Ultra;2,107=PAW3395SE;" +
                "2,108=PAW3950;2,109=PAW3395SE;2,110=PAW3395Ultra;2,114=PAW3395;" +
                "2,115=PAW3950Ultra;2,117=PAW3311;2,120=PAW3395SE;2,121=PAW3315;" +
                "2,122=PAW3395SE;2,123=PAW3395SE;2,124=PAW3950Ultra;2,125=PAW3950Ultra;" +
                "2,126=PAW3950Ultra;2,127=PAW3950Ultra;2,128=PAW3950Ultra;" +
                "2,129=PAW3955Master;2,130=PAW3395SE;2,131=PAW3950Ultra;" +
                "2,132=PAW3950Ultra;2,133=PAW3950Ultra;4,2=PAW3395Ultra;" +
                // Hand-verified overrides where the vendor table is ambiguous.
                "1,8=PAW3950Ultra;2,83=PAW3395;2,27=PAW3395;2,32=PAW3395SE";

            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] parts = table.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0) continue;
                map[parts[i].Substring(0, eq)] = parts[i].Substring(eq + 1);
            }
            return map;
        }
    }
}
