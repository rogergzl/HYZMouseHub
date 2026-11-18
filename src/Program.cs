using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace HyzMouseHub
{
    internal static class Program
    {
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
            DeviceService service = new DeviceService();
            AppConfig config = AppConfig.Load();

            bool autostart = false;
            string snapshotPath = null;
            string iconPreviewPath = null;
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--autostart", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase))
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

            if (!string.IsNullOrEmpty(iconPreviewPath))
            {
                TrayApp preview = new TrayApp(service, config, true);
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
                TrayApp snap = new TrayApp(service, config, true);
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

            Application.Run(new TrayApp(service, config, autostart));
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
            Console.WriteLine("HYZ Mouse Hub 0.3.3");
            Console.WriteLine();
            Console.WriteLine("无参数          启动托盘并弹出设置窗口");
            Console.WriteLine("--autostart     开机自启：静默最小化到托盘");
            Console.WriteLine("--minimized     同上，手动静默启动");
            Console.WriteLine("--list          列出全部 HID 接口");
            Console.WriteLine("--test          扫描 ATK / 雷蛇设备并读取电量");
            Console.WriteLine("--test-all      同时读取 DPI（会唤醒设备）");
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
            DeviceService service = new DeviceService();
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

            DeviceService service = new DeviceService();
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
