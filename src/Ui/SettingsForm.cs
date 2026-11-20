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
        private FlowLayoutPanel _left;
        private FlowLayoutPanel _right;

        private bool _loading;
        private string _thresholdDeviceIds = "";
        private bool _aligning;

        public SettingsForm(TrayApp app)
        {
            _app = app;
            _service = app.Service;
            _state = app.State;

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
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
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
            Theme.StyleGhost(_about);
            Theme.StyleGhost(_diagnose);
            _about.Size = new Size(76, Theme.RowHeight);
            _diagnose.Size = new Size(88, Theme.RowHeight);
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

            TableLayoutPanel columns = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Theme.Back
            };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _left = ColumnPanel(true);
            _right = ColumnPanel(false);
            columns.Controls.Add(_left, 0, 0);
            columns.Controls.Add(_right, 1, 0);

            // 左侧放“这台设备”：设备 → 配置 → 板载 DPI；
            // 右侧放“怎么显示”：托盘 → 外观 → 音量。两列自然高度只差十几像素，
            // 余量交给列表吸收，不会出现某张卡片被拉长后一片空白。
            _left.Controls.Add(_device);
            _left.Controls.Add(_profiles);
            _left.Controls.Add(_dpi);

            _right.Controls.Add(_trayDevices);
            _right.Controls.Add(_appearance);
            _right.Controls.Add(_volume);

            body.Controls.Add(columns);
            return body;
        }

        private static FlowLayoutPanel ColumnPanel(bool left)
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                BackColor = Theme.Back,
                Padding = new Padding(0),
                // 两列等宽、中间留 18px：外侧内边距与中间沟槽一致，视觉上左右对称。
                Margin = left ? new Padding(0, 0, 9, 0) : new Padding(9, 0, 0, 0)
            };
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
            if (_aligning || _left == null) return;
            _aligning = true;
            try
            {
                SizeCards(_left);
                SizeCards(_right);
                AlignColumns();
            }
            finally
            {
                _aligning = false;
            }
        }

        private static void SizeCards(FlowLayoutPanel parent)
        {
            int width = parent.ClientSize.Width;
            if (width < 240) return;
            foreach (Control c in parent.Controls)
                if (c is Card && c.Width != width) c.Width = width;
        }

        /// <summary>两列高度不一致时，把差给可伸缩的卡片（列表类），而不是在卡片底部留白。</summary>
        private void AlignColumns()
        {
            if (_left == null || _right == null) return;
            // 每轮先把间距和高度复位，避免上一轮的拉伸累积成越来越大。
            foreach (Control c in _left.Controls) { ResetCardHeight(c); c.Margin = new Padding(0, 0, 0, Theme.Gap); }
            foreach (Control c in _right.Controls) { ResetCardHeight(c); c.Margin = new Padding(0, 0, 0, Theme.Gap); }

            int leftHeight = StackHeight(_left);
            int rightHeight = StackHeight(_right);
            if (leftHeight <= 0 || rightHeight <= 0 || leftHeight == rightHeight) return;

            FlowLayoutPanel shorter = leftHeight < rightHeight ? _left : _right;
            int slack = Math.Abs(rightHeight - leftHeight);

            // 先按列内间距均摊：卡片保持自然高度，只把间距拉松，底边自然齐平。
            slack = DistributeGaps(shorter, slack);
            if (slack <= 0) return;

            int absorbed = 0;
            foreach (Control c in shorter.Controls)
            {
                if (absorbed >= slack) break;
                Card card = c as Card;
                if (card != null) absorbed += card.AbsorbSlack(slack - absorbed);
            }

            // 没有可伸缩的卡片时，退化为拉伸最后一张卡，至少保持两列底边齐平。
            if (absorbed < slack)
            {
                Card last = LastCard(shorter);
                if (last != null) last.Height = last.Height + (slack - absorbed);
            }
        }

        /// <summary>把高度差摊到列内卡片间距上（每张最多多 14px），返回没摊完的余量。</summary>
        private static int DistributeGaps(FlowLayoutPanel column, int slack)
        {
            int cards = 0;
            foreach (Control c in column.Controls) if (c.Visible) cards++;
            if (cards == 0) return slack;

            int per = Math.Min(14, slack / cards);
            if (per <= 0) return slack;

            int distributed = 0;
            int i = 0;
            foreach (Control c in column.Controls)
            {
                if (!c.Visible) continue;
                int extra = i < slack - per * cards ? per + 1 : per;
                c.Margin = new Padding(0, 0, 0, Theme.Gap + extra);
                distributed += extra;
                i++;
            }
            return slack - distributed;
        }

        private static void ResetCardHeight(Control c)
        {
            Card card = c as Card;
            if (card != null && card.Height != card.NaturalHeight) card.Height = card.NaturalHeight;
        }

        private static int StackHeight(FlowLayoutPanel panel)
        {
            int height = panel.Padding.Top;
            foreach (Control c in panel.Controls)
                if (c.Visible) height += c.Height + c.Margin.Vertical;
            return height + panel.Padding.Bottom;
        }

        private static Card LastCard(FlowLayoutPanel panel)
        {
            Card last = null;
            foreach (Control c in panel.Controls)
            {
                Card card = c as Card;
                if (card != null && card.Visible) last = card;
            }
            return last;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SyncAppearanceControls();
            FitHeightToContent();
        }

        /// <summary>内容比窗口矮时收掉底部空白，让卡片贴着状态栏。</summary>
        private void FitHeightToContent()
        {
            if (_left == null || _right == null || _body == null) return;
            if (_body.VerticalScroll.Visible) return;

            int contentBottom = Math.Max(StackHeight(_left), StackHeight(_right));
            if (contentBottom <= 0) return;

            int needed = Theme.HeaderHeight + _body.Padding.Vertical + contentBottom + Theme.StatusHeight;
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _state.Changed -= OnStateChanged;
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
            ReadDpi();
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

        private void ReadDpi()
        {
            DeviceInfo device = SelectedDevice();
            if (device == null) { SetStatus("请先选择设备"); return; }
            SetStatus("正在读取硬件 DPI...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                DpiReading r = _service.ReadDpi(device);
                UiInvoke(delegate
                {
                    if (r.X > 0)
                    {
                        _dpi.X.Value = Math.Min(_dpi.X.Maximum, Math.Max(_dpi.X.Minimum, r.X));
                        _dpi.Y.Value = Math.Min(_dpi.Y.Maximum, Math.Max(_dpi.Y.Minimum, r.Y > 0 ? r.Y : r.X));
                        if (r.Stage > 0) _dpi.Stage.Value = Math.Min(_dpi.Stage.Maximum, Math.Max(_dpi.Stage.Minimum, r.Stage));
                        UpdateCurrentDpiText("当前板载 DPI：" + r.X + " × " + (r.Y > 0 ? r.Y : r.X) +
                                             (r.Stage > 0 ? "（档 " + r.Stage + "）" : ""));
                    }
                    else
                    {
                        UpdateCurrentDpiText("当前板载 DPI：无法读取（" + (string.IsNullOrEmpty(r.Text) ? "设备未响应" : r.Text) + "）");
                    }
                    SetStatus("硬件 DPI：" + r.Text);
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
                UiInvoke(delegate { SetStatus(ok ? "写入成功并已回读确认" : "写入失败：休眠 / 型号未识别 / 被占用"); });
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
            MessageBox.Show(this,
                "HYZ Mouse Hub v" + AppVersion + "\r\n\r\n" +
                "把无线外设的电量放到任务栏一眼可见，附带板载 DPI 与多套配置。\r\n" +
                "单文件绿色程序，无遥测、不联网。\r\n\r\n" +
                "github.com/rogergzl/HYZMouseHub",
                "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
