using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace HyzMouseHub
{
    internal sealed class TrayApp : ApplicationContext
    {
        private const int SM_CXSMICON = 49;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        private const int QUNS_PRESENTATION_MODE = 4;

        // 每个勾选显示的设备对应一个托盘图标条目。
        private sealed class TrayEntry
        {
            public string DeviceId;
            public NotifyIcon Notify;
            public Icon CurrentIcon;
            public int LastLowPercent = -1;
            public bool MenuOpen;
            public bool Dragging;
            public int DragStartX;
            public int DragStartY;
            public int DragMoved;
        }

        private readonly Dictionary<string, TrayEntry> _trays = new Dictionary<string, TrayEntry>();
        private readonly Form _syncForm;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly DeviceService _service;
        private readonly AppState _state;
        private readonly Dictionary<string, Icon> _iconCache = new Dictionary<string, Icon>();
        private SettingsForm _settings;
        private bool _refreshing;
        private bool _orderDirty;
        private bool _fullscreenPaused;
        private bool _startupRefreshPending = true;
        private int _secondsSinceRefresh;

        public TrayApp(DeviceService service, AppState state, bool startMinimized)
        {
            _service = service;
            _state = state;
            _state.Changed += OnStateChanged;

            _syncForm = new Form();
            _syncForm.AutoScaleMode = AutoScaleMode.Dpi;
            _syncForm.WindowState = FormWindowState.Minimized;
            _syncForm.ShowInTaskbar = false;
            _syncForm.FormBorderStyle = FormBorderStyle.None;
            _syncForm.Opacity = 0;
            _syncForm.Load += delegate { _syncForm.Hide(); };
            // 提前创建句柄：启动刷新完成后能正确切回 UI 线程更新托盘图标。
            IntPtr syncHandle = _syncForm.Handle;

            _timer = new System.Windows.Forms.Timer();
            // 1 秒心跳用于全屏检测，真正读设备仍按配置的刷新间隔进行。
            _timer.Interval = 1000;
            _timer.Tick += OnTimerTick;
            _timer.Start();

            RefreshNow();
            UpdateUi();

            // 正常启动弹界面；只有开机自启/--minimized 才静默进托盘。
            if (!startMinimized) OpenSettings();
        }

        public DeviceService Service { get { return _service; } }
        public AppState State { get { return _state; } }

        /// <summary>
        /// 状态中间层广播：任一处界面改了设置，这里统一刷新托盘图标与菜单，
        /// 设置窗口自己订阅同一事件同步控件，双方不再各持一份副本。
        /// </summary>
        private void OnStateChanged(object sender, StateChangedEventArgs e)
        {
            UiInvoke(delegate
            {
                switch (e.Area)
                {
                    case StateArea.Appearance:
                        _secondsSinceRefresh = 0;
                        UpdateUi();
                        break;
                    case StateArea.TrayDevices:
                        UpdateUi();
                        break;
                    case StateArea.Profiles:
                        UpdateUi();
                        break;
                    default:
                        UpdateUi();
                        break;
                }
            });
        }

        /// <summary>设置窗口改动勾选后调用，立即重建托盘图标。</summary>
        public void RefreshTrayIcons()
        {
            UiInvoke(delegate { UpdateUi(); });
        }

        private void UiInvoke(Action action)
        {
            if (_syncForm.IsHandleCreated && _syncForm.InvokeRequired) _syncForm.BeginInvoke(action);
            else action();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (IsFullscreenActive())
            {
                // 全屏游戏/演示期间不轮询，避免占用厂商接口或弹出通知。
                _fullscreenPaused = true;
                return;
            }

            if (_fullscreenPaused)
            {
                _fullscreenPaused = false;
                _secondsSinceRefresh = 0;
                RefreshNow();
                return;
            }

            // 启动后再补一次，覆盖设备从休眠唤醒或首次枚举未完成的情况。
            if (_startupRefreshPending)
            {
                _startupRefreshPending = false;
                _secondsSinceRefresh = 0;
                RefreshNow();
                return;
            }

            _secondsSinceRefresh++;
            // 间隔统一向中间层取：开机自启后的快速扫描期它是 5 秒，
            // 读到活动设备或窗口到期后自动回到设置值，这里无需分支。
            int interval = Math.Max(StartupBurst.IntervalSeconds, _state.EffectiveRefreshSeconds);
            if (_secondsSinceRefresh >= interval)
            {
                _secondsSinceRefresh = 0;
                RefreshNow();
            }
        }

        private static bool IsFullscreenActive()
        {
            try
            {
                int state;
                if (SHQueryUserNotificationState(out state) == 0 &&
                    (state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE))
                    return true;

                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return false;

                // 带标题栏的窗口（含普通最大化）不算全屏，任务栏自动隐藏时避免误判。
                int style = GetWindowLong(hwnd, GWL_STYLE);
                if ((style & WS_CAPTION) != 0) return false;

                Rect r;
                if (!GetWindowRect(hwnd, out r)) return false;
                Rectangle bounds = Screen.FromHandle(hwnd).Bounds;
                const int margin = 8;
                return r.Left <= bounds.Left + margin && r.Top <= bounds.Top + margin &&
                       r.Right >= bounds.Right - margin && r.Bottom >= bounds.Bottom - margin;
            }
            catch { return false; }
        }

        public void RefreshNow()
        {
            if (_refreshing) return;
            _refreshing = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { _service.Refresh(); }
                catch { }
                UiInvoke(delegate
                {
                    _refreshing = false;
                    // 结果先回传中间层：快速扫描期内读到活动设备会就此收尾。
                    _state.ReportScan(_service.Snapshot());
                    UpdateUi();
                    if (_settings != null) _settings.RefreshDevices();
                });
            });
        }

        private void TrayMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            NotifyIcon notify = sender as NotifyIcon;
            TrayEntry entry = FindEntry(notify);
            if (entry == null) return;
            entry.Dragging = true;
            entry.DragStartX = Cursor.Position.X;
            entry.DragStartY = Cursor.Position.Y;
            entry.DragMoved = 0;
        }

        private void TrayMouseMove(object sender, MouseEventArgs e)
        {
            NotifyIcon notify = sender as NotifyIcon;
            TrayEntry entry = FindEntry(notify);
            if (entry == null || !entry.Dragging) return;
            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                entry.Dragging = false;
                return;
            }
            int dx = Cursor.Position.X - entry.DragStartX;
            int dy = Cursor.Position.Y - entry.DragStartY;
            if (Math.Abs(dy) > 12 || Math.Abs(dx) < 16) return;
            int step = dx > 0 ? 1 : -1;
            if (MoveTrayDevice(entry.DeviceId, step))
            {
                entry.DragStartX = Cursor.Position.X;
                entry.DragMoved += Math.Abs(dx);
                UpdateUi();
            }
        }

        private void TrayMouseUp(object sender, MouseEventArgs e)
        {
            NotifyIcon notify = sender as NotifyIcon;
            TrayEntry entry = FindEntry(notify);
            if (entry == null) return;
            bool moved = entry.DragMoved > 0;
            entry.Dragging = false;
            if (!moved && e.Button == MouseButtons.Left) OpenSettings();
        }

        private TrayEntry FindEntry(NotifyIcon notify)
        {
            if (notify == null) return null;
            foreach (KeyValuePair<string, TrayEntry> kv in _trays)
                if (kv.Value.Notify == notify) return kv.Value;
            return null;
        }

        /// <summary>按当前可见顺序把设备向左右移动一位，返回是否发生变化。</summary>
        private bool MoveTrayDevice(string deviceId, int step)
        {
            List<string> order = OrderedVisibleIds();
            int index = order.IndexOf(deviceId);
            if (index < 0) return false;
            int target = index + step;
            if (target < 0 || target >= order.Count) return false;
            string other = order[target];
            order[index] = other;
            order[target] = deviceId;
            _orderDirty = true;
            _state.SetTrayOrder(order);
            return true;
        }

        private List<string> OrderedVisibleIds()
        {
            List<DeviceSnapshot> snapshots = _service.Snapshot();
            List<DeviceSnapshot> visible = new List<DeviceSnapshot>();
            foreach (DeviceSnapshot s in snapshots)
                if (_state.IsTrayVisible(s.Info.Id)) visible.Add(s);
            visible.Sort(delegate(DeviceSnapshot a, DeviceSnapshot b)
            {
                int ia = _state.TrayOrderIndex(a.Info.Id);
                int ib = _state.TrayOrderIndex(b.Info.Id);
                return ia.CompareTo(ib);
            });
            List<string> ids = new List<string>();
            foreach (DeviceSnapshot s in visible) ids.Add(s.Info.Id);
            return ids;
        }

        public void OpenSettings()
        {
            UiInvoke(delegate
            {
                if (_settings == null || _settings.IsDisposed)
                {
                    _settings = new SettingsForm(this);
                }
                _settings.RefreshDevices();
                _settings.Show();
                _settings.BringToFront();
            });
        }

        /// <summary>外观设置变更后重置刷新计时，让新间隔立即生效。</summary>
        public void ResetRefreshClock()
        {
            _secondsSinceRefresh = 0;
        }

        /// <summary>诊断用：把设置窗口渲染成 PNG，便于不依赖外部截图工具检查布局。</summary>
        internal void CaptureSettingsSnapshot(string path)
        {
            SettingsForm f = new SettingsForm(this);
            f.RefreshDevices();
            f.Show();
            Application.DoEvents();
            using (Bitmap bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            f.Close();
            f.Dispose();
            ExitThread();
        }

        /// <summary>诊断用：把托盘图标按 8 倍最近邻放大拼成一张图，检查 16px 下的实际清晰度。</summary>
        internal void CaptureIconPreview(string path)
        {
            int zoom = 8, cell = 16, pad = 6;
            DeviceKind[] kinds = { DeviceKind.Mouse, DeviceKind.Keyboard, DeviceKind.Headset };
            int[] levels = { 8, 29, 47, 100, -1 };
            int cols = levels.Length;
            int rows = 1 + kinds.Length;
            int stride = cell * zoom + pad;
            try
            {
                using (Bitmap sheet = new Bitmap(pad + cols * stride, pad + rows * stride))
                using (Graphics g = Graphics.FromImage(sheet))
                {
                    g.Clear(Color.FromArgb(0xEF, 0xEF, 0xEF));
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    for (int r = 0; r < rows; r++)
                    {
                        for (int c = 0; c < cols; c++)
                        {
                            BatteryReading sample = new BatteryReading();
                            sample.Percent = levels[c];
                            sample.Online = levels[c] >= 0;
                            sample.Charging = levels[c] == 47;
                            int mode = r == 0 ? 0 : 1;
                            DeviceKind kind = r == 0 ? DeviceKind.Mouse : kinds[r - 1];
                            using (Icon icon = GetBatteryIcon(sample, kind, mode))
                            using (Bitmap small = icon.ToBitmap())
                            {
                                g.DrawImage(small, new Rectangle(pad + c * stride, pad + r * stride, cell * zoom, cell * zoom));
                            }
                        }
                    }
                    sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            finally
            {
                ExitThread();
            }
        }

        /// <summary>
        /// 根据勾选配置，为每个可见设备维护一个托盘图标。
        /// 一个都没勾选时回退到单个“未选择设备”图标，保证程序始终可达。
        /// </summary>
        private void UpdateUi()
        {
            List<DeviceSnapshot> snapshots = _service.Snapshot();
            List<DeviceSnapshot> visible = new List<DeviceSnapshot>();
            foreach (DeviceSnapshot s in snapshots)
                if (_state.IsTrayVisible(s.Info.Id)) visible.Add(s);
            visible.Sort(delegate(DeviceSnapshot a, DeviceSnapshot b)
            {
                return _state.TrayOrderIndex(a.Info.Id).CompareTo(_state.TrayOrderIndex(b.Info.Id));
            });

            // 顺序变化后 Windows 托盘按 NotifyIcon 创建顺序排列，必须整体重建才能生效。
            if (_orderDirty)
            {
                _orderDirty = false;
                foreach (string id in new List<string>(_trays.Keys)) RemoveTray(id);
            }

            HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DeviceSnapshot s in visible) wanted.Add(s.Info.Id);

            // 没有任何可见设备时，保留一个默认图标以便打开设置。
            bool fallback = visible.Count == 0;
            if (fallback) wanted.Add("__none__");

            // 移除不再需要的托盘图标。
            List<string> stale = new List<string>();
            foreach (KeyValuePair<string, TrayEntry> kv in _trays)
                if (!wanted.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (string id in stale) RemoveTray(id);

            if (fallback)
            {
                TrayEntry e = EnsureTray("__none__");
                e.Notify.Icon = AppIcon.Small;
                e.Notify.Text = "HYZ Mouse Hub · 未勾选显示设备";
                ContextMenuStrip old = e.Notify.ContextMenuStrip;
                e.Notify.ContextMenuStrip = BuildMenu("__none__", snapshots);
                if (old != null) old.Dispose();
                return;
            }

            foreach (DeviceSnapshot s in visible)
            {
                TrayEntry e = EnsureTray(s.Info.Id);
                Icon icon = GetBatteryIcon(s.Battery, s.Info.Kind, _state.DisplayMode);
                if (e.CurrentIcon != icon)
                {
                    e.CurrentIcon = icon;
                    e.Notify.Icon = icon;
                }

                string title = s.Info.ShortName + "  " + s.Battery.StatusText;
                if (!string.IsNullOrEmpty(s.Battery.Detail)) title += " · " + s.Battery.Detail;
                if (title.Length > 63) title = title.Substring(0, 63);
                e.Notify.Text = title;

                if (!e.MenuOpen)
                {
                    ContextMenuStrip old = e.Notify.ContextMenuStrip;
                    e.Notify.ContextMenuStrip = BuildMenu(s.Info.Id, snapshots);
                    if (old != null) old.Dispose();
                }

                CheckLowBattery(e, s);
            }
        }

        private void CheckLowBattery(TrayEntry entry, DeviceSnapshot s)
        {
            BatteryReading b = s.Battery;
            if (!b.Cached && b.Percent >= 0 && b.Percent <= _state.LowThreshold && !b.Charging)
            {
                if (entry.LastLowPercent != b.Percent)
                {
                    entry.LastLowPercent = b.Percent;
                    try
                    {
                        entry.Notify.ShowBalloonTip(4000, "电量偏低",
                            s.Info.ShortName + " 剩余 " + b.Percent + "%", ToolTipIcon.Warning);
                    }
                    catch { }
                }
            }
            else if (b.Percent > _state.LowThreshold + 5)
            {
                entry.LastLowPercent = -1;
            }
        }

        private TrayEntry EnsureTray(string deviceId)
        {
            TrayEntry entry;
            if (_trays.TryGetValue(deviceId, out entry)) return entry;

            entry = new TrayEntry();
            entry.DeviceId = deviceId;
            NotifyIcon notify = new NotifyIcon();
            notify.Icon = AppIcon.Small;
            notify.Visible = true;
            notify.Text = "HYZ Mouse Hub";
            notify.MouseDown += TrayMouseDown;
            notify.MouseMove += TrayMouseMove;
            notify.MouseUp += TrayMouseUp;
            notify.DoubleClick += delegate { OpenSettings(); };
            entry.Notify = notify;
            _trays.Add(deviceId, entry);
            return entry;
        }

        private void RemoveTray(string deviceId)
        {
            TrayEntry entry;
            if (!_trays.TryGetValue(deviceId, out entry)) return;
            try
            {
                entry.Notify.Visible = false;
                if (entry.Notify.ContextMenuStrip != null) entry.Notify.ContextMenuStrip.Dispose();
                entry.Notify.Dispose();
            }
            catch { }
            _trays.Remove(deviceId);
        }

        private DeviceSnapshot FindSnapshot(List<DeviceSnapshot> snapshots, string deviceId)
        {
            foreach (DeviceSnapshot s in snapshots)
                if (string.Equals(s.Info.Id, deviceId, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        private ContextMenuStrip BuildMenu(string deviceId, List<DeviceSnapshot> snapshots)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new PlainColors());
            TrayEntry entry;
            if (_trays.TryGetValue(deviceId, out entry))
            {
                menu.Opening += delegate { entry.MenuOpen = true; };
                menu.Closed += delegate { entry.MenuOpen = false; };
            }

            // 右键第一项直达主界面。
            ToolStripMenuItem open = new ToolStripMenuItem("打开主界面 / 设置", null, delegate { OpenSettings(); });
            open.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            menu.Items.Add(open);

            DeviceSnapshot primary = FindSnapshot(snapshots, deviceId);
            if (primary != null)
            {
                ToolStripItem level = menu.Items.Add("  当前电量  " + primary.Info.ShortName + "  " + primary.Battery.StatusText);
                level.Enabled = false;
                if (!string.IsNullOrEmpty(primary.Battery.HealthText))
                {
                    ToolStripItem health = menu.Items.Add("  " + primary.Battery.HealthText);
                    health.Enabled = false;
                    health.Font = new Font("Segoe UI", 7.5F);
                }
            }
            menu.Items.Add(new ToolStripSeparator());

            ToolStripItem header = menu.Items.Add("设备 (" + snapshots.Count + ")  ·  勾选显示可在设置中调整");
            header.Enabled = false;
            if (snapshots.Count == 0)
            {
                ToolStripItem none = menu.Items.Add("  未检测到支持设备");
                none.Enabled = false;
            }
            foreach (DeviceSnapshot s in snapshots)
            {
                string shown = _state.IsTrayVisible(s.Info.Id) ? "" : "  [隐藏]";
                string mark = s.Battery.Online ? "" : (s.Battery.Cached ? "  [休眠 · 缓存]" : "  [休眠]");
                ToolStripItem item = menu.Items.Add("  " + TypeMark(s.Info.Kind) + " " + s.Info.ShortName + "  " + s.Battery.StatusText + mark + shown);
                item.Enabled = false;
            }
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem display = new ToolStripMenuItem("托盘显示");
            ToolStripMenuItem number = new ToolStripMenuItem("百分比数字", null, delegate { SetDisplayMode(0); });
            number.Checked = _state.DisplayMode == 0;
            ToolStripMenuItem mouse = new ToolStripMenuItem("设备图标进度", null, delegate { SetDisplayMode(1); });
            mouse.Checked = _state.DisplayMode == 1;
            display.DropDownItems.Add(number);
            display.DropDownItems.Add(mouse);
            menu.Items.Add(display);

            ToolStripMenuItem interval = new ToolStripMenuItem("刷新间隔");
            foreach (int seconds in new int[] { 15, 30, 90 })
            {
                int captured = seconds;
                ToolStripMenuItem choice = new ToolStripMenuItem(seconds + " 秒", null, delegate
                {
                    _state.SetRefreshSeconds(captured);
                });
                choice.Checked = _state.RefreshSeconds == seconds;
                interval.DropDownItems.Add(choice);
            }
            menu.Items.Add(interval);

            ToolStripMenuItem profiles = new ToolStripMenuItem("板载配置");
            foreach (MouseProfile p in _state.Profiles)
            {
                MouseProfile captured = p;
                ToolStripMenuItem pi = new ToolStripMenuItem(p.Name + "  " + p.DpiX + "×" + p.DpiY);
                pi.Click += delegate { ApplyProfile(captured, deviceId); };
                profiles.DropDownItems.Add(pi);
            }
            if (_state.Profiles.Count > 0) profiles.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem manage = new ToolStripMenuItem("管理 / 新建配置...");
            manage.Click += delegate { OpenSettings(); };
            profiles.DropDownItems.Add(manage);
            menu.Items.Add(profiles);

            ToolStripMenuItem auto = new ToolStripMenuItem("开机自启动");
            auto.Checked = AppConfig.IsAutoStartEnabled();
            auto.Click += delegate
            {
                bool enable = !auto.Checked;
                AppConfig.SetAutoStart(enable);
                auto.Checked = enable;
            };
            menu.Items.Add(auto);

            ToolStripMenuItem rescan = new ToolStripMenuItem("立即重新扫描");
            rescan.Click += delegate { RefreshNow(); };
            menu.Items.Add(rescan);

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem exit = new ToolStripMenuItem("退出 HYZ Mouse Hub");
            exit.Click += delegate { ExitApplication(); };
            menu.Items.Add(exit);

            return menu;
        }

        private static string TypeMark(DeviceKind kind)
        {
            if (kind == DeviceKind.Keyboard) return "[键盘]";
            if (kind == DeviceKind.Headset) return "[耳机]";
            return "[鼠标]";
        }

        /// <summary>显示方式只写状态中间层，托盘与设置窗口都从那里同步。</summary>
        private void SetDisplayMode(int mode)
        {
            _state.SetDisplayMode(mode);
        }

        public void ApplyProfile(MouseProfile profile)
        {
            ApplyProfile(profile, _state.SelectedDeviceId);
        }

        private void ApplyProfile(MouseProfile profile, string deviceId)
        {
            DeviceSnapshot device = null;
            foreach (DeviceSnapshot s in _service.Snapshot())
            {
                if (string.Equals(s.Info.Id, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    device = s;
                    break;
                }
            }
            if (device == null)
            {
                Balloon(deviceId, "HYZ Mouse Hub", "请先在设置里选择设备", ToolTipIcon.Info);
                return;
            }

            _state.SetSelectedDevice(deviceId);
            _state.SetActiveProfile(profile.Name);
            _secondsSinceRefresh = 0;

            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = _service.WriteDpi(device.Info, profile.DpiX, profile.DpiY, profile.Stage);
                UiInvoke(delegate
                {
                    Balloon(deviceId, "HYZ Mouse Hub",
                        ok ? "已应用 " + profile.Name + " · " + profile.DpiX + " DPI" : "写入失败：设备休眠、型号未识别或被占用",
                        ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
                    UpdateUi();
                });
            });
        }

        private void Balloon(string deviceId, string title, string text, ToolTipIcon icon)
        {
            TrayEntry entry;
            if (!_trays.TryGetValue(deviceId, out entry))
                foreach (KeyValuePair<string, TrayEntry> kv in _trays) { entry = kv.Value; break; }
            if (entry == null) return;
            try { entry.Notify.ShowBalloonTip(3000, title, text, icon); }
            catch { }
        }

        public void ExitApplication()
        {
            _timer.Stop();
            foreach (TrayEntry e in _trays.Values)
            {
                try
                {
                    e.Notify.Visible = false;
                    if (e.Notify.ContextMenuStrip != null) e.Notify.ContextMenuStrip.Dispose();
                    e.Notify.Dispose();
                }
                catch { }
            }
            _trays.Clear();
            foreach (Icon icon in _iconCache.Values) icon.Dispose();
            _iconCache.Clear();
            _syncForm.Dispose();
            ExitThread();
        }

        private Icon GetBatteryIcon(BatteryReading battery, DeviceKind kind, int displayMode)
        {
            int level = battery.Percent;
            bool charging = battery.Charging;
            // 缓存读数（设备未应答但在保留窗口内）仍然画出数值，只是整体压淡，
            // 这样既能继续看到电量，又和实时读数一眼可分。
            bool offline = level < 0;
            bool stale = battery.Cached;

            // Tray icons are 16px at 100% (20/24/32px at higher scaling). Drawing at
            // the native small-icon size keeps the digits sharp; downscaling one
            // 32px bitmap is what made the old icon unreadable.
            int size = 16;
            try
            {
                int native = GetSystemMetrics(SM_CXSMICON);
                if (native >= 16 && native <= 64) size = native;
            }
            catch { }

            string key = size + "|" + (offline ? "off" : level.ToString()) + "|" +
                         (charging ? "c" : "") + (stale ? "s" : "") + "|" + displayMode + "|" + (int)kind;
            Icon cached;
            if (_iconCache.TryGetValue(key, out cached)) return cached;

            using (Bitmap bmp = new Bitmap(size, size))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                float scale = size / 16f;
                g.ScaleTransform(scale, scale);
                if (displayMode == 1)
                    DrawDeviceGlyph(g, offline ? -1 : level, charging, offline, kind);
                else
                    DrawNumberGlyph(g, offline ? -1 : level, charging, scale);

                if (stale)
                {
                    g.ResetTransform();
                    using (SolidBrush fade = new SolidBrush(Color.FromArgb(112, 255, 255, 255)))
                        g.FillRectangle(fade, 0, 0, size, size);
                }

                IntPtr h = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                _iconCache[key] = icon;
                return icon;
            }
        }

        private static void DrawNumberGlyph(Graphics g, int level, bool charging, float scale)
        {
            // 纯数字：透明背景，直接绘制黑色粗体百分比，不画底色块。
            string text = level < 0 ? "--" : (level > 99 ? "100" : level.ToString());
            // 16px 下用像素对齐的粗体数字，字号取整避免亚像素模糊。
            float fontPx = (text.Length >= 3 ? 9.5f : 11.6f) * scale;
            using (Font font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat sf = new StringFormat())
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(0x0C, 0x0C, 0x0C)))
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                sf.Trimming = StringTrimming.None;
                g.ResetTransform();
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                // 整个 16px 宽度都给数字，抬高 0.6px 给底部类型色条让位。
                g.DrawString(text, font, brush, new RectangleF(0.2f * scale, 0.4f * scale, 15.6f * scale, 13.4f * scale), sf);
            }

            // 底部色条用醒目字体提示电量档位（红 ≤15% / 黄 ≤40% / 绿 >40%），
            // 1px 的类型色在 16px 下几乎看不见，改为 2px 高、更宽的档位色条。
            g.ResetTransform();
            g.ScaleTransform(scale, scale);
            using (SolidBrush bar = new SolidBrush(StatusColor(level, level < 0)))
                g.FillRectangle(bar, 1.8f, 13.7f, 12.4f, 2.1f);

            if (charging)
            {
                g.ResetTransform();
                g.ScaleTransform(scale, scale);
                DrawBolt(g, new PointF(0.2f, 0.2f), Color.FromArgb(255, 190, 0), 0.42f);
            }
        }

        private static void DrawDeviceGlyph(Graphics g, int level, bool charging, bool offline, DeviceKind kind)
        {
            if (kind == DeviceKind.Keyboard) DrawKeyboardGlyph(g, level, charging, offline);
            else if (kind == DeviceKind.Headset) DrawHeadsetGlyph(g, level, charging, offline);
            else DrawMouseGlyph(g, level, charging, offline);
        }

        /// <summary>电量档位色：红 ≤15%、黄 ≤40%、绿 >40%；无读数用中性灰。</summary>
        private static Color StatusColor(int level, bool offline)
        {
            if (offline || level < 0) return Color.FromArgb(0x8A, 0x90, 0x96);
            if (level <= 15) return Color.FromArgb(0xE2, 0x3A, 0x3A);
            if (level <= 40) return Color.FromArgb(0xF2, 0xB7, 0x0C);
            return Color.FromArgb(0x2E, 0xA5, 0x5F);
        }

        private static void DrawMouseGlyph(Graphics g, int level, bool charging, bool offline)
        {
            Color baseColor = offline ? Color.FromArgb(0xB8, 0xBD, 0xC4) : Color.FromArgb(0xF2, 0xF4, 0xF6);
            Color status = StatusColor(level, offline);

            using (GraphicsPath body = RoundRect(2.4f, 1.2f, 11.2f, 13.6f, 3.4f))
            using (GraphicsPath wheel = RoundRect(6.8f, 2.6f, 2.4f, 3f, 1.2f))
            {
                using (SolidBrush b = new SolidBrush(baseColor)) g.FillPath(b, body);
                if (!offline && level >= 0)
                {
                    float h = 13.6f * Math.Min(100, level) / 100f;
                    using (Region clip = new Region(body))
                    {
                        g.SetClip(clip, CombineMode.Replace);
                        using (SolidBrush b = new SolidBrush(status))
                            g.FillRectangle(b, 2f, 14.8f - h, 12f, h + 0.5f);
                        g.ResetClip();
                    }
                }
                using (Pen line = new Pen(Color.FromArgb(0x24, 0x24, 0x2A), 0.85f))
                {
                    g.DrawPath(line, body);
                    g.DrawPath(line, wheel);
                    g.DrawLine(line, 8f, 7f, 8f, 13.6f);
                }
            }
            if (charging) DrawBolt(g, new PointF(11f, 0.1f), Color.FromArgb(255, 214, 10), 0.44f);
        }

        private static void DrawKeyboardGlyph(Graphics g, int level, bool charging, bool offline)
        {
            Color baseColor = offline ? Color.FromArgb(0xB8, 0xBD, 0xC4) : Color.FromArgb(0xF2, 0xF4, 0xF6);
            Color status = StatusColor(level, offline);
            using (GraphicsPath body = RoundRect(0.8f, 3.2f, 14.4f, 9.6f, 1.6f))
            {
                using (SolidBrush b = new SolidBrush(baseColor)) g.FillPath(b, body);
                if (!offline && level >= 0)
                {
                    float h = 9.6f * Math.Min(100, level) / 100f;
                    using (Region clip = new Region(body))
                    {
                        g.SetClip(clip, CombineMode.Replace);
                        using (SolidBrush b = new SolidBrush(status))
                            g.FillRectangle(b, 0f, 12.8f - h, 16f, h + 0.5f);
                        g.ResetClip();
                    }
                }
                using (Pen line = new Pen(Color.FromArgb(0x24, 0x24, 0x2A), 0.8f))
                {
                    g.DrawPath(line, body);
                    // 三排键位短线，保持键盘可辨识。
                    for (int row = 0; row < 2; row++)
                    {
                        float y = 5.6f + row * 3.2f;
                        for (int col = 0; col < 4; col++)
                        {
                            float x = 2.4f + col * 3f;
                            g.DrawLine(line, x, y, x + 1.8f, y);
                        }
                    }
                }
            }
            if (charging) DrawBolt(g, new PointF(11.4f, 0.2f), Color.FromArgb(255, 214, 10), 0.42f);
        }

        private static void DrawHeadsetGlyph(Graphics g, int level, bool charging, bool offline)
        {
            Color baseColor = offline ? Color.FromArgb(0xB8, 0xBD, 0xC4) : Color.FromArgb(0xF2, 0xF4, 0xF6);
            Color status = StatusColor(level, offline);
            using (Pen band = new Pen(Color.FromArgb(0x24, 0x24, 0x2A), 1.5f))
                g.DrawArc(band, 2.2f, 1f, 11.6f, 10f, 180f, 180f);

            using (GraphicsPath left = RoundRect(1.2f, 6.4f, 3.6f, 7f, 1.5f))
            using (GraphicsPath right = RoundRect(11.2f, 6.4f, 3.6f, 7f, 1.5f))
            {
                using (SolidBrush b = new SolidBrush(baseColor)) { g.FillPath(b, left); g.FillPath(b, right); }
                if (!offline && level >= 0)
                {
                    float h = 7f * Math.Min(100, level) / 100f;
                    using (Region clipL = new Region(left))
                    using (Region clipR = new Region(right))
                    using (SolidBrush b = new SolidBrush(status))
                    {
                        g.SetClip(clipL, CombineMode.Replace);
                        g.FillRectangle(b, 0f, 13.4f - h, 16f, h + 0.5f);
                        g.ResetClip();
                        g.SetClip(clipR, CombineMode.Replace);
                        g.FillRectangle(b, 0f, 13.4f - h, 16f, h + 0.5f);
                        g.ResetClip();
                    }
                }
                using (Pen line = new Pen(Color.FromArgb(0x24, 0x24, 0x2A), 0.8f))
                {
                    g.DrawPath(line, left);
                    g.DrawPath(line, right);
                }
            }
            if (charging) DrawBolt(g, new PointF(11.4f, 0.2f), Color.FromArgb(255, 214, 10), 0.42f);
        }

        private static GraphicsPath RoundRect(float x, float y, float w, float h, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2f;
            p.AddArc(x, y, d, d, 180f, 90f);
            p.AddArc(x + w - d, y, d, d, 270f, 90f);
            p.AddArc(x + w - d, y + h - d, d, d, 0f, 90f);
            p.AddArc(x, y + h - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        private static void DrawBolt(Graphics g, PointF origin, Color color, float s)
        {
            PointF[] points = new PointF[]
            {
                new PointF(origin.X + 3f * s, origin.Y),
                new PointF(origin.X, origin.Y + 5f * s),
                new PointF(origin.X + 2.2f * s, origin.Y + 5f * s),
                new PointF(origin.X + 1.2f * s, origin.Y + 9f * s),
                new PointF(origin.X + 5f * s, origin.Y + 3.6f * s),
                new PointF(origin.X + 2.8f * s, origin.Y + 3.6f * s),
                new PointF(origin.X + 4f * s, origin.Y)
            };
            using (SolidBrush b = new SolidBrush(color)) g.FillPolygon(b, points);
            using (Pen p = new Pen(Color.FromArgb(140, 90, 50, 0), s)) g.DrawPolygon(p, points);
        }

        private sealed class PlainColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(250, 250, 250); } }
            public override Color MenuItemSelected { get { return Color.FromArgb(0, 120, 212); } }
            public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(0, 120, 212); } }
            public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(0, 120, 212); } }
            public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(235, 235, 235); } }
            public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(235, 235, 235); } }
            public override Color SeparatorLight { get { return Color.FromArgb(225, 225, 225); } }
            public override Color SeparatorDark { get { return Color.FromArgb(225, 225, 225); } }
        }
    }
}
