using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace HyzMouseHub
{
    // Razer HyperSpeed / wired mice: 90-byte vendor feature report (91 bytes on
    // Windows, including report id 0). Battery: class 0x07 / id 0x80 (0..255).
    // DPI: class 0x04 / id 0x85 read, 0x05 write, with a read-back confirmation.
    // Transaction ids are per product id; a wrong id is silent, never an error.
    internal sealed class RazerProvider : DeviceProviderBase
    {
        private const ushort RazerVid = 0x1532;
        private const int ReportLength = 91;
        private const byte StatusOk = 0x02;
        private const byte StatusUnsupported = 0x05;

        private sealed class Meta
        {
            public readonly string Model;
            public readonly byte Transaction;
            public readonly byte DpiStore;

            public Meta(string model, byte transaction, byte dpiStore = 0x01)
            {
                Model = model;
                Transaction = transaction;
                DpiStore = dpiStore;
            }
        }

        private sealed class RazerReply
        {
            public readonly byte Status;
            public readonly byte[] Args;

            public RazerReply(byte status, byte[] args)
            {
                Status = status;
                Args = args;
            }
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<ushort, Meta> Known = new Dictionary<ushort, Meta>
        {
            { 0x00A5, new Meta("Viper V2 Pro", 0x1F) },
            { 0x00A6, new Meta("Viper V2 Pro", 0x1F) },
            { 0x00C0, new Meta("Viper V3 Pro", 0x1F) },
            { 0x00C1, new Meta("Viper V3 Pro", 0x1F) },
            { 0x006E, new Meta("DeathAdder Essential", 0x3F) },
            { 0x0071, new Meta("DeathAdder Essential White Edition", 0x3F) },
            { 0x0098, new Meta("DeathAdder Essential (2021)", 0x3F) },
            { 0x0015, new Meta("Naga", 0xFF) },
            { 0x001F, new Meta("Naga Epic", 0x3F) },
            { 0x0020, new Meta("Abyssus 1800", 0xFF) },
            { 0x0024, new Meta("Mamba 2012 (Wired)", 0xFF) },
            { 0x0025, new Meta("Mamba 2012", 0xFF) },
            { 0x002E, new Meta("Naga 2012", 0xFF) },
            { 0x002F, new Meta("Imperator 2012", 0xFF) },
            { 0x0032, new Meta("Ouroboros 2012", 0xFF) },
            { 0x0034, new Meta("Taipan", 0xFF) },
            { 0x0036, new Meta("Naga Hex (Red)", 0xFF) },
            { 0x0037, new Meta("DeathAdder 2013", 0xFF) },
            { 0x0038, new Meta("DeathAdder 1800", 0xFF) },
            { 0x0039, new Meta("Orochi 2013", 0xFF) },
            { 0x003E, new Meta("Naga Epic Chroma (Wired)", 0xFF) },
            { 0x003F, new Meta("Naga Epic Chroma", 0x3F) },
            { 0x0040, new Meta("Naga 2014", 0xFF) },
            { 0x0041, new Meta("Naga Hex", 0xFF) },
            { 0x0042, new Meta("Abyssus 2014", 0xFF) },
            { 0x0043, new Meta("DeathAdder Chroma", 0xFF) },
            { 0x0044, new Meta("Mamba (Wired)", 0xFF) },
            { 0x0045, new Meta("Mamba", 0xFF) },
            { 0x0046, new Meta("Mamba Tournament Edition", 0xFF) },
            { 0x0048, new Meta("Orochi (Wired)", 0xFF) },
            { 0x004C, new Meta("Diamondback Chroma", 0xFF) },
            { 0x004F, new Meta("DeathAdder 2000", 0xFF) },
            { 0x0050, new Meta("Naga Hex V2", 0x3F) },
            { 0x0053, new Meta("Naga Chroma", 0xFF) },
            { 0x0054, new Meta("DeathAdder 3500", 0xFF) },
            { 0x0059, new Meta("Lancehead (Wired)", 0x3F) },
            { 0x005A, new Meta("Lancehead", 0x3F) },
            { 0x005B, new Meta("Abyssus V2", 0xFF) },
            { 0x005C, new Meta("DeathAdder Elite", 0x3F) },
            { 0x005E, new Meta("Abyssus 2000", 0xFF) },
            { 0x0060, new Meta("Lancehead Tournament Edition", 0x3F) },
            { 0x0064, new Meta("Basilisk", 0x3F) },
            { 0x0065, new Meta("Basilisk Essential", 0x3F) },
            { 0x0067, new Meta("Naga Trinity", 0xFF) },
            { 0x006A, new Meta("Abyssus Elite (D.Va Edition)", 0xFF) },
            { 0x006B, new Meta("Abyssus Essential", 0xFF) },
            { 0x006C, new Meta("Mamba Elite", 0x1F) },
            { 0x0084, new Meta("DeathAdder V2", 0x3F, 0x00) },
            { 0x0085, new Meta("Basilisk V2", 0x1F) },
            { 0x008C, new Meta("DeathAdder V2 Mini", 0x3F, 0x00) },
            { 0x008D, new Meta("Naga Left-Handed Edition 2020", 0x1F) },
            { 0x0091, new Meta("Viper 8KHz", 0xFF) },
            { 0x00A1, new Meta("DeathAdder V2 Lite", 0x1F) },
            { 0x00B2, new Meta("DeathAdder V3", 0x1F) },
            { 0x0096, new Meta("Naga X", 0x1F) },
            { 0x0099, new Meta("Basilisk V3", 0x1F) },
            { 0x00CB, new Meta("Basilisk V3 35K", 0x1F) },
            { 0x0062, new Meta("Atheris", 0x1F) },
            { 0x0094, new Meta("Orochi V2", 0x1F) },
            { 0x007A, new Meta("Viper Ultimate (Wired)", 0x3F) },
            { 0x007B, new Meta("Viper Ultimate", 0x3F) },
            { 0x007C, new Meta("DeathAdder V2 Pro (Wired)", 0x3F) },
            { 0x007D, new Meta("DeathAdder V2 Pro", 0x3F) },
            { 0x009E, new Meta("Viper Mini Signature Edition (Wired)", 0x1F) },
            { 0x009F, new Meta("Viper Mini Signature Edition", 0x1F) },
            { 0x00B8, new Meta("Viper V3 HyperSpeed", 0x1F) },
            { 0x00DE, new Meta("Viper V3 Pro SE (Wired)", 0x1F) },
            { 0x00DF, new Meta("Viper V3 Pro SE", 0x1F) },
            { 0x006F, new Meta("Lancehead Wireless", 0x3F) },
            { 0x0070, new Meta("Lancehead Wireless (Wired)", 0x3F) },
            { 0x0072, new Meta("Mamba Wireless", 0x3F) },
            { 0x0073, new Meta("Mamba Wireless (Wired)", 0x3F) },
            { 0x0077, new Meta("Pro Click", 0x1F) },
            { 0x0080, new Meta("Pro Click (Wired)", 0x1F) },
            { 0x0083, new Meta("Basilisk X HyperSpeed", 0xFF) },
            { 0x0086, new Meta("Basilisk Ultimate (Wired)", 0x1F) },
            { 0x0088, new Meta("Basilisk Ultimate", 0x1F) },
            { 0x008F, new Meta("Naga Pro (Wired)", 0x1F) },
            { 0x0090, new Meta("Naga Pro", 0x1F) },
            { 0x009A, new Meta("Pro Click Mini", 0x1F) },
            { 0x009C, new Meta("DeathAdder V2 X HyperSpeed", 0x1F) },
            { 0x00A7, new Meta("Naga V2 Pro (Wired)", 0x1F) },
            { 0x00A8, new Meta("Naga V2 Pro", 0x1F) },
            { 0x00AA, new Meta("Basilisk V3 Pro (Wired)", 0x1F) },
            { 0x00AB, new Meta("Basilisk V3 Pro", 0x1F) },
            { 0x00AF, new Meta("Cobra Pro (Wired)", 0x1F) },
            { 0x00B0, new Meta("Cobra Pro", 0x1F) },
            { 0x00B4, new Meta("Naga V2 HyperSpeed", 0x1F) },
            { 0x00B6, new Meta("DeathAdder V3 Pro (Wired)", 0x1F) },
            { 0x00B7, new Meta("DeathAdder V3 Pro", 0x1F) },
            { 0x00B9, new Meta("Basilisk V3 X HyperSpeed", 0x1F) },
            { 0x00BE, new Meta("DeathAdder V4 Pro (Wired)", 0x1F) },
            { 0x00BF, new Meta("DeathAdder V4 Pro", 0x1F) },
            { 0x00EF, new Meta("DeathAdder V4 Pro Carbon Fiber Edition (Wired)", 0x1F) },
            { 0x00F0, new Meta("DeathAdder V4 Pro Carbon Fiber Edition", 0x1F) },
            { 0x00C2, new Meta("DeathAdder V3 Pro (Wired)", 0x1F) },
            { 0x00C3, new Meta("DeathAdder V3 Pro", 0x1F) },
            { 0x00C4, new Meta("DeathAdder V3 HyperSpeed (Wired)", 0x1F) },
            { 0x00C5, new Meta("DeathAdder V3 HyperSpeed", 0x1F) },
            { 0x00C7, new Meta("Pro Click V2 Vertical Edition (Wired)", 0x1F) },
            { 0x00C8, new Meta("Pro Click V2 Vertical Edition", 0x1F) },
            { 0x00CC, new Meta("Basilisk V3 Pro 35K (Wired)", 0x1F) },
            { 0x00CD, new Meta("Basilisk V3 Pro 35K", 0x1F) },
            { 0x00D0, new Meta("Pro Click V2 (Wired)", 0x1F) },
            { 0x00D1, new Meta("Pro Click V2", 0x1F) },
            { 0x00D3, new Meta("Basilisk Mobile (Wired)", 0x1F) },
            { 0x00D4, new Meta("Basilisk Mobile", 0x1F) },
            { 0x00D6, new Meta("Basilisk V3 Pro 35K Phantom Green (Wired)", 0x1F) },
            { 0x00D7, new Meta("Basilisk V3 Pro 35K Phantom Green", 0x1F) },
            { 0x008A, new Meta("Viper Mini", 0xFF, 0x00) },
            { 0x00A3, new Meta("Cobra", 0xFF, 0x00) },
        };
        private static readonly Dictionary<string, string[]> Interfaces = new Dictionary<string, string[]>();

        public override string Name
        {
            get { return "Razer HID"; }
        }

        public override List<DeviceInfo> Discover(List<HidDeviceDescriptor> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();
            Dictionary<ushort, List<string>> groups = new Dictionary<ushort, List<string>>();
            Dictionary<ushort, string> productNames = new Dictionary<ushort, string>();

            foreach (HidDeviceDescriptor h in catalog)
            {
                if (h.VendorId != RazerVid) continue;
                // Razer does not declare the control report in its HID descriptor;
                // accept the plain mouse collection as well as 90/91-byte reports.
                bool control = h.FeatureReportLength >= 90 || h.OutputReportLength >= 90 ||
                               (h.UsagePage == 0x01 && h.Usage == 0x02);
                if (!control) continue;

                List<string> paths;
                if (!groups.TryGetValue(h.ProductId, out paths))
                {
                    paths = new List<string>();
                    groups.Add(h.ProductId, paths);
                }
                if (!paths.Contains(h.Path)) paths.Add(h.Path);
                if (!productNames.ContainsKey(h.ProductId))
                    productNames[h.ProductId] = h.ProductName ?? "";
            }

            foreach (KeyValuePair<ushort, List<string>> kv in groups)
            {
                ushort pid = kv.Key;
                string[] paths = kv.Value.ToArray();
                Meta meta;
                lock (CacheLock) { Known.TryGetValue(pid, out meta); }

                if (meta == null)
                {
                    byte probed;
                    if (!ProbeTransaction(paths, out probed)) continue;
                    meta = new Meta(CleanName(productNames[pid]), probed);
                    lock (CacheLock) { Known[pid] = meta; }
                }
                else
                {
                    ProbeInterface(pid, meta.Transaction, paths);
                }

                DeviceInfo d = new DeviceInfo();
                d.Id = "RAZER:" + RazerVid.ToString("X4") + ":" + pid.ToString("X4");
                d.Provider = Name;
                d.Brand = "Razer";
                d.Model = meta.Model;
                d.Name = "Razer " + meta.Model;
                d.VendorId = RazerVid;
                d.ProductId = pid;
                d.HidPath = BestPath(d.Id, paths);
                d.SupportsBattery = true;
                d.SupportsDpi = true;
                d.DpiMin = 100;
                d.DpiMax = 35000;
                d.DpiStep = 1;
                d.Sensor = "Razer 90B";
                d.DpiNote = "0x07/0x80 电量 · 0x04/0x85 DPI · 0x04/0x05 写入回读 · 档位只读";

                lock (CacheLock) { Interfaces[d.Id] = (string[])paths.Clone(); }
                found.Add(d);
            }
            return found;
        }

        private static string CleanName(string productName)
        {
            string name = (productName ?? "").Trim();
            if (name.StartsWith("Razer ", StringComparison.OrdinalIgnoreCase)) name = name.Substring(6);
            return string.IsNullOrEmpty(name) ? "Mouse" : name;
        }

        /// <summary>Unknown PID: find which of the three transaction generations answers.</summary>
        private static bool ProbeTransaction(string[] paths, out byte transaction)
        {
            byte[] ids = new byte[] { 0xFF, 0x3F, 0x1F };
            foreach (byte tx in ids)
            {
                foreach (string path in paths)
                {
                    RazerReply reply = Exchange(path, tx, 0x00, 0x81, 0x02, null, 180);
                    if (reply != null && reply.Status == StatusOk)
                    {
                        transaction = tx;
                        return true;
                    }
                }
            }
            transaction = 0;
            return false;
        }

        private static void ProbeInterface(ushort pid, byte tx, string[] paths)
        {
            if (paths.Length < 2) return;
            string id = "RAZER:" + RazerVid.ToString("X4") + ":" + pid.ToString("X4");
            foreach (string path in paths)
            {
                RazerReply reply = Exchange(path, tx, 0x00, 0x81, 0x02, null, 180);
                if (reply != null && reply.Status == StatusOk)
                {
                    Promote(id, path);
                    return;
                }
            }
        }

        private static string BestPath(string id, string[] paths)
        {
            lock (CacheLock)
            {
                string[] saved;
                if (Interfaces.TryGetValue(id, out saved) && saved.Length > 0) return saved[0];
            }
            return paths.Length > 0 ? paths[0] : "";
        }

        private static void Promote(string id, string path)
        {
            lock (CacheLock)
            {
                string[] paths;
                if (!Interfaces.TryGetValue(id, out paths)) return;
                List<string> next = new List<string>();
                next.Add(path);
                foreach (string p in paths) if (p != path) next.Add(p);
                Interfaces[id] = next.ToArray();
            }
        }

        private static RazerReply ExchangeBest(DeviceInfo device, byte cls, byte cmd, byte size, byte[] args, int timeout)
        {
            string[] paths;
            lock (CacheLock)
            {
                if (!Interfaces.TryGetValue(device.Id, out paths)) paths = new string[] { device.HidPath };
                else paths = (string[])paths.Clone();
            }
            if (paths.Length == 0) paths = new string[] { device.HidPath };

            byte tx;
            Meta meta;
            lock (CacheLock) { meta = Known.ContainsKey(device.ProductId) ? Known[device.ProductId] : null; }
            if (meta == null) return null;
            tx = meta.Transaction;

            RazerReply soft = null;
            foreach (string path in paths)
            {
                RazerReply reply = Exchange(path, tx, cls, cmd, size, args, timeout);
                if (reply == null) continue;
                if (reply.Status == StatusOk)
                {
                    if (BestPath(device.Id, paths) != path) Promote(device.Id, path);
                    return reply;
                }
                if (reply.Status == StatusUnsupported) return reply;
                if (soft == null) soft = reply;
            }
            return soft;
        }

        public override BatteryReading ReadBattery(DeviceInfo device)
        {
            BatteryReading r = new BatteryReading();
            r.UpdatedAt = DateTime.Now;
            RazerReply reply = ExchangeBest(device, 0x07, 0x80, 0x02, null, 900);
            if (reply == null) { r.Error = "设备休眠"; return r; }
            if (reply.Status == StatusUnsupported) { r.Error = "该型号不支持电量查询"; return r; }
            if (reply.Status != StatusOk) { r.Error = "设备未应答 0x" + reply.Status.ToString("X2"); return r; }
            if (reply.Args.Length < 2) { r.Error = "应答长度异常"; return r; }

            int raw = reply.Args[1];
            int percent = (int)Math.Round(raw * 100.0 / 255.0);
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            r.Percent = percent;
            r.Online = true;
            r.Detail = "Razer 2.4G";

            // Some AA-powered models do not implement the charging query; ignore it.
            RazerReply charge = ExchangeBest(device, 0x07, 0x84, 0x02, null, 400);
            if (charge != null && charge.Status == StatusOk && charge.Args.Length >= 2)
                r.Charging = charge.Args[1] == 1 || (charge.Args[0] == 1 && charge.Args[1] == 0);
            return r;
        }

        public override DpiReading ReadDpi(DeviceInfo device)
        {
            DpiReading r = new DpiReading();
            byte store = DpiStoreOf(device);
            RazerReply reply = ExchangeBest(device, 0x04, 0x85, 0x07, new byte[] { store }, 900);
            if (reply == null) { r.Error = "设备休眠"; return r; }
            if (reply.Status == StatusUnsupported) { r.Error = "该型号不支持 DPI 读取"; return r; }
            if (reply.Status != StatusOk) { r.Error = "设备未应答 0x" + reply.Status.ToString("X2"); return r; }
            if (reply.Args.Length < 5) { r.Error = "DPI 应答长度异常"; return r; }

            r.X = (reply.Args[1] << 8) | reply.Args[2];
            r.Y = (reply.Args[3] << 8) | reply.Args[4];

            // Stage table is read-only here: writing it has a fixed 56-byte layout
            // and a mistake can corrupt stored settings, so this tool never sends it.
            RazerReply stages = ExchangeBest(device, 0x04, 0x86, 0x38, new byte[] { store }, 500);
            if (stages != null && stages.Status == StatusOk && stages.Args.Length > 2 && stages.Args[2] > 0)
            {
                r.Stage = stages.Args[1];
                r.StageCount = stages.Args[2];
            }
            return r;
        }

        public override bool WriteDpi(DeviceInfo device, int x, int y, int stage)
        {
            if (x < 100 || x > 35000 || y < 100 || y > 35000) return false;
            byte store = DpiStoreOf(device);
            byte[] args = new byte[]
            {
                store,
                (byte)(x >> 8), (byte)(x & 0xFF),
                (byte)(y >> 8), (byte)(y & 0xFF),
                0x00, 0x00
            };
            RazerReply reply = ExchangeBest(device, 0x04, 0x05, 0x07, args, 1000);
            if (reply == null || reply.Status != StatusOk) return false;

            Thread.Sleep(80);
            DpiReading check = ReadDpi(device);
            return check.X == x && check.Y == y;
        }

        private static byte DpiStoreOf(DeviceInfo device)
        {
            Meta meta;
            lock (CacheLock) { meta = Known.ContainsKey(device.ProductId) ? Known[device.ProductId] : null; }
            return meta == null ? (byte)0x01 : meta.DpiStore;
        }

        private static byte[] BuildReport(byte tx, byte cls, byte cmd, byte size, byte[] args)
        {
            byte[] buf = new byte[ReportLength];
            buf[0] = 0x00;   // report id on Windows
            buf[1] = 0x00;   // status: new command
            buf[2] = tx;
            buf[3] = 0x00;   // remaining packets hi
            buf[4] = 0x00;   // remaining packets lo
            buf[5] = 0x00;   // protocol type
            buf[6] = size;
            buf[7] = cls;
            buf[8] = cmd;
            if (args != null) Array.Copy(args, 0, buf, 9, Math.Min(args.Length, 80));

            byte crc = 0;
            for (int i = 3; i <= 88; i++) crc ^= buf[i];
            buf[89] = crc;
            return buf;
        }

        private static RazerReply Exchange(string path, byte tx, byte cls, byte cmd, byte size, byte[] args, int timeout)
        {
            try
            {
                using (RazerSession session = new RazerSession(path))
                {
                    if (!session.SetFeature(BuildReport(tx, cls, cmd, size, args))) return null;
                    byte[] buf = new byte[ReportLength];
                    int deadline = Environment.TickCount + Math.Max(20, timeout);
                    while (Environment.TickCount < deadline)
                    {
                        if (session.GetFeature(buf))
                        {
                            byte status = buf[1];
                            bool terminal = status != 0x00 && status != 0x01;
                            if (terminal && buf[2] == tx && buf[7] == cls && buf[8] == cmd)
                            {
                                byte[] arguments = new byte[80];
                                Array.Copy(buf, 9, arguments, 0, 80);
                                return new RazerReply(status, arguments);
                            }
                        }
                        Thread.Sleep(6);
                    }
                }
            }
            catch { }
            return null;
        }

        private sealed class RazerSession : IDisposable
        {
            private readonly IntPtr _handle;

            public RazerSession(string path)
            {
                _handle = HidNative.CreateFile(path, HidNative.GenericWrite, HidNative.FileShareRw,
                    IntPtr.Zero, HidNative.OpenExisting, 0, IntPtr.Zero);
                if (_handle == new IntPtr(-1))
                    throw new InvalidOperationException("无法打开雷蛇 HID 接口");
            }

            public bool SetFeature(byte[] report)
            {
                return HidNative.HidD_SetFeature(_handle, report, report.Length);
            }

            public bool GetFeature(byte[] report)
            {
                return HidNative.HidD_GetFeature(_handle, report, report.Length);
            }

            public void Dispose()
            {
                if (_handle != IntPtr.Zero && _handle != new IntPtr(-1)) HidNative.CloseHandle(_handle);
            }
        }
    }
}
