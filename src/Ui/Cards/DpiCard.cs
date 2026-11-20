using System;
using System.Drawing;
using System.Windows.Forms;

namespace HyzMouseHub
{
    /// <summary>板载 DPI：X / Y / 档位 + 读、写、重扫三个动作。</summary>
    internal sealed class DpiCard : Card
    {
        public readonly NumericUpDown X = new NumericUpDown();
        public readonly NumericUpDown Y = new NumericUpDown();
        public readonly NumericUpDown Stage = new NumericUpDown();
        public readonly Button Read = new Button();
        public readonly Button Write = new Button();
        public readonly Button Rescan = new Button();

        private readonly Label _xKey = KeyLabel("X");
        private readonly Label _yKey = KeyLabel("Y");
        private readonly Label _stageKey = KeyLabel("档位");

        public DpiCard() : base("板载 DPI", Theme.FieldHeight + Theme.RowGap + Theme.RowHeight)
        {
            AddField(_xKey, X, false);
            AddField(_yKey, Y, false);
            AddField(_stageKey, Stage, true);

            Read.Text = "读取硬件 DPI";
            Write.Text = "写入板载 DPI";
            Rescan.Text = "重新扫描";
            Theme.StyleButton(Read, true);
            Theme.StyleButton(Write, false);
            Theme.StyleButton(Rescan, false);
            Content.Controls.Add(Read);
            Content.Controls.Add(Write);
            Content.Controls.Add(Rescan);
        }

        private void AddField(Label key, NumericUpDown box, bool stage)
        {
            box.Minimum = stage ? 1 : 50;
            box.Maximum = stage ? 8 : 42000;
            box.Increment = stage ? 1 : 10;
            box.Value = stage ? 1 : 1600;
            box.Height = Theme.FieldHeight;
            Theme.StyleField(box);
            Content.Controls.Add(key);
            Content.Controls.Add(box);
        }

        protected override void LayoutContent()
        {
            int w = Content.Width;

            // 三个输入组等宽，右边的空余平均分给它们，避免挤在左侧。
            int groupWidth = Math.Max(96, (w - 12 * 2) / 3);
            int x = 0;
            PlaceField(_xKey, X, x, groupWidth);
            x += groupWidth + 12;
            PlaceField(_yKey, Y, x, groupWidth);
            x += groupWidth + 12;
            PlaceField(_stageKey, Stage, x, groupWidth);

            int buttonTop = Theme.FieldHeight + Theme.RowGap;
            int buttonWidth = Math.Max(96, (w - 8 * 2) / 3);
            Read.SetBounds(0, buttonTop, buttonWidth, Theme.RowHeight);
            Write.SetBounds(buttonWidth + 8, buttonTop, buttonWidth, Theme.RowHeight);
            Rescan.SetBounds((buttonWidth + 8) * 2, buttonTop, w - (buttonWidth + 8) * 2, Theme.RowHeight);
        }

        private void PlaceField(Label key, NumericUpDown box, int left, int width)
        {
            int keyWidth = key.Text == "档位" ? 30 : 14;
            key.SetBounds(left, 0, keyWidth, Theme.FieldHeight);
            box.SetBounds(left + keyWidth + 4, 0, Math.Max(40, width - keyWidth - 4), Theme.FieldHeight);
        }
    }
}
