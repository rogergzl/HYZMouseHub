using System;
using System.Drawing;
using System.Windows.Forms;

namespace HyzMouseHub
{
    /// <summary>外观与运行：显示方式、刷新与提醒口径、开机自启与提醒开关。</summary>
    internal sealed class AppearanceCard : Card
    {
        private const int Rows = 5;

        public readonly RadioButton NumberMode = new RadioButton();
        public readonly RadioButton IconMode = new RadioButton();
        public readonly ComboBox Interval = new ComboBox();
        public readonly NumericUpDown LowThreshold = new NumericUpDown();
        public readonly NumericUpDown SleepMinutes = new NumericUpDown();
        public readonly CheckBox AutoStart = new CheckBox();
        public readonly CheckBox ChargeFullAlert = new CheckBox();
        public readonly CheckBox DisconnectAlert = new CheckBox();
        public readonly Button Save = new Button();
        public readonly Button Close = new Button();

        private readonly Label _intervalKey = KeyLabel("刷新间隔");
        private readonly Label _thresholdKey = KeyLabel("低电量");
        private readonly Label _sleepKey = KeyLabel("休眠判定");
        private readonly Label _thresholdUnit = MutedLabel("%");
        private readonly Label _sleepUnit = MutedLabel("分钟");
        private readonly ToolTip _tips = new ToolTip();

        public AppearanceCard() : base("外观与运行",
            Theme.RowHeight * Rows + Theme.RowGap * (Rows - 1))
        {
            NumberMode.Text = "百分比数字";
            IconMode.Text = "设备图标进度";
            foreach (RadioButton radio in new[] { NumberMode, IconMode })
            {
                radio.AutoSize = false;
                radio.Font = Theme.Body;
                radio.ForeColor = Theme.TextPrimary;
                radio.TextAlign = ContentAlignment.MiddleLeft;
                Content.Controls.Add(radio);
            }

            Content.Controls.Add(_intervalKey);
            Interval.DropDownStyle = ComboBoxStyle.DropDownList;
            Interval.Items.AddRange(new object[] { "15 秒", "30 秒", "90 秒" });
            Interval.SelectedIndex = 0;
            Theme.StyleField(Interval);
            Content.Controls.Add(Interval);

            Content.Controls.Add(_thresholdKey);
            LowThreshold.Minimum = 0;
            LowThreshold.Maximum = 50;
            LowThreshold.Value = 15;
            LowThreshold.Height = Theme.RowHeight;
            Theme.StyleField(LowThreshold);
            Content.Controls.Add(LowThreshold);
            Content.Controls.Add(_thresholdUnit);

            Content.Controls.Add(_sleepKey);
            SleepMinutes.Minimum = AppConfig.SleepTimeoutMin;
            SleepMinutes.Maximum = AppConfig.SleepTimeoutMax;
            SleepMinutes.Increment = 5;
            SleepMinutes.Value = AppConfig.SleepTimeoutDefault;
            SleepMinutes.Height = Theme.RowHeight;
            Theme.StyleField(SleepMinutes);
            Content.Controls.Add(SleepMinutes);
            Content.Controls.Add(_sleepUnit);

            AutoStart.Text = "开机自动启动";
            ChargeFullAlert.Text = "充满提醒";
            DisconnectAlert.Text = "设备断开提醒";
            foreach (CheckBox box in new[] { AutoStart, ChargeFullAlert, DisconnectAlert })
            {
                box.AutoSize = false;
                box.Font = Theme.Body;
                box.ForeColor = Theme.TextPrimary;
                box.TextAlign = ContentAlignment.MiddleLeft;
                Content.Controls.Add(box);
            }

            Save.Text = "保存设置";
            Close.Text = "关闭";
            Theme.StyleButton(Save, true);
            Theme.StyleButton(Close, false);
            Content.Controls.Add(Save);
            Content.Controls.Add(Close);

            _tips.SetToolTip(SleepMinutes, "连续这么久读不到电量才判定为休眠");
            _tips.SetToolTip(LowThreshold, "低于该电量时提醒，单台设备可在托盘显示设备里单独设置");
            _tips.SetToolTip(AutoStart, "开机静默进托盘，手动双击仍会弹出窗口");
        }

        /// <summary>
        /// 休眠判定按分钟配置、按刷新周期生效。入参一律由调用方从中间层（或编辑中的控件值）传入，
        /// 卡片本身不缓存状态；折算结果只放进提示气泡，界面上不铺说明文字。
        /// </summary>
        public void RefreshSleepHint(int refreshSeconds, int sleepMinutes)
        {
            int gap = Math.Max(5, refreshSeconds);
            int cycles = Math.Max(1, (sleepMinutes * 60) / gap);
            _tips.SetToolTip(SleepMinutes, "约 " + cycles + " 个刷新周期无响应才判定为休眠");
        }

        protected override void LayoutContent()
        {
            int w = Content.Width;
            int row = Theme.RowHeight;

            // 第 1 行：显示方式
            CenterIn(NumberMode, 0, row);
            NumberMode.SetBounds(0, NumberMode.Top, 116, NumberMode.Height);
            CenterIn(IconMode, 0, row);
            IconMode.SetBounds(124, IconMode.Top, 140, IconMode.Height);

            // 第 2 行：刷新间隔 / 低电量
            int y = row + Theme.RowGap;
            int left = PlaceLabelled(_intervalKey, Interval, 0, y, 56, 88);
            int thresholdLeft = left + 20;
            int afterThreshold = PlaceLabelled(_thresholdKey, LowThreshold, thresholdLeft, y, 58, 56);
            _thresholdUnit.SetBounds(afterThreshold + 4, y, 16, row);

            // 第 3 行：休眠判定
            y += row + Theme.RowGap;
            int afterSleep = PlaceLabelled(_sleepKey, SleepMinutes, 0, y, 58, 56);
            _sleepUnit.SetBounds(afterSleep + 6, y, 40, row);

            // 第 4 行：三个开关
            y += row + Theme.RowGap;
            int third = Math.Max(88, w / 3);
            CenterIn(AutoStart, y, row);
            AutoStart.SetBounds(0, AutoStart.Top, third, AutoStart.Height);
            CenterIn(ChargeFullAlert, y, row);
            ChargeFullAlert.SetBounds(third, ChargeFullAlert.Top, Math.Min(104, third), ChargeFullAlert.Height);
            CenterIn(DisconnectAlert, y, row);
            DisconnectAlert.SetBounds(third + Math.Min(104, third), DisconnectAlert.Top,
                Math.Max(80, w - third - Math.Min(104, third)), DisconnectAlert.Height);

            // 第 5 行：动作按钮贴右
            y += row + Theme.RowGap;
            const int primaryWidth = 100;
            const int ghostWidth = 88;
            Save.SetBounds(w - primaryWidth, y, primaryWidth, row);
            Close.SetBounds(w - primaryWidth - 8 - ghostWidth, y, ghostWidth, row);
        }

        /// <summary>一行里放“标签 + 输入框”，返回该组右边缘，便于右侧继续排。</summary>
        private int PlaceLabelled(Label key, Control field, int left, int top, int keyWidth, int fieldWidth)
        {
            key.SetBounds(left, top, keyWidth, Theme.RowHeight);
            CenterIn(field, top, Theme.RowHeight);
            field.SetBounds(left + keyWidth + 6, field.Top, fieldWidth, field.Height);
            return left + keyWidth + 6 + fieldWidth;
        }
    }
}
