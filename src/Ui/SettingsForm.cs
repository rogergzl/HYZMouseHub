using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HyzMouseHub
{
    /// <summary>
    /// 设置窗口。只做三件事：摆放卡片、把控件事件转成中间层调用、把中间层状态刷回控件。
    /// 界面不持有任何业务状态，显示与保存都以 <see cref="AppState"/> 为准。
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly TrayApp _app;
        private readonly DeviceService _service;
        private readonly AppState _state;

        private readonly DeviceCard _device = new DeviceCard();
        private readonly TrayDevicesCard _trayDevices = new TrayDevicesCard();
        private readonly DpiCard _dpi = new DpiCard();
        private readonly ProfileCard _profiles = new ProfileCard();
        private readonly AppearanceCard _appearance = new AppearanceCard();
        private readonly VolumeCard _volume = new VolumeCard();
        private readonly DiagnosticsCard _diagnostics = new DiagnosticsCard();
        private readonly Button _about = new Button();
        private readonly Button _diagnose = new Button();
        private readonly Label _status = new Label();
        private readonly Label _version = new Label();
        private Panel _body;
        private TableLayoutPanel _grid;
        private Card[][] _rows;

        private bool _loading;
        private string _thresholdDeviceIds = "";
        private bool _aligning;

        // 板载 DPI 的显示一律以中间层缓存为准：这里没有「休眠」这种中间态，
        // 只区分「读到过」和「还没读到过」。自动补读由托盘侧按启动规则驱动。
        private bool _dpiReading;
        private string _dpiDeviceId = "";
        // 用户手改过 X / Y / 档位、还没写入：这期间后台自动重读只更新「当前板载 DPI」那行文字，
        // 不把输入框里的数字抢回去，否则每到一个刷新间隔就可能刷掉正在改的值。
        private bool _dpiEdited;
        private bool _dpiSyncing;

        public SettingsForm(TrayApp app)
        {
            _app = app;
            _service = app.Service;
            _state = app.State;

            // 左列放“这台设备”，右列放“怎么显示”，同一行的两张卡配成一对。
            _rows = new Card[][]
            {
                new Card[] { _device, _trayDevices },
                new Card[] { _profiles, _appearance },
                new Card[] { _dpi, _volume }
            };

            Text = "HYZ Mouse Hub";
            Icon = AppIcon.Shared;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(1020, 840);
            MinimumSize = new Size(900, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Back;
            Font = Theme.Body;
            DoubleBuffered = true;
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    // 关窗即放弃没写入的编辑，下次打开重新跟随硬件值。
                    _dpiEdited = false;
                    e.Cancel = true;
                    Hide();
                }
            };

            BuildUi();
            WireEvents();
            _state.Changed += OnStateChanged;
            LoadSettingsUi();
            RefreshDevices();
        }

        // ── 布局 ───────────────────────────────────────────────────────────

        private void BuildUi()
        {
            Control header = BuildHeader();
            Controls.Add(header);
            Controls.Add(_body = BuildBody());
            Controls.Add(BuildStatusBar());

            // WinForms 从 Controls 末尾开始停靠：Fill 必须第一个，Header 次之，状态栏最后。
            Controls.SetChildIndex(_body, 0);
            Controls.SetChildIndex(header, 1);
            Controls.SetChildIndex(Controls[2], 2);
        }

        private Control BuildHeader()
        {
            Panel header = new Panel { Dock = DockStyle.Top, Height = Theme.HeaderHeight, BackColor = Theme.Header };

            Label title = new Label
            {
                Text = "HYZ Mouse Hub",
                Font = Theme.Title,
                ForeColor = Theme.TextPrimary,
                Location = new Point(Theme.Pad + 2, 0),
                Size = new Size(320, Theme.HeaderHeight),
                TextAlign = ContentAlignment.MiddleLeft
            };
            header.Controls.Add(title);

            _about.Text = "关于";
            _diagnose.Text = "读取诊断";
            Theme.StyleHeaderButton(_about);
            Theme.StyleHeaderButton(_diagnose);
            _about.Size = new Size(72, Theme.FieldHeight);
            _diagnose.Size = new Size(88, Theme.FieldHeight);
            _about.Anchor = _diagnose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.Add(_about);
            header.Controls.Add(_diagnose);

            // 宽度要等布局出来才知道，按右边缘算位置，窗口缩放时跟着走。
            EventHandler place = delegate
            {
                _about.Location = new Point(header.ClientSize.Width - Theme.Pad - _about.Width,
                    (Theme.HeaderHeight - _about.Height) / 2);
                _diagnose.Location = new Point(_about.Left - 8 - _diagnose.Width, _about.Top);
            };
            header.Resize += place;
            place(null, EventArgs.Empty);

            header.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Theme.Hairline
            });
            return header;
        }

        private Panel BuildBody()
        {
            Panel body = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(Theme.Pad, Theme.Pad, Theme.Pad, Theme.Pad),
                BackColor = Theme.Back,
                AutoScroll = true
            };

            _grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = _rows.Length,
                BackColor = Theme.Back,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            int half = Theme.Gutter / 2;
            for (int row = 0; row < _rows.Length; row++)
            {
                _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
                for (int col = 0; col < 2; col++)
                {
                    Card card = _rows[row][col];
                    card.Dock = DockStyle.Fill;
                    // 中间沟槽左右各半；行间距只留给非最后一行，最后一张卡贴着底。
                    card.Margin = new Padding(col == 0 ? 0 : half, 0,
                        col == 0 ? half : 0, row == _rows.Length - 1 ? 0 : Theme.Gap);
                    _grid.Controls.Add(card, col, row);
                }
            }

            body.Controls.Add(_grid);
            return body;
        }

        private Control BuildStatusBar()
        {
            _status.Dock = DockStyle.Fill;
            _status.BackColor = Theme.Header;
            _status.ForeColor = Theme.TextMuted;
            _status.Font = Theme.Small;
            _status.Padding = new Padding(Theme.Pad, 0, 0, 0);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Text = "就绪";

            _version.Dock = DockStyle.Right;
            _version.Width = 110;
            _version.BackColor = Theme.Header;
            _version.ForeColor = Theme.TextMuted;
            _version.Font = Theme.Small;
            _version.TextAlign = ContentAlignment.MiddleRight;
            _version.Padding = new Padding(0, 0, Theme.Pad, 0);
            _version.Text = "v" + typeof(Program).Assembly.GetName().Version.ToString(3);

            Panel bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.StatusHeight, BackColor = Theme.Header };
            bar.Controls.Add(_status);
            bar.Controls.Add(_version);
            bar.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Hairline });
            return bar;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_aligning || _grid == null) return;
            _aligning = true;
            try { LayoutRows(); }
            finally { _aligning = false; }
        }

        /// <summary>
        /// 每一行的高度取两张卡里较高的那张，两列因此顶部对齐、底部也齐平；
        /// 内容决定的高度不够填满窗口时，多出来的空间平均分给三行。
        /// </summary>
        private void LayoutRows()
        {
            if (_grid == null || _rows == null || _grid.RowStyles.Count < _rows.Length) return;

            int[] natural = new int[_rows.Length];
            int total = 0;
            for (int i = 0; i < _rows.Length; i++)
            {
                natural[i] = Math.Max(_rows[i][0].NaturalHeight, _rows[i][1].NaturalHeight);
                total += natural[i];
                if (i < _rows.Length - 1) total += Theme.Gap;
            }

            int viewport = _body == null ? 0 : _body.ClientSize.Height - _body.Padding.Vertical;
            int target = Math.Max(total, viewport);
            int extra = Math.Max(0, target - total);
            int share = _rows.Length > 0 ? extra / _rows.Length : 0;
            int remainder = extra - share * _rows.Length;

            if (_grid.Height != target) _grid.Height = target;
            for (int i = 0; i < _rows.Length; i++)
            {
                int height = natural[i] + share + (i == _rows.Length - 1 ? remainder : 0);
                if (i < _rows.Length - 1) height += Theme.Gap;
                if (Math.Abs(_grid.RowStyles[i].Height - height) > 0.5f)
                    _grid.RowStyles[i].Height = height;
            }
        }

        /// <summary>三行卡片按内容决定的总高度（不含被窗口拉高的部分）。</summary>
        private int NaturalContentHeight()
        {
            if (_rows == null) return 0;
            int total = 0;
            for (int i = 0; i < _rows.Length; i++)
            {
                total += Math.Max(_rows[i][0].NaturalHeight, _rows[i][1].NaturalHeight);
                if (i < _rows.Length - 1) total += Theme.Gap;
            }
            return total;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SyncAppearanceControls();
            FitHeightToContent();
        }

        /// <summary>内容比窗口矮时收掉底部空白，让最后一行卡片贴着状态栏。</summary>
        private void FitHeightToContent()
        {
            if (_body == null) return;
            if (_body.VerticalScroll.Visible) return;

            int content = NaturalContentHeight();
            if (content <= 0) return;

            int needed = Theme.HeaderHeight + _body.Padding.Vertical + content + Theme.StatusHeight;
            int slack = ClientSize.Height - needed;
            if (slack <= 8) return;

            int newClient = ClientSize.Height - slack;
            if (Height - ClientSize.Height + newClient < MinimumSize.Height) return;
            ClientSize = new Size(ClientSize.Width, newClient);
        }

        // ── 事件 ───────────────────────────────────────────────────────────

        private void WireEvents()
        {
            _device.DeviceList.SelectedIndexChanged += delegate { OnDeviceSelected(); };
            _trayDevices.DeviceList.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                // 程序化填充列表时也会触发 ItemCheck；构造阶段句柄还没建好，直接 BeginInvoke 会抛。
                if (_loading || !IsHandleCreated) return;
                BeginInvoke(new Action(SaveTrayVisibility));
            };
            _dpi.Read.Click += delegate { ReadDpi(); };
            _dpi.Write.Click += delegate { ApplyDpi(); };
            _dpi.Rescan.Click += delegate { _app.RefreshNow(); };
            // 用户手改数值就记下来：后台每个刷新间隔的自动重读不许把正在改的数字抢回去。
            _dpi.X.ValueChanged += delegate { MarkDpiEdited(); };
            _dpi.Y.ValueChanged += delegate { MarkDpiEdited(); };
            _dpi.Stage.ValueChanged += delegate { MarkDpiEdited(); };
            _profiles.Profiles.SelectedIndexChanged += delegate
            {
                MouseProfile p = _profiles.Profiles.SelectedItem as MouseProfile;
                if (p != null) _profiles.NameBox.Text = p.Name;
            };
            _profiles.Save.Click += delegate { SaveProfile(); };
            _profiles.Apply.Click += delegate { ApplySelectedProfile(); };
            _profiles.Rename.Click += delegate { RenameProfile(); };
            _profiles.Delete.Click += delegate { DeleteProfile(); };
            _profiles.Add.Click += delegate { AddProfile(); };
            _appearance.Save.Click += delegate { SaveSettings(); };
            _appearance.Close.Click += delegate { Hide(); };
            _about.Click += delegate { ShowAbout(); };
            _diagnose.Click += delegate { ShowDiagnostics(); };
            _diagnostics.OpenLog.Click += delegate { OpenLogFolder(); };
            _trayDevices.ThresholdDevice.SelectedIndexChanged += delegate { LoadThresholdForSelectedDevice(); };
            _trayDevices.UseGlobal.CheckedChanged += delegate { SaveThresholdForSelectedDevice(); };
            _trayDevices.Threshold.ValueChanged += delegate { SaveThresholdForSelectedDevice(); };

            // 编辑中的折算预览：值还没保存，按控件现值算给用户看。
            _appearance.Interval.SelectedIndexChanged += delegate { RefreshSleepPreview(); };
            _appearance.SleepMinutes.ValueChanged += delegate { RefreshSleepPreview(); };

            _volume.EndpointList.SelectedIndexChanged += delegate
            {
                if (_loading) return;
                VolumeService.Endpoint ep = _volume.EndpointList.SelectedItem as VolumeService.Endpoint;
                if (ep == null) return;
                int v = VolumeService.GetVolume(ep.Id);
                if (v >= 0) _volume.Level.Value = v;
                UpdateVolumeLabel();
            };
            _volume.Level.ValueChanged += delegate
            {
                if (_loading) return;
                VolumeService.Endpoint ep = _volume.EndpointList.SelectedItem as VolumeService.Endpoint;
                if (ep == null) return;
                if (VolumeService.SetVolume(ep.Id, _volume.Level.Value)) UpdateVolumeLabel();
            };

            // 键盘操作：Esc 关闭（等同隐藏），Tab 顺序按卡片视觉顺序。
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = true; }
            };
            ApplyTabOrder();
        }

        private void ApplyTabOrder()
        {
            Control[] order = { _device.DeviceList, _profiles.Profiles, _profiles.NameBox, _profiles.Save,
                                _profiles.Apply, _profiles.Rename, _profiles.Delete, _profiles.Add,
                                _volume.EndpointList, _volume.Level,
                                _trayDevices.DeviceList, _trayDevices.ThresholdDevice, _trayDevices.Threshold,
                                _trayDevices.UseGlobal,
                                _appearance.NumberMode, _appearance.IconMode, _appearance.Interval,
                                _appearance.LowThreshold, _appearance.SleepMinutes, _appearance.AutoStart,
                                _appearance.ChargeFullAlert, _appearance.DisconnectAlert,
                                _appearance.Save, _appearance.Close,
                                _dpi.X, _dpi.Y, _dpi.Stage, _dpi.Read, _dpi.Write, _dpi.Rescan };
            for (int i = 0; i < order.Length; i++) order[i].TabIndex = i;
        }

        private void RefreshSleepPreview()
        {
            if (_loading) return;
            _appearance.RefreshSleepHint(
                _appearance.Interval.SelectedIndex == 0 ? 15 : (_appearance.Interval.SelectedIndex == 1 ? 30 : 90),
                (int)_appearance.SleepMinutes.Value);
        }

        private void UpdateVolumeLabel()
        {
            _volume.Value.Text = _volume.Level.Value + "%";
        }

        // ── 状态同步（全部以中间层为准） ─────────────────────────────────

        private void OnStateChanged(object sender, StateChangedEventArgs e)
        {
            if (IsDisposed) return;
            if (e.Area == StateArea.Appearance)
            {
                UiInvoke(SyncAppearanceControls);
            }
            else if (e.Area == StateArea.Profiles)
            {
                UiInvoke(delegate
                {
                    if (_loading) return;
                    bool wasLoading = _loading;
                    _loading = true;
                    try { ReloadProfiles(); }
                    finally { _loading = wasLoading; }
                });
            }
            else if (e.Area == StateArea.Dpi)
            {
                UiInvoke(ShowCachedDpi);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _state.Changed -= OnStateChanged;
            }
            base.Dispose(disposing);
        }

        private void LoadSettingsUi()
        {
            _loading = true;
            try
            {
                SyncAppearanceControls();
                ReloadProfiles();
                LoadVolumeEndpoints();
            }
            finally { _loading = false; }
        }

        /// <summary>把外观控件重新对齐到中间层状态（只读回写控件，不反向写状态）。</summary>
        private void SyncAppearanceControls()
        {
            if (IsDisposed) return;
            bool wasLoading = _loading;
            _loading = true;
            try
            {
                _appearance.NumberMode.Checked = _state.DisplayMode == 0;
                _appearance.IconMode.Checked = _state.DisplayMode == 1;
                _appearance.Interval.SelectedIndex = _state.RefreshSeconds == 15 ? 0 : (_state.RefreshSeconds == 30 ? 1 : 2);
                _appearance.LowThreshold.Value = Clamp(_appearance.LowThreshold, _state.LowThreshold);
                _appearance.SleepMinutes.Value = Clamp(_appearance.SleepMinutes, _state.SleepTimeoutMinutes);
                _appearance.ChargeFullAlert.Checked = _state.ChargeFullAlert;
                _appearance.DisconnectAlert.Checked = _state.DisconnectAlert;
                _appearance.AutoStart.Checked = AppConfig.IsAutoStartEnabled();
                _appearance.RefreshSleepHint(_state.RefreshSeconds, _state.SleepTimeoutMinutes);
            }
            finally { _loading = wasLoading; }
        }

        private static decimal Clamp(NumericUpDown box, int value)
        {
            return Math.Min(box.Maximum, Math.Max(box.Minimum, value));
        }

        private void LoadVolumeEndpoints()
        {
            List<VolumeService.Endpoint> endpoints = VolumeService.ListHeadsetEndpoints();
            _volume.EndpointList.Items.Clear();
            foreach (VolumeService.Endpoint ep in endpoints) _volume.EndpointList.Items.Add(ep);
            _volume.SetAvailable(endpoints.Count > 0);
            PerformLayout();
            if (endpoints.Count > 0)
            {
                _volume.EndpointList.SelectedIndex = 0;
                int v = VolumeService.GetVolume(endpoints[0].Id);
                _volume.Level.Value = v >= 0 ? v : 50;
                UpdateVolumeLabel();
            }
        }

        private void ReloadProfiles()
        {
            _profiles.Profiles.Items.Clear();
            foreach (MouseProfile p in _state.Profiles) _profiles.Profiles.Items.Add(p);
            int pick = -1;
            string active = _state.ActiveProfile;
            if (!string.IsNullOrEmpty(active))
            {
                for (int i = 0; i < _profiles.Profiles.Items.Count; i++)
                {
                    MouseProfile p = _profiles.Profiles.Items[i] as MouseProfile;
                    if (p != null && p.Name == active) { pick = i; break; }
                }
            }
            if (pick < 0 && _profiles.Profiles.Items.Count > 0) pick = 0;
            if (pick >= 0)
            {
                _profiles.Profiles.SelectedIndex = pick;
                MouseProfile p = _profiles.Profiles.Items[pick] as MouseProfile;
                if (p != null) _profiles.NameBox.Text = p.Name;
            }
            else
            {
                _profiles.NameBox.Text = "";
            }
            _profiles.SetEmptyState(_profiles.Profiles.Items.Count == 0);
            // 有没有配置会改变卡片高度，行高要重算。
            PerformLayout();
        }

        private void UpdateCurrentDpiText(string text)
        {
            _profiles.CurrentDpi.Text = text;
        }

        public void RefreshDevices()
        {
            if (IsDisposed) return;
            string selected = _state.SelectedDeviceId;
            bool wasLoading = _loading;
            _loading = true;
            try
            {
                _device.DeviceList.Items.Clear();
                _trayDevices.DeviceList.Items.Clear();
                foreach (DeviceSnapshot s in _service.Snapshot())
                {
                    _device.DeviceList.Items.Add(new DeviceItem(s.Info, s.Battery.StatusText));
                    _trayDevices.DeviceList.Items.Add(new TrayDeviceItem(s.Info), _state.IsTrayVisible(s.Info.Id));
                }
                int selectIndex = 0;
                for (int i = 0; i < _device.DeviceList.Items.Count; i++)
                {
                    DeviceItem item = _device.DeviceList.Items[i] as DeviceItem;
                    if (item != null && item.Device.Id == selected) { selectIndex = i; break; }
                }
                if (_device.DeviceList.Items.Count > 0) _device.DeviceList.SelectedIndex = selectIndex;
                ReloadThresholdDevices();
                RefreshDiagnostics();
                UpdateDeviceLabels();
                // 列表行数变了，卡片高度跟着变：重新走一遍列对齐。
                PerformLayout();
            }
            finally
            {
                _loading = wasLoading;
            }
        }

        private void SaveTrayVisibility()
        {
            if (IsDisposed || !_trayDevices.DeviceList.IsHandleCreated) return;
            List<string> ids = new List<string>();
            int checkedCount = 0;
            for (int i = 0; i < _trayDevices.DeviceList.Items.Count; i++)
            {
                if (_trayDevices.DeviceList.GetItemChecked(i))
                {
                    TrayDeviceItem item = _trayDevices.DeviceList.Items[i] as TrayDeviceItem;
                    if (item != null) { ids.Add(item.Device.Id); checkedCount++; }
                }
            }
            _state.SetTrayVisibleIds((checkedCount == _trayDevices.DeviceList.Items.Count || _trayDevices.DeviceList.Items.Count == 0)
                ? "" : string.Join(";", ids.ToArray()));
            _app.RefreshTrayIcons();
        }

        private void OnDeviceSelected()
        {
            DeviceItem item = _device.DeviceList.SelectedItem as DeviceItem;
            if (item == null) return;
            _state.SetSelectedDevice(item.Device.Id);
            UpdateDeviceLabels();
            // 换设备时催一次；真正读不读由中间层按节奏节流，这里只是尽早补上。
            if (item.Device.Id != _dpiDeviceId)
            {
                _dpiDeviceId = item.Device.Id;
                _dpiEdited = false;
                ShowCachedDpi();
                _app.ProbeDpi();
            }
        }

        private void UpdateDeviceLabels()
        {
            DeviceItem item = _device.DeviceList.SelectedItem as DeviceItem;
            if (item == null)
            {
                _device.Model.Text = "未检测到设备";
                _device.Battery.Text = "--";
                _device.Sensor.Text = "--";
                UpdateCurrentDpiText("当前板载 DPI：--");
                return;
            }
            _device.Model.Text = TypeName(item.Device.Kind) + " · " + item.Device.DisplayName;
            _device.Battery.Text = item.BatteryText + (item.Device.SupportsDpi ? "" : " · 无 DPI");
            _device.Sensor.Text = string.IsNullOrEmpty(item.Device.Sensor) ? "--" : item.Device.Sensor;
            ShowCachedDpi();
        }

        private static string TypeName(DeviceKind kind)
        {
            if (kind == DeviceKind.Keyboard) return "键盘";
            if (kind == DeviceKind.Headset) return "耳机";
            return "鼠标";
        }

        private DeviceInfo SelectedDevice()
        {
            DeviceItem item = _device.DeviceList.SelectedItem as DeviceItem;
            return item == null ? null : item.Device;
        }

        // ── DPI ───────────────────────────────────────────────────────────

        /// <summary>
        /// 显示中间层里存下的板载值。板载是硬件里的档位，读到一次就一直显示：
        /// 启动 / 自启动、手动读取、手动写入、以及每个刷新间隔的自动重读会改它，
        /// 设备休眠时保持不动；运行时两次重读之间界面上的值不会跳成未知。
        /// </summary>
        private void ShowCachedDpi()
        {
            DeviceInfo device = SelectedDevice();
            if (device == null)
            {
                UpdateCurrentDpiText("当前板载 DPI：--");
                return;
            }
            if (!device.SupportsDpi)
            {
                UpdateCurrentDpiText("当前板载 DPI：该设备没有板载 DPI");
                return;
            }

            DpiSnapshot cached = _state.CachedDpi(device.Id);
            if (cached != null)
            {
                ApplyDpiToUi(cached);
                UpdateCurrentDpiText("当前板载 DPI：" + cached.Text);
                return;
            }
            UpdateCurrentDpiText(_state.DpiWindowActive ? "当前板载 DPI：读取中…" : "当前板载 DPI：--");
        }

        private void ApplyDpiToUi(DpiSnapshot s)
        {
            if (s == null || !s.IsValid) return;
            // 用户手改过、还没写入：只让上面那行「当前板载 DPI」跟着硬件走，输入框留着用户的值。
            if (_dpiEdited) return;
            _dpiSyncing = true;
            try
            {
                _dpi.X.Value = Math.Min(_dpi.X.Maximum, Math.Max(_dpi.X.Minimum, s.X));
                _dpi.Y.Value = Math.Min(_dpi.Y.Maximum, Math.Max(_dpi.Y.Minimum, s.Y > 0 ? s.Y : s.X));
                if (s.Stage > 0)
                    _dpi.Stage.Value = Math.Min(_dpi.Stage.Maximum, Math.Max(_dpi.Stage.Minimum, s.Stage));
            }
            finally { _dpiSyncing = false; }
        }

        /// <summary>记下「用户改过输入框」；程序自己填值时不算（_dpiSyncing / _loading 期间忽略）。</summary>
        private void MarkDpiEdited()
        {
            if (_dpiSyncing || _loading) return;
            _dpiEdited = true;
        }

        /// <summary>手动「读取硬件 DPI」：不管缓存里有没有值都去敲一次接口。</summary>
        private void ReadDpi()
        {
            DeviceInfo device = SelectedDevice();
            if (device == null) { SetStatus("请先选择设备"); return; }
            if (!device.SupportsDpi) { SetStatus("该设备没有板载 DPI"); return; }
            if (_dpiReading) return;
            _dpiReading = true;
            SetStatus("正在读取硬件 DPI...");

            ThreadPool.QueueUserWorkItem(delegate
            {
                DpiReading r = _service.ReadDpi(device);
                UiInvoke(delegate
                {
                    _dpiReading = false;
                    if (r.X > 0)
                    {
                        _state.RememberDpi(device.Id, r.X, r.Y, r.Stage);
                        _dpiEdited = false;
                        ShowCachedDpi();
                        SetStatus("硬件 DPI：" + r.Text);
                        return;
                    }
                    // 读失败不动已显示的板载值：休眠只是这一次没应答，不代表板载值变了。
                    SetStatus("读取失败：" + (string.IsNullOrEmpty(r.Text) ? "设备未响应" : r.Text));
                    ShowCachedDpi();
                });
            });
        }

        private void ApplyDpi()
        {
            DeviceInfo device = SelectedDevice();
            if (device == null) { SetStatus("请先选择设备"); return; }
            int x = (int)_dpi.X.Value, y = (int)_dpi.Y.Value, stage = (int)_dpi.Stage.Value;
            SetStatus("正在写入 " + x + "×" + y + " ...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = _service.WriteDpi(device, x, y, stage);
                UiInvoke(delegate
                {
                    if (ok)
                    {
                        // 写入成功即视为新的板载值：手动修改是允许改板载值的时机之一。
                        _state.RememberDpi(device.Id, x, y, stage);
                        _dpiEdited = false;
                        ShowCachedDpi();
                    }
                    SetStatus(ok ? "写入成功并已回读确认" : "写入失败：休眠 / 型号未识别 / 被占用");
                });
            });
        }

        // ── 配置档位 ─────────────────────────────────────────────────────

        private void SaveProfile()
        {
            string name = _profiles.NameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) name = _dpi.X.Value + " DPI";
            MouseProfile p = _state.FindProfile(name);
            bool isNew = p == null;
            if (p == null) p = new MouseProfile();
            p.Name = name;
            p.DpiX = (int)_dpi.X.Value;
            p.DpiY = (int)_dpi.Y.Value;
            p.Stage = (int)_dpi.Stage.Value;
            DeviceInfo d = SelectedDevice();
            p.DeviceId = d == null ? "" : d.Id;
            if (isNew) _state.Profiles.Add(p);
            _state.ProfilesChanged(p.Name);
            ReloadProfiles();
            SetStatus(isNew ? "已新建配置 " + name : "已更新配置 " + name);
        }

        private void ApplySelectedProfile()
        {
            MouseProfile p = _profiles.Profiles.SelectedItem as MouseProfile;
            if (p == null) { SetStatus("请先选择配置"); return; }
            _dpi.X.Value = Math.Min(_dpi.X.Maximum, Math.Max(_dpi.X.Minimum, p.DpiX));
            _dpi.Y.Value = Math.Min(_dpi.Y.Maximum, Math.Max(_dpi.Y.Minimum, p.DpiY));
            _dpi.Stage.Value = Math.Min(_dpi.Stage.Maximum, Math.Max(_dpi.Stage.Minimum, p.Stage));
            // 应用配置会真的写进硬件，输入框和板载值此刻一致，不再算「用户手改」。
            _dpiEdited = false;
            _app.ApplyProfile(p);
            SetStatus("应用配置 " + p.Name);
        }

        private void RenameProfile()
        {
            MouseProfile p = _profiles.Profiles.SelectedItem as MouseProfile;
            if (p == null) { SetStatus("请先选择配置"); return; }
            string name = _profiles.NameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) { SetStatus("请输入新名称"); return; }
            p.Name = name;
            _state.ProfilesChanged(name);
            ReloadProfiles();
            SetStatus("已重命名");
        }

        private void DeleteProfile()
        {
            MouseProfile p = _profiles.Profiles.SelectedItem as MouseProfile;
            if (p == null) { SetStatus("请先选择配置"); return; }
            _state.Profiles.Remove(p);
            _state.ProfilesChanged(null);
            ReloadProfiles();
            SetStatus("已删除配置");
        }

        private void AddProfile()
        {
            MouseProfile p = new MouseProfile();
            p.Name = "新配置 " + (_state.Profiles.Count + 1);
            p.DpiX = (int)_dpi.X.Value;
            p.DpiY = (int)_dpi.Y.Value;
            p.Stage = (int)_dpi.Stage.Value;
            _state.Profiles.Add(p);
            _state.ProfilesChanged(null);
            ReloadProfiles();
            _profiles.NameBox.Text = p.Name;
            SetStatus("已新建配置");
        }

        // ── 阈值 ─────────────────────────────────────────────────────────

        private sealed class ThresholdDeviceItem
        {
            public readonly DeviceInfo Device;
            public ThresholdDeviceItem(DeviceInfo device) { Device = device; }
            public override string ToString() { return Device.DisplayName; }
        }

        /// <summary>设备列表没变化时不重建下拉框，避免定时刷新打断正在编辑的阈值。</summary>
        private void ReloadThresholdDevices()
        {
            List<DeviceSnapshot> snapshots = _service.Snapshot();
            StringBuilder ids = new StringBuilder();
            foreach (DeviceSnapshot s in snapshots) ids.Append(s.Info.Id).Append('|');
            string signature = ids.ToString();
            bool same = signature == _thresholdDeviceIds && _trayDevices.ThresholdDevice.Items.Count > 0;
            if (same) return;

            object current = _trayDevices.ThresholdDevice.SelectedItem;
            string selectedId = current is ThresholdDeviceItem ? ((ThresholdDeviceItem)current).Device.Id : "";
            _trayDevices.ThresholdDevice.Items.Clear();
            int pick = -1;
            foreach (DeviceSnapshot s in snapshots)
            {
                _trayDevices.ThresholdDevice.Items.Add(new ThresholdDeviceItem(s.Info));
                if (string.Equals(s.Info.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                    pick = _trayDevices.ThresholdDevice.Items.Count - 1;
            }
            _thresholdDeviceIds = signature;
            if (pick < 0 && _trayDevices.ThresholdDevice.Items.Count > 0) pick = 0;
            bool wasLoading = _loading;
            _loading = true;
            try
            {
                _trayDevices.ThresholdDevice.SelectedIndex = pick;
            }
            finally { _loading = wasLoading; }
            LoadThresholdForSelectedDevice();
        }

        private void LoadThresholdForSelectedDevice()
        {
            if (_loading) return;
            ThresholdDeviceItem item = _trayDevices.ThresholdDevice.SelectedItem as ThresholdDeviceItem;
            bool wasLoading = _loading;
            _loading = true;
            try
            {
                if (item == null)
                {
                    _trayDevices.Threshold.Enabled = false;
                    _trayDevices.Threshold.Value = _trayDevices.Threshold.Minimum;
                    return;
                }
                bool overridden = _state.HasLowThresholdOverride(item.Device.Id);
                _trayDevices.UseGlobal.Checked = !overridden;
                _trayDevices.Threshold.Enabled = overridden;
                _trayDevices.Threshold.Value = Clamp(_trayDevices.Threshold, _state.LowThresholdFor(item.Device.Id));
            }
            finally { _loading = wasLoading; }
        }

        private void SaveThresholdForSelectedDevice()
        {
            if (_loading) return;
            ThresholdDeviceItem item = _trayDevices.ThresholdDevice.SelectedItem as ThresholdDeviceItem;
            if (item == null) return;
            _trayDevices.Threshold.Enabled = !_trayDevices.UseGlobal.Checked;
            int value = _trayDevices.UseGlobal.Checked
                ? _state.LowThreshold
                : (int)_trayDevices.Threshold.Value;
            _state.SetLowThresholdOverride(item.Device.Id, value);
            SetStatus(_trayDevices.UseGlobal.Checked
                ? item.Device.DisplayName + "：跟随全局"
                : item.Device.DisplayName + "：" + value + "%");
        }

        // ── 诊断 / 关于 ──────────────────────────────────────────────────

        private void RefreshDiagnostics()
        {
            if (IsDisposed) return;
            List<string> errors = _service.RecentErrors();
            _diagnostics.Errors.Text = errors.Count == 0
                ? "暂无读取失败记录。"
                : string.Join(Environment.NewLine, errors.ToArray());
        }

        private void ShowDiagnostics()
        {
            RefreshDiagnostics();
            using (Form dialog = new Form())
            {
                dialog.Text = "读取诊断";
                dialog.ClientSize = new Size(600, _diagnostics.NaturalHeight + 40);
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.BackColor = Theme.Back;
                dialog.Padding = new Padding(Theme.Pad);

                // 同一个控件只能有一个父容器：弹窗期间挂过来，关掉再摘掉。
                _diagnostics.Dock = DockStyle.Fill;
                dialog.Controls.Add(_diagnostics);
                dialog.ShowDialog(this);
                dialog.Controls.Remove(_diagnostics);
                _diagnostics.Dock = DockStyle.None;
                _diagnostics.Height = _diagnostics.NaturalHeight;
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + AppConfig.ConfigDir + "\"");
                SetStatus("已打开配置目录");
            }
            catch { SetStatus("无法打开配置目录"); }
        }

        private static string AppVersion
        {
            get { return typeof(Program).Assembly.GetName().Version.ToString(3); }
        }

        private void ShowAbout()
        {
            using (AboutForm about = new AboutForm())
            {
                about.ShowDialog(this);
            }
        }

        private void SaveSettings()
        {
            bool autostartOk = AppConfig.SetAutoStart(_appearance.AutoStart.Checked);
            _state.ApplyAppearance(
                _appearance.IconMode.Checked ? 1 : 0,
                _appearance.Interval.SelectedIndex == 0 ? 15 : (_appearance.Interval.SelectedIndex == 1 ? 30 : 90),
                (int)_appearance.LowThreshold.Value,
                (int)_appearance.SleepMinutes.Value,
                _appearance.ChargeFullAlert.Checked,
                _appearance.DisconnectAlert.Checked);
            _app.ResetRefreshClock();
            SetStatus(autostartOk ? "设置已保存"
                : "设置已保存，开机自启失败：" + (AppConfig.LastAutoStartError ?? "原因未知"));
        }

        private void SetStatus(string text)
        {
            if (IsDisposed) return;
            UiInvoke(delegate { _status.Text = text; });
        }

        private void UiInvoke(Action action)
        {
            if (IsHandleCreated && InvokeRequired) BeginInvoke(action);
            else action();
        }

        private sealed class DeviceItem
        {
            public readonly DeviceInfo Device;
            public readonly string BatteryText;
            public DeviceItem(DeviceInfo device, string batteryText) { Device = device; BatteryText = batteryText; }
            public override string ToString() { return Device.DisplayName + " · " + BatteryText; }
        }

        private sealed class TrayDeviceItem
        {
            public readonly DeviceInfo Device;
            public TrayDeviceItem(DeviceInfo device) { Device = device; }
            public override string ToString()
            {
                string type = Device.Kind == DeviceKind.Keyboard ? "键盘" : (Device.Kind == DeviceKind.Headset ? "耳机" : "鼠标");
                return "[" + type + "] " + Device.DisplayName;
            }
        }
    }
}
