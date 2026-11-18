using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace HyzMouseHub
{
    /// <summary>所有卡片的基类：统一白底、圆角观感、标题栏，尺寸完全由内容决定。</summary>
    internal abstract class Card : UserControl
    {
        private readonly Label _title;
        protected readonly Panel Content;
        /// <summary>按内容计算出的自然高度。布局时可以在此基础上拉伸，用于左右两列底部对齐。</summary>
        public readonly int NaturalHeight;

        protected Card(string title, int contentHeight)
        {
            BackColor = Color.White;
            BorderStyle = BorderStyle.FixedSingle;
            Padding = new Padding(0);
            AutoScaleMode = AutoScaleMode.Dpi;

            _title = new Label();
            _title.Text = title;
            _title.Dock = DockStyle.Top;
            _title.Height = 34;
            _title.Padding = new Padding(14, 9, 0, 0);
            _title.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            _title.ForeColor = Color.FromArgb(0x1F, 0x6F, 0xB2);
            Controls.Add(_title);

            // Content 不定 Dock，由 OnResize 跟着卡片高度走；卡片被拉伸时多余的空白
            // 出现在内容底部，而不是标题与内容之间。
            Content = new Panel();
            Content.Location = new Point(0, 34);
            Content.Size = new Size(Width, contentHeight);
            Content.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Content.Padding = new Padding(14, 2, 14, 12);
            Controls.Add(Content);

            NaturalHeight = 34 + contentHeight;
            Height = NaturalHeight;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Content != null && Content.Height != Math.Max(0, Height - 34))
                Content.Size = new Size(Width, Math.Max(0, Height - 34));
        }

        /// <summary>卡片内部统一的输入/标签布局位置。</summary>
        protected static Label FieldLabel(string text, int x, int y, int w)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 22),
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(90, 90, 90)
            };
        }
    }

    internal sealed class DeviceCard : Card
    {
        public readonly ComboBox DeviceList = new ComboBox();
        public readonly Label Model = new Label();
        public readonly Label Battery = new Label();
        public readonly Label Sensor = new Label();
        public readonly Label Health = new Label();

        public DeviceCard() : base("设备状态", 148)
        {
            DeviceList.DropDownStyle = ComboBoxStyle.DropDownList;
            DeviceList.Location = new Point(14, 8);
            DeviceList.Size = new Size(Content.ClientSize.Width - 28, 26);
            DeviceList.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(DeviceList);

            AddInfo(Model, "未检测到设备", 44, false);
            AddInfo(Battery, "电量 --", 70, false);
            AddInfo(Sensor, "传感器 --", 96, false);
            AddInfo(Health, "电池健康：硬件不支持 SoH / 循环次数", 120, true);
        }

        private void AddInfo(Label label, string text, int y, bool small)
        {
            label.Text = text;
            label.Location = new Point(14, y);
            label.Size = new Size(Content.ClientSize.Width - 28, small ? 24 : 22);
            label.Font = new Font("Segoe UI", small ? 8.5F : 9.5F);
            label.ForeColor = small ? Color.FromArgb(120, 120, 120) : Color.FromArgb(60, 60, 60);
            Content.Controls.Add(label);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // 构造期基类可能先改尺寸，此时子控件还没创建，必须保护。
            if (DeviceList == null || Model == null || Battery == null || Sensor == null || Health == null) return;
            int w = Math.Max(120, Content.ClientSize.Width - 28);
            DeviceList.Width = w;
            Model.Width = Battery.Width = Sensor.Width = Health.Width = w;
        }
    }

    internal sealed class TrayDevicesCard : Card
    {
        public readonly CheckedListBox DeviceList = new CheckedListBox();
        private readonly Label _hint = new Label();

        public TrayDevicesCard() : base("托盘显示设备", 138)
        {
            DeviceList.Location = new Point(14, 8);
            DeviceList.Size = new Size(Content.ClientSize.Width - 28, 76);
            DeviceList.BorderStyle = BorderStyle.FixedSingle;
            DeviceList.CheckOnClick = true;
            DeviceList.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(DeviceList);

            _hint.Text = "勾选即显示独立图标 · 全选=显示所有设备（含新插入）\r\n拖动托盘图标可调整左右顺序；Win11 可能折叠到 ^ 中。";
            _hint.Location = new Point(14, 88);
            _hint.Size = new Size(Content.ClientSize.Width - 28, 42);
            _hint.Font = new Font("Segoe UI", 8.5F);
            _hint.ForeColor = Color.FromArgb(125, 125, 125);
            Content.Controls.Add(_hint);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (DeviceList == null || _hint == null) return;
            int w = Math.Max(120, Content.ClientSize.Width - 28);
            DeviceList.Width = w;
            _hint.Width = w;
        }
    }

    internal sealed class DpiCard : Card
    {
        public readonly NumericUpDown X = new NumericUpDown();
        public readonly NumericUpDown Y = new NumericUpDown();
        public readonly NumericUpDown Stage = new NumericUpDown();
        public readonly Button Read = new Button();
        public readonly Button Write = new Button();
        public readonly Button Rescan = new Button();
        public readonly Label Status = new Label();

        public DpiCard() : base("板载 DPI", 142)
        {
            AddField("X", X, 14, 8, 110);
            AddField("Y", Y, 158, 8, 110);
            AddField("档位", Stage, 310, 8, 62);

            Read.Text = "读取硬件 DPI";
            Write.Text = "写入板载 DPI";
            Rescan.Text = "重新扫描";
            ButtonAt(Read, 14, 46, 150);
            ButtonAt(Write, 172, 46, 150);
            ButtonAt(Rescan, 330, 46, 114);

            Status.Text = "选择设备后读取。写入仅作用于已识别型号。";
            Status.Location = new Point(14, 90);
            Status.Size = new Size(Content.ClientSize.Width - 28, 40);
            Status.Font = new Font("Segoe UI", 8.5F);
            Status.ForeColor = Color.FromArgb(95, 95, 95);
            Content.Controls.Add(Status);
        }

        private void AddField(string text, NumericUpDown box, int x, int y, int width)
        {
            bool stage = text == "档位";
            // “档位”是两个汉字，24px 宽会换行被裁；给足 40px 并让输入框顺延。
            Content.Controls.Add(FieldLabel(text, x, y + 2, stage ? 40 : 24));
            box.Minimum = stage ? 1 : 50;
            box.Maximum = stage ? 8 : 42000;
            box.Increment = stage ? 1 : 10;
            box.Value = stage ? 1 : 1600;
            box.Location = new Point(x + (stage ? 44 : 22), y);
            box.Size = new Size(width, 24);
            box.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(box);
        }

        private void ButtonAt(Button b, int x, int y, int w)
        {
            b.Location = new Point(x, y);
            b.Size = new Size(w, 32);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(0xC8, 0xD4, 0xDE);
            b.BackColor = Color.White;
            b.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(b);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Status == null || Content == null) return;
            Status.Width = Math.Max(120, Content.ClientSize.Width - 28);
        }
    }

    internal sealed class ProfileCard : Card
    {
        public readonly ListBox Profiles = new ListBox();
        public readonly TextBox NameBox = new TextBox();
        public readonly Label CurrentDpi = new Label();
        public readonly Button Save = new Button();
        public readonly Button Apply = new Button();
        public readonly Button Rename = new Button();
        public readonly Button Delete = new Button();
        public readonly Button Add = new Button();
        private readonly Label _emptyHint = new Label();
        private readonly Label _tip = new Label();

        public ProfileCard() : base("配置档位", 236)
        {
            // 顶部直接显示设备当前上报的板载 DPI，不展示任何占位档位。
            CurrentDpi.Text = "当前板载 DPI：--（选择设备后读取）";
            CurrentDpi.Location = new Point(14, 6);
            CurrentDpi.Size = new Size(Content.ClientSize.Width - 28, 22);
            CurrentDpi.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            CurrentDpi.ForeColor = Color.FromArgb(0x1F, 0x6F, 0xB2);
            Content.Controls.Add(CurrentDpi);

            Profiles.Location = new Point(14, 32);
            Profiles.Size = new Size(Content.ClientSize.Width - 28, 78);
            Profiles.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(Profiles);

            _emptyHint.Text = "还没有保存的配置。\r\n读取到板载 DPI 后，在下方填名称并点“保存当前为配置”。";
            _emptyHint.Location = new Point(14, 32);
            _emptyHint.Size = new Size(Content.ClientSize.Width - 28, 78);
            _emptyHint.TextAlign = ContentAlignment.MiddleLeft;
            _emptyHint.Font = new Font("Segoe UI", 8.5F);
            _emptyHint.ForeColor = Color.FromArgb(135, 135, 135);
            Content.Controls.Add(_emptyHint);

            NameBox.Location = new Point(14, 118);
            NameBox.Size = new Size(180, 26);
            NameBox.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(NameBox);

            ButtonAt(Save, "保存当前为配置", 200, 116, 132);
            ButtonAt(Apply, "应用选中", 340, 116, 118);
            ButtonAt(Rename, "重命名", 14, 152, 108);
            ButtonAt(Delete, "删除", 130, 152, 108);
            ButtonAt(Add, "新建空白", 246, 152, 122);

            _tip.Text = "保存后可在托盘右键里直接切换。";
            _tip.Location = new Point(14, 190);
            _tip.Size = new Size(Content.ClientSize.Width - 28, 30);
            _tip.Font = new Font("Segoe UI", 8.5F);
            _tip.ForeColor = Color.FromArgb(135, 135, 135);
            Content.Controls.Add(_tip);

            SetEmptyState(true);
        }

        /// <summary>没有已保存配置时用提示文案替代空白列表。</summary>
        public void SetEmptyState(bool empty)
        {
            Profiles.Visible = !empty;
            _emptyHint.Visible = empty;
            Apply.Enabled = !empty;
            Rename.Enabled = !empty;
            Delete.Enabled = !empty;
        }

        private void ButtonAt(Button b, string text, int x, int y, int w)
        {
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(0xC8, 0xD4, 0xDE);
            b.BackColor = Color.White;
            b.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(b);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Profiles == null || Apply == null || Save == null || Content == null) return;
            int w = Math.Max(120, Content.ClientSize.Width - 28);
            Profiles.Width = w;
            CurrentDpi.Width = w;
            _emptyHint.Width = w;
            if (_tip != null) _tip.Width = w;
            Apply.Left = Content.ClientSize.Width - 14 - Apply.Width;
            Save.Left = Apply.Left - Save.Width - 8;
        }
    }

    internal sealed class AppearanceCard : Card
    {
        public readonly RadioButton NumberMode = new RadioButton();
        public readonly RadioButton IconMode = new RadioButton();
        public readonly ComboBox Interval = new ComboBox();
        public readonly NumericUpDown LowThreshold = new NumericUpDown();
        public readonly NumericUpDown SleepMinutes = new NumericUpDown();
        public readonly CheckBox AutoStart = new CheckBox();
        public readonly Button Save = new Button();
        public readonly Button Close = new Button();
        private readonly Label _autoHint = new Label();
        private readonly Label _sleepHint = new Label();
        private readonly Label _badge = new Label();

        public AppearanceCard() : base("外观与运行", 250)
        {
            NumberMode.Text = "百分比数字";
            IconMode.Text = "设备图标进度";
            NumberMode.Location = new Point(14, 8);
            IconMode.Location = new Point(150, 8);
            NumberMode.Size = new Size(130, 24);
            IconMode.Size = new Size(160, 24);
            NumberMode.Font = IconMode.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(NumberMode);
            Content.Controls.Add(IconMode);

            Content.Controls.Add(FieldLabel("刷新间隔", 14, 42, 76));
            Interval.DropDownStyle = ComboBoxStyle.DropDownList;
            Interval.Items.AddRange(new object[] { "15 秒", "30 秒", "90 秒" });
            Interval.Location = new Point(92, 38);
            Interval.Size = new Size(120, 26);
            Interval.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(Interval);

            Content.Controls.Add(FieldLabel("低电量提醒", 226, 42, 92));
            LowThreshold.Minimum = 0;
            LowThreshold.Maximum = 50;
            LowThreshold.Value = 15;
            LowThreshold.Location = new Point(322, 38);
            LowThreshold.Size = new Size(72, 26);
            LowThreshold.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(LowThreshold);

            Content.Controls.Add(FieldLabel("休眠判定", 14, 76, 76));
            SleepMinutes.Minimum = AppConfig.SleepTimeoutMin;
            SleepMinutes.Maximum = AppConfig.SleepTimeoutMax;
            SleepMinutes.Increment = 5;
            SleepMinutes.Value = AppConfig.SleepTimeoutDefault;
            SleepMinutes.Location = new Point(92, 72);
            SleepMinutes.Size = new Size(72, 26);
            SleepMinutes.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(SleepMinutes);

            _sleepHint.Text = "分钟 · 无响应按刷新周期累计，超时才判休眠";
            _sleepHint.Location = new Point(172, 76);
            _sleepHint.Size = new Size(Content.ClientSize.Width - 186, 22);
            _sleepHint.Font = new Font("Segoe UI", 8.5F);
            _sleepHint.ForeColor = Color.FromArgb(125, 125, 125);
            Content.Controls.Add(_sleepHint);

            AutoStart.Text = "开机自动启动";
            AutoStart.Location = new Point(14, 110);
            AutoStart.Size = new Size(160, 24);
            AutoStart.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(AutoStart);

            _autoHint.Text = "仅开机自启时静默进托盘，手动打开仍弹出窗口";
            _autoHint.Location = new Point(178, 113);
            _autoHint.Size = new Size(Content.ClientSize.Width - 192, 22);
            _autoHint.Font = new Font("Segoe UI", 8.5F);
            _autoHint.ForeColor = Color.FromArgb(125, 125, 125);
            Content.Controls.Add(_autoHint);

            ButtonAt(Save, "保存设置", 14, 142, 120);
            ButtonAt(Close, "关闭窗口", 142, 142, 120);

            _badge.Text = "电量配色：红 ≤15% · 黄 ≤40% · 绿 >40%\r\n" +
                          "数字模式看图标底部色条，图标模式看自下而上的填充\r\n" +
                          "缓存读数（设备未应答时）整体压淡，超出休眠判定才隐藏电量";
            _badge.Location = new Point(14, 186);
            _badge.Size = new Size(Content.ClientSize.Width - 28, 64);
            _badge.Font = new Font("Segoe UI", 8.5F);
            _badge.ForeColor = Color.FromArgb(110, 110, 110);
            Content.Controls.Add(_badge);
        }

        private void ButtonAt(Button b, string text, int x, int y, int w)
        {
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(w, 32);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(0xC8, 0xD4, 0xDE);
            b.BackColor = Color.White;
            b.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(b);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Content == null || _autoHint == null || _badge == null || _sleepHint == null) return;
            int w = Math.Max(120, Content.ClientSize.Width - 28);
            _autoHint.Width = Math.Max(80, Content.ClientSize.Width - _autoHint.Left - 14);
            _sleepHint.Width = Math.Max(80, Content.ClientSize.Width - _sleepHint.Left - 14);
            _badge.Width = w;
        }
    }

    internal sealed class VolumeCard : Card
    {
        public readonly ComboBox EndpointList = new ComboBox();
        public readonly TrackBar Level = new TrackBar();
        public readonly Label Value = new Label();
        public readonly Label Empty = new Label();

        public VolumeCard() : base("EPOS 音量（Windows 音频端点）", 112)
        {
            EndpointList.DropDownStyle = ComboBoxStyle.DropDownList;
            EndpointList.Location = new Point(14, 8);
            EndpointList.Size = new Size(Content.ClientSize.Width - 28, 26);
            EndpointList.Font = new Font("Segoe UI", 9.5F);
            Content.Controls.Add(EndpointList);

            Level.Minimum = 0;
            Level.Maximum = 100;
            Level.Location = new Point(14, 42);
            Level.Size = new Size(Content.ClientSize.Width - 100, 40);
            Level.TickFrequency = 10;
            Content.Controls.Add(Level);

            Value.Text = "--%";
            Value.Location = new Point(Content.ClientSize.Width - 74, 48);
            Value.Size = new Size(60, 26);
            Value.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            Content.Controls.Add(Value);

            Empty.Text = "未检测到 EPOS / GSA 70 播放端点。音量走 Windows 标准音频接口，不是耳机私有 HID 协议。";
            Empty.Location = new Point(14, 42);
            Empty.Size = new Size(Content.ClientSize.Width - 28, 56);
            Empty.Font = new Font("Segoe UI", 8.5F);
            Empty.ForeColor = Color.FromArgb(120, 120, 120);
            Content.Controls.Add(Empty);
        }

        public void SetAvailable(bool available)
        {
            EndpointList.Visible = Level.Visible = Value.Visible = available;
            Empty.Visible = !available;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Content == null || EndpointList == null || Level == null || Value == null || Empty == null) return;
            int w = Math.Max(120, Content.ClientSize.Width - 28);
            EndpointList.Width = w;
            Level.Width = Math.Max(80, w - 86);
            Value.Left = Content.ClientSize.Width - 14 - Value.Width;
            Empty.Width = w;
        }
    }

    internal sealed class SafetyCard : Card
    {
        public SafetyCard() : base("安全说明 / 电池健康", 100)
        {
            Label text = new Label();
            text.Text =
                "• 电池健康 (SoH / 循环次数)：所列设备 HID 均不上报，不显示伪数值。\n" +
                "• 写入 DPI 后都会回读确认，型号未识别时只读不写。\n" +
                "• 设备休眠时写入会失败，先动一下设备再试。\n" +
                "• 配置保存在 %AppData%\\HYZMouseHub。";
            text.Location = new Point(14, 8);
            text.Size = new Size(Content.ClientSize.Width - 28, 88);
            text.Font = new Font("Segoe UI", 8.5F);
            text.ForeColor = Color.FromArgb(85, 85, 85);
            Content.Controls.Add(text);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Content == null) return;
            foreach (Control c in Content.Controls) c.Width = Math.Max(120, Content.ClientSize.Width - 28);
        }
    }
}
