using System;
using System.Drawing;
using System.Windows.Forms;

namespace HyzMouseHub
{
    /// <summary>读取诊断：失败记录 + 打开配置目录。只在弹窗里出现，不占主界面版面。</summary>
    internal sealed class DiagnosticsCard : Card
    {
        public readonly TextBox Errors = new TextBox();
        public readonly Button OpenLog = new Button();

        public DiagnosticsCard() : base("读取诊断", 220 + Theme.RowGap + Theme.RowHeight)
        {
            Errors.Multiline = true;
            Errors.ReadOnly = true;
            Errors.BorderStyle = BorderStyle.None;
            Errors.ScrollBars = ScrollBars.Vertical;
            Errors.BackColor = Theme.Surface;
            Errors.ForeColor = Theme.TextSecondary;
            Errors.Font = Theme.Mono;
            Errors.Text = "暂无读取失败记录。";
            Content.Controls.Add(Errors);

            OpenLog.Text = "打开配置目录";
            Theme.StyleButton(OpenLog, false);
            Content.Controls.Add(OpenLog);
        }

        protected override void LayoutContent()
        {
            int w = Content.Width;
            int height = Math.Max(60, Content.Height - Theme.RowGap - Theme.RowHeight);
            Errors.SetBounds(0, 0, w, height);
            OpenLog.SetBounds(0, height + Theme.RowGap, 132, Theme.RowHeight);
        }
    }
}
