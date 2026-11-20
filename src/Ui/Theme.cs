using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HyzMouseHub
{
    /// <summary>
    /// 设计令牌：颜色、间距、字体、控件高度只在这里定义一次。
    /// 卡片和控件不再各写一套硬编码数值，改风格只改这个文件。
    /// 间距一律取 4 的倍数，行内控件统一高度，避免出现同一行一边高一边低。
    /// </summary>
    internal static class Theme
    {
        // 页面底色：中性浅灰，卡片浮在上面才有层次。
        public static readonly Color Back = Color.FromArgb(0xF1, 0xF3, 0xF7);
        public static readonly Color Header = Color.FromArgb(0xFF, 0xFF, 0xFF);

        public static readonly Color Surface = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static readonly Color SurfaceBottom = Color.FromArgb(0xFB, 0xFC, 0xFE);
        public static readonly Color Border = Color.FromArgb(0xE2, 0xE7, 0xEE);
        // 按钮 / 输入框描边比卡片描边稍深一点，才看得出是能点的控件。
        public static readonly Color BorderStrong = Color.FromArgb(0xD3, 0xDA, 0xE4);
        // 卡片描边：一圈很细的近黑线，让悬浮卡片有明确边界。
        public static readonly Color BorderInk = Color.FromArgb(0x24, 0x29, 0x30);
        public static readonly Color Hairline = Color.FromArgb(0xEB, 0xEF, 0xF4);
        public static readonly Color Shadow = Color.FromArgb(0x11, 0x18, 0x24);

        public static readonly Color TextPrimary = Color.FromArgb(0x15, 0x1A, 0x20);
        public static readonly Color TextSecondary = Color.FromArgb(0x5D, 0x66, 0x72);
        public static readonly Color TextMuted = Color.FromArgb(0x8C, 0x96, 0xA3);

        public static readonly Color Accent = Color.FromArgb(0x2F, 0x6B, 0xE6);
        public static readonly Color AccentHover = Color.FromArgb(0x27, 0x5B, 0xC8);
        public static readonly Color AccentSoft = Color.FromArgb(0xEC, 0xF2, 0xFE);
        public static readonly Color Danger = Color.FromArgb(0xD9, 0x3B, 0x3B);

        // 顶栏按钮底色：比白底深一档，才看得出是控件而不是标题的一部分。
        public static readonly Color HeaderButton = Color.FromArgb(0xEC, 0xF0, 0xF7);
        public static readonly Color FieldFill = Color.FromArgb(0xF7, 0xF9, 0xFC);
        public static readonly Color FieldHover = Color.FromArgb(0xF4, 0xF6, 0xFA);
        public static readonly Color FieldDown = Color.FromArgb(0xE8, 0xEC, 0xF3);

        // 度量：所有间距 4 的倍数。
        public const int Gutter = 14;        // 两列之间
        public const int Gap = 10;           // 卡片之间
        public const int Pad = 18;           // 卡片内边距
        public const int Radius = 14;        // 卡片圆角
        public const int FieldRadius = 8;    // 输入控件圆角
        public const int HeaderHeight = 64;
        public const int StatusHeight = 30;
        public const int RowHeight = 28;     // 行内控件统一高度（按钮）
        public const int FieldHeight = 26;   // 输入控件统一高度（下拉 / 数字 / 文本）
        public const int RowGap = 10;
        public const int TitleHeight = 24;
        public const int TitleGap = 12;

        public static readonly Font Title = new Font("Segoe UI Semibold", 18F, FontStyle.Bold);
        public static readonly Font CardTitle = new Font("Segoe UI", 10F, FontStyle.Bold);
        public static readonly Font Body = new Font("Segoe UI", 9.75F);
        public static readonly Font BodyStrong = new Font("Segoe UI Semibold", 9.75F);
        public static readonly Font Small = new Font("Segoe UI", 9F);
        public static readonly Font Mono = new Font("Consolas", 9F);

        /// <summary>卡片内的固定高度：标题行 + 上下内边距，内容高度在此基础上叠加。</summary>
        public const int CardChrome = Pad + TitleHeight + TitleGap + Pad;

        public static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
            Rectangle arc = new Rectangle(rect.X, rect.Y, d, d);
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - d;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - d;
            path.AddArc(arc, 0, 90);
            arc.X = rect.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>按钮统一外观：主按钮实心、次按钮描边，都带 hover / 按下反馈。</summary>
        public static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Border;
            button.BackColor = primary ? Accent : Surface;
            button.ForeColor = primary ? Color.White : TextPrimary;
            button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : FieldHover;
            button.FlatAppearance.MouseDownBackColor = primary ? AccentHover : FieldDown;
            button.Font = primary ? BodyStrong : Body;
            button.Cursor = Cursors.Hand;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Height = RowHeight;
        }

        /// <summary>
        /// 顶栏动作按钮：做成带底色的次级按钮，和白底顶栏拉开一层，看着就知道能点。
        /// 比卡片里的描边按钮更轻，不抢标题。
        /// </summary>
        public static void StyleHeaderButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = BorderStrong;
            button.BackColor = HeaderButton;
            button.ForeColor = TextPrimary;
            button.FlatAppearance.MouseOverBackColor = FieldDown;
            button.FlatAppearance.MouseDownBackColor = AccentSoft;
            button.Font = Body;
            button.Cursor = Cursors.Hand;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Height = FieldHeight;
        }

        /// <summary>下拉框 / 数字框共用的视觉参数。</summary>
        public static void StyleField(Control field)
        {
            field.Font = Body;
            field.ForeColor = TextPrimary;
        }

        /// <summary>
        /// 下拉框：系统样式会给一个固定矮高度，和旁边的数字框、按钮对不齐，
        /// 底边还常被吃掉一截。改成自绘固定行高 + 自己画一圈描边，
        /// 高度和边框都由我们说了算，视觉上和数字框、按钮同一层次。
        /// </summary>
        public static void StyleCombo(ComboBox combo)
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Standard;
            combo.BackColor = FieldFill;
            combo.ForeColor = TextPrimary;
            combo.Font = Body;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = FieldHeight - 6;
            combo.DrawItem += DrawComboRow;
            combo.Paint += DrawComboFrame;
        }

        /// <summary>FlatStyle 拉平后系统不再画外框，只在悬停时浮一层；这里补一圈实描边。</summary>
        private static void DrawComboFrame(object sender, PaintEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null) return;
            Color ink = combo.Focused ? Accent : BorderStrong;
            using (Pen pen = new Pen(ink))
                e.Graphics.DrawRectangle(pen, 0, 0, combo.Width - 1, combo.Height - 1);
        }

        private static void DrawComboRow(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = sender as ComboBox;
            if (combo == null) return;

            bool closed = (e.State & DrawItemState.ComboBoxEdit) == DrawItemState.ComboBoxEdit;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = closed ? FieldFill : (selected ? AccentSoft : Surface);
            using (SolidBrush fill = new SolidBrush(back)) e.Graphics.FillRectangle(fill, e.Bounds);

            string text = e.Index >= 0 && e.Index < combo.Items.Count
                ? combo.GetItemText(combo.Items[e.Index])
                : combo.Text;
            Rectangle box = new Rectangle(e.Bounds.X + 7, e.Bounds.Y,
                Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, combo.Font, box, TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
