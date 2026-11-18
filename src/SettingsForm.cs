using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace HyzMouseHub
{
    internal sealed class SettingsForm : Form
    {
        private readonly TrayApp _app;
        private readonly DeviceService _service;
        private readonly AppConfig _config;

        private readonly DeviceCard _device = new DeviceCard();
        private readonly TrayDevicesCard _trayDevices = new TrayDevicesCard();
        private readonly DpiCard _dpi = new DpiCard();
        private readonly ProfileCard _profiles = new ProfileCard();
        private readonly AppearanceCard _appearance = new AppearanceCard();
        private readonly VolumeCard _volume = new VolumeCard();
        private readonly SafetyCard _safety = new SafetyCard();
        private readonly Label _status = new Label();
        private Panel _body;

        private bool _loading;

        public SettingsForm(TrayApp app)
        {
            _app = app;
            _service = app.Service;
            _config = app.Config;

            Text = "HYZ Mouse Hub";
            Icon = AppIcon.Shared;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(1040, 880);
            MinimumSize = new Size(940, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(0xF3, 0xF6, 0xF9);
            Font = new Font("Segoe UI", 9.5F);
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            };

            BuildUi();
            WireEvents();
            LoadSettingsUi();
            RefreshDevices();
        }

        private void BuildUi()
        {
            Panel header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Color.White };
            Controls.Add(header);

            Label title = new Label
            {
                Text = "HYZ Mouse Hub",
                Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 30, 30),
                Location = new Point(20, 8),
                Size = new Size(280, 30)
            };
            header.Controls.Add(title);

            Label subtitle = new Label
            {
                Text = "ATK / 雷蛇 / LEOBOG / EPOS 电量 · 板载 DPI · 多配置 · 多设备托盘",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(110, 110, 110),
                Location = new Point(21, 38),
                Size = new Size(600, 22)
            };
            header.Controls.Add(subtitle);

            Panel headerLine = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = Color.FromArgb(0xE2, 0xE8, 0xEE)
            };
            header.Controls.Add(headerLine);

            _body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 12), AutoScroll = true };
            Panel body = _body;
            Controls.Add(body);

            TableLayoutPanel columns = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            body.Controls.Add(columns);

            FlowLayoutPanel left = ColumnPanel();
            FlowLayoutPanel right = ColumnPanel();
            columns.Controls.Add(left, 0, 0);
            columns.Controls.Add(right, 1, 0);

            left.Controls.Add(_device);
            left.Controls.Add(_trayDevices);
            left.Controls.Add(_dpi);
            left.Controls.Add(_safety);

            right.Controls.Add(_profiles);
            right.Controls.Add(_appearance);
            right.Controls.Add(_volume);

            _status.Dock = DockStyle.Bottom;
            _status.Height = 30;
            _status.BackColor = Color.White;
            _status.ForeColor = Color.FromArgb(80, 80, 80);
            _status.Font = new Font("Segoe UI", 9.5F);
            _status.Padding = new Padding(16, 6, 0, 0);
            _status.Text = "就绪";
            Controls.Add(_status);

            // WinForms 从 Controls 末尾开始停靠：Fill 必须第一个，Header 其次，状态栏最后。
            Controls.SetChildIndex(body, 0);
            Controls.SetChildIndex(header, 1);
            Controls.SetChildIndex(_status, 2);
        }

        private static FlowLayoutPanel ColumnPanel()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 0, 6, 0)
            };
        }

        private bool _aligning;

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_aligning) return;
            _aligning = true;
            try
            {
                // 卡片宽度跟随所在列，内容始终完整可见，不依赖写死的窗口高度。
                SizeCards(_device.Parent as FlowLayoutPanel);
                SizeCards(_profiles.Parent as FlowLayoutPanel);
                AlignColumns();
            }
            finally
            {
                _aligning = false;
            }
        }

        /// <summary>两列高度不一致时，把较短一列的最后一张卡片拉伸到与另一列底部齐平。</summary>
        private void AlignColumns()
        {
            FlowLayoutPanel left = _device.Parent as FlowLayoutPanel;
            FlowLayoutPanel right = _profiles.Parent as FlowLayoutPanel;
            if (left == null || right == null) return;

            foreach (Control c in left.Controls) ResetCardHeight(c);
            foreach (Control c in right.Controls) ResetCardHeight(c);

            int lb = ContentBottom(left);
            int rb = ContentBottom(right);
            if (lb <= 0 || rb <= 0) return;
            // 内容超出可视区域时交给滚动，不做拉伸，避免卡片被压扁。
            if (lb > left.ClientSize.Height || rb > right.ClientSize.Height) return;
            if (lb == rb) return;

            FlowLayoutPanel shorter = lb < rb ? left : right;
            int diff = Math.Abs(rb - lb);
            Card last = LastCard(shorter);
            if (last != null) last.Height = last.NaturalHeight + diff;
        }

        private static void ResetCardHeight(Control c)
        {
            Card card = c as Card;
            if (card != null && card.Height != card.NaturalHeight) card.Height = card.NaturalHeight;
        }

        private static int ContentBottom(FlowLayoutPanel panel)
        {
            int bottom = 0;
            foreach (Control c in panel.Controls)
                if (c.Visible && c.Bottom > bottom) bottom = c.Bottom;
            return bottom;
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
            // 布局稳定后把窗口底部多余空白收掉，让卡片紧贴状态栏。
            FitHeightToContent();
        }

        private void FitHeightToContent()
        {
            FlowLayoutPanel left = _device.Parent as FlowLayoutPanel;
            FlowLayoutPanel right = _profiles.Parent as FlowLayoutPanel;
            if (left == null || right == null || _body == null) return;
            if (_body.VerticalScroll.Visible) return; // 内容已经超出，交给滚动

            int cardsBottom = Math.Max(ContentBottom(left), ContentBottom(right));
            if (cardsBottom <= 0) return;

            // 末尾 6px 是卡片下边距 + 滚动条余量，避免收缩后反而出现滚动条。
            int needed = _body.Top + _body.Padding.Top + cardsBottom + 16 + _body.Padding.Bottom + _status.Height;
            int slack = ClientSize.Height - needed;
            if (slack <= 10) return;

            int newClient = ClientSize.Height - slack;
            if (Height - ClientSize.Height + newClient < MinimumSize.Height) return;
            ClientSize = new Size(ClientSize.Width, newClient);
        }

        private static void SizeCards(FlowLayoutPanel parent)
        {
            if (parent == null) return;
            int width = parent.ClientSize.Width - 12;
            if (width < 200) return;
            foreach (Control c in parent.Controls)
                if (c is Card && c.Width != width) c.Width = width;
        }

        private void WireEvents()
        {
            _device.DeviceList.SelectedIndexChanged += delegate { OnDeviceSelected(); };
            _trayDevices.DeviceList.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                // 程序化填充列表时也会触发 ItemCheck；构造函数阶段句柄还没建好，
                // 直接 BeginInvoke 会抛 InvalidOperationException，所以必须双重保护。
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
        }

        private void UpdateVolumeLabel()
        {
            _volume.Value.Text = _volume.Level.Value + "%";
        }

        private void LoadSettingsUi()
        {
            _loading = true;
            _appearance.NumberMode.Checked = _config.DisplayMode == 0;
            _appearance.IconMode.Checked = _config.DisplayMode == 1;
            _appearance.Interval.SelectedIndex = _config.RefreshSeconds == 15 ? 0 : (_config.RefreshSeconds == 30 ? 1 : 2);
            _appearance.LowThreshold.Value = _config.LowThreshold;
            _appearance.AutoStart.Checked = AppConfig.IsAutoStartEnabled();
            ReloadProfiles();
            LoadVolumeEndpoints();
            _loading = false;
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
            foreach (MouseProfile p in _config.Profiles) _profiles.Profiles.Items.Add(p);
            int pick = -1;
            if (!string.IsNullOrEmpty(_config.ActiveProfile))
            {
                for (int i = 0; i < _profiles.Profiles.Items.Count; i++)
                {
                    MouseProfile p = _profiles.Profiles.Items[i] as MouseProfile;
                    if (p != null && p.Name == _config.ActiveProfile) { pick = i; break; }
                }
            }
            // 没有“当前配置”时默认选中第一条，避免名称框一片空白。
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
            // 列表为空时不显示占位行，改为提示“先读取再保存”。
            _profiles.SetEmptyState(_profiles.Profiles.Items.Count == 0);
        }

        private void UpdateCurrentDpiText(string text)
        {
            _profiles.CurrentDpi.Text = text;
        }

        public void RefreshDevices()
        {
            if (IsDisposed) return;
            string selected = _config.SelectedDeviceId;
            bool wasLoading = _loading;
            _loading = true;
            try
            {
                _device.DeviceList.Items.Clear();
                _trayDevices.DeviceList.Items.Clear();
                foreach (DeviceSnapshot s in _service.Snapshot())
                {
                    _device.DeviceList.Items.Add(new DeviceItem(s.Info, s.Battery.StatusText));
                    _trayDevices.DeviceList.Items.Add(new TrayDeviceItem(s.Info), _config.IsTrayVisible(s.Info.Id));
                }
                int selectIndex = 0;
                for (int i = 0; i < _device.DeviceList.Items.Count; i++)
                {
                    DeviceItem item = _device.DeviceList.Items[i] as DeviceItem;
                    if (item != null && item.Device.Id == selected) { selectIndex = i; break; }
                }
                if (_device.DeviceList.Items.Count > 0) _device.DeviceList.SelectedIndex = selectIndex;
                UpdateDeviceLabels();
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
            _config.TrayVisibleIds = (checkedCount == _trayDevices.DeviceList.Items.Count || _trayDevices.DeviceList.Items.Count == 0)
                ? "" : string.Join(";", ids.ToArray());
            _config.Save();
            _app.RefreshTrayIcons();
        }

        private void OnDeviceSelected()
        {
            DeviceItem item = _device.DeviceList.SelectedItem as DeviceItem;
            if (item == null) return;
            _config.SelectedDeviceId = item.Device.Id;
            _config.Save();
            UpdateDeviceLabels();
            ReadDpi();
        }

        private void UpdateDeviceLabels()
        {
            DeviceItem item = _device.DeviceList.SelectedItem as DeviceItem;
            if (item == null)
            {
                _device.Model.Text = "未检测到设备";
                _device.Battery.Text = "电量 --";
                _device.Sensor.Text = "传感器 --";
                _device.Health.Text = "电池健康：硬件不支持 SoH / 循环次数";
                _dpi.Status.Text = "插入 ATK / 雷蛇 2.4G 接收器后重试。";
                UpdateCurrentDpiText("当前板载 DPI：--（未检测到设备）");
                return;
            }
            _device.Model.Text = TypeName(item.Device.Kind) + " · " + item.Device.ShortName + " · " + item.Device.VidPid;
            _device.Battery.Text = "电量 " + item.BatteryText + (item.Device.SupportsDpi ? "" : " · 不支持 DPI");
            _device.Sensor.Text = string.IsNullOrEmpty(item.Device.Sensor) ? "传感器 -- · " + item.Device.DpiNote : "传感器 " + item.Device.Sensor;
            DeviceSnapshot snap = FindSnapshot(item.Device.Id);
            _device.Health.Text = snap != null && !string.IsNullOrEmpty(snap.Battery.HealthText)
                ? "电池健康：" + snap.Battery.HealthText
                : "电池健康：硬件不支持 SoH / 循环次数";
            _dpi.Status.Text = item.Device.DpiNote;
        }

        private DeviceSnapshot FindSnapshot(string deviceId)
        {
            foreach (DeviceSnapshot s in _service.Snapshot())
                if (string.Equals(s.Info.Id, deviceId, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
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
                UiInvoke(delegate { SetStatus(ok ? "写入成功并已回读确认" : "写入失败：设备休眠 / 型号未识别 / 被占用"); });
            });
        }

        private void SaveProfile()
        {
            string name = _profiles.NameBox.Text.Trim();
            // 未填名称时用当前板载 DPI 命名，避免因空名无法保存。
            if (string.IsNullOrEmpty(name)) name = _dpi.X.Value + " DPI";
            MouseProfile p = _config.FindProfile(name);
            bool isNew = p == null;
            if (p == null) p = new MouseProfile();
            p.Name = name;
            p.DpiX = (int)_dpi.X.Value;
            p.DpiY = (int)_dpi.Y.Value;
            p.Stage = (int)_dpi.Stage.Value;
            DeviceInfo d = SelectedDevice();
            p.DeviceId = d == null ? "" : d.Id;
            if (isNew) _config.Profiles.Add(p);
            _config.ActiveProfile = p.Name;
            _config.Save();
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
            _config.ActiveProfile = name;
            _config.Save();
            ReloadProfiles();
            SetStatus("已重命名");
        }

        private void DeleteProfile()
        {
            MouseProfile p = _profiles.Profiles.SelectedItem as MouseProfile;
            if (p == null) { SetStatus("请先选择配置"); return; }
            _config.Profiles.Remove(p);
            _config.Save();
            ReloadProfiles();
            SetStatus("已删除配置");
        }

        private void AddProfile()
        {
            MouseProfile p = new MouseProfile();
            p.Name = "新配置 " + (_config.Profiles.Count + 1);
            p.DpiX = (int)_dpi.X.Value;
            p.DpiY = (int)_dpi.Y.Value;
            p.Stage = (int)_dpi.Stage.Value;
            _config.Profiles.Add(p);
            _config.Save();
            ReloadProfiles();
            _profiles.NameBox.Text = p.Name;
            SetStatus("已新建配置");
        }

        private void SaveSettings()
        {
            _config.DisplayMode = _appearance.IconMode.Checked ? 1 : 0;
            _config.RefreshSeconds = _appearance.Interval.SelectedIndex == 0 ? 15 : (_appearance.Interval.SelectedIndex == 1 ? 30 : 90);
            _config.LowThreshold = (int)_appearance.LowThreshold.Value;
            _app.SaveConfig();
            AppConfig.SetAutoStart(_appearance.AutoStart.Checked);
            SetStatus("设置已保存");
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
            public override string ToString() { return Device.ShortName + " · " + BatteryText; }
        }

        private sealed class TrayDeviceItem
        {
            public readonly DeviceInfo Device;
            public TrayDeviceItem(DeviceInfo device) { Device = device; }
            public override string ToString()
            {
                string type = Device.Kind == DeviceKind.Keyboard ? "键盘" : (Device.Kind == DeviceKind.Headset ? "耳机" : "鼠标");
                return "[" + type + "] " + Device.ShortName;
            }
        }
    }
}
