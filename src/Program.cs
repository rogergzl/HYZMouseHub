using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HyzMouseHub
{
    internal static class Program
    {
        /// <summary>第二实例启动时用它唤醒已运行实例的主界面。</summary>
        internal const string ShowEventName = @"Local\HYZMouseHub_ShowSettings";
        private const string SingleInstanceName = @"Local\HYZMouseHub_SingleInstance";
        // 静态持有：Mutex 被 GC 回收会提前释放，导致单实例保护失效。
        private static Mutex _instanceMutex;

        [STAThread]
        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch { }

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                try
                {
                    string dir = AppConfig.ConfigDir;
                    File.AppendAllText(Path.Combine(dir, "error.log"),
                        DateTime.Now.ToString("s") + " " + e.ExceptionObject + Environment.NewLine);
                }
                catch { }
            };

            try { EnableDpiAwareness(); }
            catch { }

            if (args != null && args.Length > 0)
            {
                string first = args[0].ToLowerInvariant();
                if (first == "--help" || first == "-h" || first == "/?")
                {
                    PrintHelp();
                    return 0;
                }
                if (first == "--list")
                {
                    PrintHidList();
                    return 0;
                }
                if (first == "--test" || first == "--test-all")
                {
                    PrintDeviceTest(first == "--test-all");
                    return 0;
                }
                if (first == "--watch" || first.StartsWith("--watch="))
                {
                    int gap = 3;
                    if (first.Length > 8) int.TryParse(first.Substring(8), out gap);
                    PrintWatch(gap < 1 ? 1 : gap);
                    return 0;
                }
                if (first == "--burst" || first.StartsWith("--burst="))
                {
                    // 语法 --burst[=秒[:blind]]；blind 表示不看设备结果，用来看 5 秒节奏与窗口到期。
                    int span = StartupBurst.WindowSeconds + 2;
                    bool blind = first.IndexOf(":blind", StringComparison.OrdinalIgnoreCase) > 0;
                    string value = first.Replace(":blind", "").Replace(":BLIND", "");
                    if (value.Length > 8) int.TryParse(value.Substring(8), out span);
                    PrintBurst(span < 5 ? 5 : span, blind);
                    return 0;
                }
                if (first.StartsWith("--dpi="))
                {
                    return SetDpiFromArgs(first.Substring(6));
                }
                if (first.StartsWith("--probe="))
                {
                    return PrintProbe(first.Substring(8));
                }
                if (first.StartsWith("--make-icon="))
                {
                    string target = args[0].Substring(12);
                    AppIcon.SaveIco(target);
                    Console.WriteLine("已生成图标: " + Path.GetFullPath(target));
                    return 0;
                }
                if (first.StartsWith("--app-icon-preview="))
                {
                    string target = args[0].Substring(19);
                    AppIcon.SavePreview(target);
                    Console.WriteLine("已生成图标预览: " + Path.GetFullPath(target));
                    return 0;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(true);
            // 启动顺序即依赖方向：配置 → 状态中间层 → 设备服务 → 界面。
            AppConfig config = AppConfig.Load();
            AppState state = new AppState(config);
            DeviceService service = new DeviceService(state);

            bool autostart = false;
            bool bootStart = false;
            string snapshotPath = null;
            string iconPreviewPath = null;
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--autostart", StringComparison.OrdinalIgnoreCase))
                {
                    autostart = true;
                    bootStart = true;
                }
                else if (string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase))
                {
                    autostart = true;
                }
                else if (arg.StartsWith("--snapshot=", StringComparison.OrdinalIgnoreCase))
                {
                    snapshotPath = arg.Substring(11);
                    autostart = true;
                }
                else if (arg.StartsWith("--icon-preview=", StringComparison.OrdinalIgnoreCase))
                {
                    iconPreviewPath = arg.Substring(15);
                    autostart = true;
                }
            }

            // 诊断 / 截图命令允许与正在运行的托盘程序并存；正式 GUI 实例才做单实例保护。
            bool diagnosticsOnly = !string.IsNullOrEmpty(snapshotPath) || !string.IsNullOrEmpty(iconPreviewPath);
            if (!diagnosticsOnly)
            {
                bool createdNew;
                _instanceMutex = new Mutex(true, SingleInstanceName, out createdNew);
                if (!createdNew)
                {
                    // 已有实例：通知它显示主界面后退出，避免双份托盘图标抢 HID。
                    _instanceMutex.Close();
                    _instanceMutex = null;
                    try
                    {
                        using (EventWaitHandle evt = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                            evt.Set();
                    }
                    catch { }
                    return 0;
                }
            }

            if (!string.IsNullOrEmpty(iconPreviewPath))
            {
                TrayApp preview = new TrayApp(service, state, true, false);
                System.Windows.Forms.Timer pt = new System.Windows.Forms.Timer();
                pt.Interval = 600;
                pt.Tick += delegate
                {
                    pt.Stop();
                    pt.Dispose();
                    try { preview.CaptureIconPreview(iconPreviewPath); }
                    catch (Exception ex)
                    {
                        try { Console.WriteLine(ex.ToString()); } catch { }
                        preview.ExitThread();
                    }
                };
                pt.Start();
                Application.Run(preview);
                return 0;
            }

            if (!string.IsNullOrEmpty(snapshotPath))
            {
                TrayApp snap = new TrayApp(service, state, true, false);
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 900;
                t.Tick += delegate
                {
                    t.Stop();
                    t.Dispose();
                    try { snap.CaptureSettingsSnapshot(snapshotPath); }
                    catch (Exception ex)
                    {
                        try { Console.WriteLine(ex.ToString()); } catch { }
                        snap.ExitThread();
                    }
                };
                t.Start();
                Application.Run(snap);
                return 0;
            }

            // 开机自启：先开快速扫描窗口（前 60 秒每 5 秒一次），
            // 手动静默启动和诊断截图不走这条路径。
            if (bootStart) state.BeginStartupScan();

            Application.Run(new TrayApp(service, state, autostart));
            return 0;
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern IntPtr SetProcessDpiAwarenessContext(IntPtr context);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        private static void EnableDpiAwareness()
        {
            // Per-Monitor DPI Awareness v2: 避免 Windows 位图拉伸导致界面文字发虚。
            if (SetProcessDpiAwarenessContext(new IntPtr(-4)) != IntPtr.Zero) return;
            // Windows 8.1 / 10 回退。
            if (SetProcessDpiAwareness(2) == 0) return;
            // Windows 7 回退。
            SetProcessDPIAware();
        }

        private static void PrintHelp()
        {
            Console.WriteLine("HYZ Mouse Hub " + typeof(Program).Assembly.GetName().Version.ToString(3));
            Console.WriteLine();
            Console.WriteLine("无参数          启动托盘并弹出设置窗口");
            Console.WriteLine("--autostart     开机自启：静默最小化到托盘");
            Console.WriteLine("--minimized     同上，手动静默启动");
            Console.WriteLine("--list          列出全部 HID 接口");
            Console.WriteLine("--test          扫描 ATK / 雷蛇设备并读取电量");
            Console.WriteLine("--test-all      同时读取 DPI（会唤醒设备）");
            Console.WriteLine("--watch[=秒]    连续刷新 8 次，观察休眠保留窗口（默认间隔 3 秒）");
            Console.WriteLine("--burst[=秒[:blind]]  模拟开机自启，逐秒打印生效间隔；blind 忽略设备结果");
            Console.WriteLine("--dpi=ID,X,Y,STAGE  写入 DPI，例如 --dpi=RAZER:1532:00C1,1600,1600,1");
            Console.WriteLine("--probe=VID:PID  导出指定 HID 接口的特征/输入报告（协议适配用）");
            Console.WriteLine("--make-icon=路径  生成多尺寸程序图标 .ico");
            Console.WriteLine("--app-icon-preview=路径  输出图标在不同背景/尺寸下的效果图");
            Console.WriteLine("--help          显示帮助");
        }

        private static void PrintHidList()
        {
            List<HidDeviceDescriptor> all = DeviceService.ListHid();
            Console.WriteLine("VID:PID   UsagePage/Usage  In/Out/Feat  Product");
            Console.WriteLine(new string('-', 96));
            foreach (HidDeviceDescriptor h in all)
            {
                Console.WriteLine("{0,-10} {1:X4}/{2:X4}       {3,2}/{4,2}/{5,3}     {6} · {7}",
                    h.VidPid, h.UsagePage, h.Usage, h.InputReportLength, h.OutputReportLength,
                    h.FeatureReportLength, h.ProductName, h.Manufacturer);
            }
        }

        private static void PrintDeviceTest(bool includeDpi)
        {
            DeviceService service = new DeviceService(new AppState(AppConfig.Load()));
            service.Refresh();
            List<DeviceSnapshot> snapshots = service.Snapshot();
            if (snapshots.Count == 0)
            {
                Console.WriteLine("未检测到 ATK / 雷蛇 2.4G 设备。可先运行 --list 查看 HID 接口。");
                return;
            }
            foreach (DeviceSnapshot s in snapshots)
            {
                Console.WriteLine("{0}  [{1}]", s.Info.ShortName, s.Info.Id);
                Console.WriteLine("  电量: {0}  在线: {1}  充电: {2}  详情: {3} {4}",
                    s.Battery.Percent < 0 ? "--" : s.Battery.Percent + "%",
                    s.Battery.Online, s.Battery.Charging, s.Battery.Detail, s.Battery.Error);
                if (includeDpi)
                {
                    DpiReading d = service.ReadDpi(s.Info);
                    Console.WriteLine("  DPI : {0}  传感器: {1}", d.Text, string.IsNullOrEmpty(s.Info.Sensor) ? s.Info.DpiNote : s.Info.Sensor);
                }
            }
        }

        /// <summary>
        /// 诊断用：模拟开机自启后的快速扫描窗口，逐秒打印当前生效间隔。
        /// 设备在线时会在首个活动读数处提前结束，否则跑满窗口再回到设置间隔。
        /// </summary>
        private static void PrintBurst(int spanSeconds, bool blind)
        {
            AppState state = new AppState(AppConfig.Load());
            DeviceService service = new DeviceService(state);
            state.BeginStartupScan();
            Console.WriteLine("模拟开机自启：前 " + StartupBurst.WindowSeconds + " 秒按 "
                + StartupBurst.IntervalSeconds + " 秒扫描，否则回到设置间隔 "
                + state.RefreshSeconds + " 秒。共观察 " + spanSeconds + " 秒。"
                + (blind ? "（blind：忽略设备结果，仅验证节奏与到期）" : ""));

            int elapsed = 0;
            int nextScan = 0;
            string lastResult = "未扫描";
            while (elapsed < spanSeconds)
            {
                if (elapsed >= nextScan)
                {
                    service.Refresh();
                    if (!blind) state.ReportScan(service.Snapshot());
                    nextScan = elapsed + Math.Max(StartupBurst.IntervalSeconds, state.EffectiveRefreshSeconds);
                    List<DeviceSnapshot> now = service.Snapshot();
                    lastResult = now.Count == 0 ? "未检测到设备" : DescribeDevices(now);
                }
                Console.WriteLine("t={0,3}s  生效间隔={1,2}s  {2}  {3}",
                    elapsed,
                    state.EffectiveRefreshSeconds,
                    state.StartupScanActive ? "快速扫描中（剩 " + state.StartupScanRemainingSeconds + "s）" : "已恢复设置间隔",
                    lastResult);
                Thread.Sleep(1000);
                elapsed++;
            }
        }

        private static string DescribeDevices(List<DeviceSnapshot> snapshots)
        {
            StringBuilder sb = new StringBuilder();
            foreach (DeviceSnapshot s in snapshots)
            {
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(s.Info.ShortName);
                sb.Append(' ');
                sb.Append(s.Battery.Percent < 0 ? "--" : s.Battery.Percent + "%");
                sb.Append(s.Battery.Online ? " 在线" : " 离线");
                if (s.Battery.Cached) sb.Append(" 缓存");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 诊断用：连续刷新几次并打印每次状态，用来确认“设备无应答后仍沿用上次读数”的保留窗口是否生效。
        /// </summary>
        private static void PrintWatch(int gapSeconds)
        {
            AppState state = new AppState(AppConfig.Load());
            DeviceService service = new DeviceService(state);
            int allowed = Math.Max(1, (state.SleepTimeoutMinutes * 60) / Math.Max(5, state.RefreshSeconds));
            Console.WriteLine("每隔 " + gapSeconds + " 秒刷新一次，共 8 次。休眠判定 = "
                + state.SleepTimeoutMinutes + " 分钟（约 " + allowed + " 个 " + state.RefreshSeconds
                + " 秒的刷新周期，可在设置里改；输出格式：电量/在线/缓存/详情）。");
            for (int cycle = 0; cycle < 8; cycle++)
            {
                service.Refresh();
                Console.WriteLine("--- 第 " + (cycle + 1) + " 次 " + DateTime.Now.ToString("HH:mm:ss") + " ---");
                List<DeviceSnapshot> snapshots = service.Snapshot();
                if (snapshots.Count == 0) Console.WriteLine("  未检测到设备");
                foreach (DeviceSnapshot s in snapshots)
                {
                    Console.WriteLine("  {0}  电量 {1}  在线 {2}  缓存 {3}  {4}",
                        s.Info.ShortName,
                        s.Battery.Percent < 0 ? "--" : s.Battery.Percent + "%",
                        s.Battery.Online, s.Battery.Cached, s.Battery.StatusText);
                    if (!string.IsNullOrEmpty(s.Battery.Detail)) Console.WriteLine("    详情: " + s.Battery.Detail);
                    if (!string.IsNullOrEmpty(s.Battery.Error)) Console.WriteLine("    错误: " + s.Battery.Error);
                }
                if (cycle < 7) Thread.Sleep(gapSeconds * 1000);
            }
        }

        private static int PrintProbe(string value)
        {
            string[] parts = value.Split(':');
            ushort vid, pid;
            if (parts.Length != 2 ||
                !ushort.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vid) ||
                !ushort.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out pid))
            {
                Console.WriteLine("参数格式错误，应为 --probe=1395:0089");
                return 1;
            }

            List<HidDeviceDescriptor> all = HidNative.Enumerate();
            int count = 0;
            foreach (HidDeviceDescriptor h in all)
            {
                if (h.VendorId != vid || h.ProductId != pid) continue;
                count++;
                Console.WriteLine("HID {0}  Usage {1:X4}/{2:X4}  IN {3} OUT {4} FEAT {5}  {6}",
                    h.VidPid, h.UsagePage, h.Usage, h.InputReportLength, h.OutputReportLength,
                    h.FeatureReportLength, h.ProductName);
                try
                {
                    using (HidSession s = new HidSession(h.Path,
                        Math.Max(1, h.InputReportLength), Math.Max(1, h.OutputReportLength)))
                    {
                        for (byte id = 0; id < 4 && h.FeatureReportLength > 0; id++)
                        {
                            byte[] feat = new byte[Math.Max(2, h.FeatureReportLength)];
                            feat[0] = id;
                            if (s.GetFeature(feat)) Console.WriteLine("  FEAT[{0}] {1}", id, Hex(feat));
                        }
                        for (int i = 0; i < 3; i++)
                        {
                            byte[] input = s.Read(200);
                            if (input != null) Console.WriteLine("  IN     {0}", Hex(input));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  打开失败: " + ex.Message);
                }
            }
            if (count == 0) Console.WriteLine("未找到 " + vid.ToString("X4") + ":" + pid.ToString("X4") + " 的 HID 接口");
            return 0;
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", " ");
        }

        private static int SetDpiFromArgs(string value)
        {
            string[] parts = value.Split(',');
            if (parts.Length < 3)
            {
                Console.WriteLine("参数格式错误，应为 --dpi=ID,X,Y[,STAGE]");
                return 1;
            }
            int x, y, stage = 1;
            if (!int.TryParse(parts[1], out x) || !int.TryParse(parts[2], out y))
            {
                Console.WriteLine("DPI 必须是整数");
                return 1;
            }
            if (parts.Length >= 4) int.TryParse(parts[3], out stage);
            if (stage < 1) stage = 1;

            DeviceService service = new DeviceService(new AppState(AppConfig.Load()));
            service.Refresh();
            DeviceSnapshot target = null;
            foreach (DeviceSnapshot s in service.Snapshot())
            {
                if (s.Info.Id.IndexOf(parts[0], StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.Info.Name.IndexOf(parts[0], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    target = s;
                    break;
                }
            }
            if (target == null)
            {
                Console.WriteLine("未找到设备: " + parts[0]);
                return 1;
            }
            bool ok = service.WriteDpi(target.Info, x, y, stage);
            Console.WriteLine(ok ? "写入成功: " + x + "×" + y + ", 档位 " + stage : "写入失败");
            return ok ? 0 : 2;
        }
    }
}
