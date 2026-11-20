using System;
using System.Drawing;
using System.Windows.Forms;

namespace HyzMouseHub
{
    /// <summary>耳机音量：端点 + 滑杆。没有端点时只留一行说明。</summary>
    internal sealed class VolumeCard : Card
    {
        public readonly ComboBox EndpointList = new ComboBox();
        public readonly TrackBar Level = new TrackBar();
        public readonly Label Value = new Label();
        public readonly Label Empty = MutedLabel("未检测到耳机端点");

        public VolumeCard() : base("耳机音量", Theme.RowHeight + Theme.RowGap + 34)
        {
            EndpointList.DropDownStyle = ComboBoxStyle.DropDownList;
            Theme.StyleField(EndpointList);
            Content.Controls.Add(EndpointList);

            Level.Minimum = 0;
            Level.Maximum = 100;
            Level.TickStyle = TickStyle.None;
            Level.AutoSize = false;
            Level.Height = 24;
            Content.Controls.Add(Level);

            Value.Text = "--";
            Value.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
            Value.ForeColor = Theme.TextPrimary;
            Value.AutoSize = false;
            Value.TextAlign = ContentAlignment.MiddleRight;
            Content.Controls.Add(Value);

            Empty.TextAlign = ContentAlignment.MiddleLeft;
            Content.Controls.Add(Empty);
        }

        private bool _available = true;

        /// <summary>没有端点时只留一行说明，卡片跟着变矮，不留空框。</summary>
        protected override int ContentHeight
        {
            get { return _available ? base.ContentHeight : Theme.RowHeight; }
        }

        public void SetAvailable(bool available)
        {
            _available = available;
            EndpointList.Visible = Level.Visible = Value.Visible = available;
            Empty.Visible = !available;
            Height = NaturalHeight;
            PerformLayout();
        }

        protected override void LayoutContent()
        {
            int w = Content.Width;
            CenterIn(EndpointList, 0, Theme.RowHeight);
            EndpointList.SetBounds(0, EndpointList.Top, w, EndpointList.Height);

            int top = Theme.RowHeight + Theme.RowGap;
            const int valueWidth = 52;
            CenterIn(Level, top, 34);
            Level.SetBounds(0, Level.Top, Math.Max(80, w - valueWidth - 12), Level.Height);
            Value.SetBounds(w - valueWidth, top, valueWidth, 34);

            Empty.SetBounds(0, 0, w, Theme.RowHeight + Theme.RowGap + 8);
        }
    }
}
