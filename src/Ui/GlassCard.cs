using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HyzMouseHub
{
    /// <summary>
    /// 所有卡片的基类：圆角玻璃感面板 + 统一标题行 + 内容区内缩。
    /// 内容区（Content）四边都留出 Pad，圆角不会被内容方角顶破。
    /// 卡片高度默认由内容决定；列高不齐时由 <see cref="AbsorbSlack"/> 把余量交给可伸缩的卡片。
    /// </summary>
    internal abstract class Card : UserControl
    {
        private readonly string _title;
        protected readonly Panel Content;
        private readonly int _baseContentHeight;
        /// <summary>内容高度：默认由构造参数决定，子类可覆写（例如没有内容时卡片自动变矮）。</summary>
        protected virtual int ContentHeight { get { return _baseContentHeight; } }
        /// <summary>内容决定的自然高度；布局时按它复位，再按需吸收列间高度差。</summary>
        public int NaturalHeight { get { return Theme.CardChrome + ContentHeight; } }

        protected Card(string title, int contentHeight)
        {
            _title = title;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
            Margin = new Padding(0, 0, 0, Theme.Gap);

            Content = new Panel();
            Content.BackColor = Theme.Surface;
            Controls.Add(Content);

            _baseContentHeight = contentHeight;
            Height = NaturalHeight;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle body = new Rectangle(0, 0, Math.Max(2, Width - 1), Math.Max(2, Height - 3));
            using (GraphicsPath shadowPath = Theme.Rounded(new Rectangle(0, 2, body.Width, body.Height), Theme.Radius))
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(16, Theme.Shadow)))
                g.FillPath(shadow, shadowPath);

            using (GraphicsPath path = Theme.Rounded(body, Theme.Radius))
            {
                using (LinearGradientBrush fill = new LinearGradientBrush(body, Theme.Surface, Theme.SurfaceBottom, 90f))
                    g.FillPath(fill, path);
                using (Pen border = new Pen(Theme.BorderInk, 1f))
                    g.DrawPath(border, path);
            }

            TextRenderer.DrawText(g, _title, Theme.CardTitle,
                new Rectangle(Theme.Pad, Theme.Pad - 2, body.Width - Theme.Pad * 2, Theme.TitleHeight),
                Theme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Content == null) return;
            int width = Math.Max(60, Width - Theme.Pad * 2);
            int height = Math.Max(20, Height - Theme.CardChrome);
            Content.SetBounds(Theme.Pad, Theme.Pad + Theme.TitleHeight + Theme.TitleGap, width, height);
            LayoutContent();
            SpreadRows();
        }

        /// <summary>
        /// 卡片被拉到和同一行的邻居一样高时，内容区会多出一截。把这一截平均分到内容的
        /// 行间隙上，内容顶上贴齐标题、底下贴齐卡片，不在底部积一块空白。
        /// LayoutContent 每次从绝对坐标重排，重复调用不会累积偏移。
        /// </summary>
        private void SpreadRows()
        {
            List<int> tops = new List<int>();
            int used = 0;
            foreach (Control c in Content.Controls)
            {
                if (!c.Visible) continue;
                tops.Add(c.Top);
                if (c.Bottom > used) used = c.Bottom;
            }
            if (tops.Count == 0) return;

            // 同一行的控件顶端常差一两像素（输入框在行内居中），先按 8px 容差归成一行，
            // 否则它们会各自按不同系数下移，越撑越歪。
            tops.Sort();
            List<int> rows = new List<int>();
            foreach (int top in tops)
                if (rows.Count == 0 || top - rows[rows.Count - 1] > 8) rows.Add(top);

            int extra = Content.Height - used;
            if (rows.Count < 2 || extra < 6) return;

            int step = extra / (rows.Count - 1);
            if (step <= 0) return;

            foreach (Control c in Content.Controls)
            {
                if (!c.Visible) continue;
                int index = 0;
                for (int i = 0; i < rows.Count; i++) if (c.Top >= rows[i]) index = i;
                if (index > 0) c.Top += index * step;
            }
        }

        /// <summary>子类在这里按 Content 的实际宽高摆放控件，坐标从内容区左上角算起。</summary>
        protected virtual void LayoutContent()
        {
        }

        protected static Label KeyLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = Theme.Small,
                ForeColor = Theme.TextMuted,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        protected static Label ValueLabel()
        {
            return new Label
            {
                Font = Theme.Body,
                ForeColor = Theme.TextPrimary,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        protected static Label MutedLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = Theme.Small,
                ForeColor = Theme.TextMuted,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        /// <summary>输入控件在固定行高里垂直居中，抵消 ComboBox 不能设高度的差异。</summary>
        protected static void CenterIn(Control child, int top, int rowHeight)
        {
            int h = child.Height;
            child.Top = top + Math.Max(0, (rowHeight - h) / 2);
        }
    }
}
