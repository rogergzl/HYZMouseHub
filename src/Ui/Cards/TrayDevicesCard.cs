using System;
using System.Drawing;
using System.Windows.Forms;

namespace HyzMouseHub
{
    /// <summary>托盘显示设备：勾选列表 + 单台设备的低电量阈值。</summary>
    internal sealed class TrayDevicesCard : Card
    {
        // 行高要足够放下勾选框：默认行高比勾选框矮一点，底部会被切掉一截。
        private const int ItemHeight = 24;
        private const int MinRows = 2;

        public readonly CheckedListBox DeviceList = new CheckedListBox();
        public readonly ComboBox ThresholdDevice = new ComboBox();
        public readonly NumericUpDown Threshold = new NumericUpDown();
        public readonly CheckBox UseGlobal = new CheckBox();
        private readonly Label _percent = MutedLabel("%");

        public TrayDevicesCard() : base("托盘显示设备", MinRows * ItemHeight + 8 + Theme.RowGap + Theme.FieldHeight)
        {
            DeviceList.BorderStyle = BorderStyle.FixedSingle;
            DeviceList.BackColor = Theme.FieldFill;
            DeviceList.CheckOnClick = true;
            DeviceList.Font = Theme.Body;
            DeviceList.IntegralHeight = false;
            DeviceList.ItemHeight = ItemHeight;
            Content.Controls.Add(DeviceList);

            // 设备名带品牌 + 型号 + 实例后缀，下拉展开宽度单独放宽。
            Theme.StyleCombo(ThresholdDevice);
            ThresholdDevice.DropDownWidth = 340;
            Content.Controls.Add(ThresholdDevice);

            Threshold.Minimum = 0;
            Threshold.Maximum = 100;
            Threshold.Height = Theme.FieldHeight;
            Theme.StyleField(Threshold);
            Content.Controls.Add(Threshold);

            Content.Controls.Add(_percent);

            UseGlobal.Text = "跟随全局";
            UseGlobal.AutoSize = false;
            UseGlobal.Font = Theme.Body;
            UseGlobal.ForeColor = Theme.TextSecondary;
            UseGlobal.TextAlign = ContentAlignment.MiddleLeft;
            Content.Controls.Add(UseGlobal);
        }

        /// <summary>列表高度跟着设备数走（2~4 行），设备少时不留一个空框。</summary>
        protected override int ContentHeight
        {
            get
            {
                int rows = Math.Max(MinRows, Math.Min(4, DeviceList.Items.Count));
                return rows * ItemHeight + 8 + Theme.RowGap + Theme.FieldHeight;
            }
        }

        protected override void LayoutContent()
        {
            int w = Content.Width;
            int listHeight = Math.Max(MinRows * ItemHeight + 8, Content.Height - Theme.RowGap - Theme.FieldHeight);
            DeviceList.SetBounds(0, 0, w, listHeight);

            int rowTop = listHeight + Theme.RowGap;
            const int globalWidth = 96;
            const int numericWidth = 56;
            const int percentWidth = 16;

            CenterIn(UseGlobal, rowTop, Theme.FieldHeight);
            UseGlobal.SetBounds(w - globalWidth, UseGlobal.Top, globalWidth, UseGlobal.Height);

            int percentLeft = UseGlobal.Left - 10 - percentWidth;
            _percent.SetBounds(percentLeft, rowTop, percentWidth, Theme.FieldHeight);

            int numericLeft = percentLeft - 6 - numericWidth;
            Threshold.SetBounds(numericLeft, rowTop, numericWidth, Theme.FieldHeight);

            int comboLeft = 0;
            int comboWidth = Math.Max(90, numericLeft - 10 - comboLeft);
            CenterIn(ThresholdDevice, rowTop, Theme.FieldHeight);
            ThresholdDevice.SetBounds(comboLeft, ThresholdDevice.Top, comboWidth, ThresholdDevice.Height);
        }

    }
}
