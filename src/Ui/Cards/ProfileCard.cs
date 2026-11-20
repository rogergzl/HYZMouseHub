using System;
using System.Drawing;
using System.Windows.Forms;

namespace HyzMouseHub
{
    /// <summary>配置档位：当前板载 DPI + 配置列表 + 一条命名/操作行。</summary>
    internal sealed class ProfileCard : Card
    {
        private const int ListHeight = 118;
        private const int EmptyListHeight = 46;
        private const int HeadlineRow = 26;

        public readonly ListBox Profiles = new ListBox();
        public readonly TextBox NameBox = new TextBox();
        public readonly Label CurrentDpi = new Label();
        public readonly Button Save = new Button();
        public readonly Button Apply = new Button();
        public readonly Button Rename = new Button();
        public readonly Button Delete = new Button();
        public readonly Button Add = new Button();
        private readonly Label _empty = MutedLabel("还没有保存的配置");

        public ProfileCard() : base("配置档位", ContentFor(EmptyListHeight))
        {
            CurrentDpi.Text = "当前板载 DPI：--";
            CurrentDpi.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            CurrentDpi.ForeColor = Theme.TextPrimary;
            CurrentDpi.AutoSize = false;
            CurrentDpi.TextAlign = ContentAlignment.MiddleLeft;
            Content.Controls.Add(CurrentDpi);

            Profiles.BorderStyle = BorderStyle.FixedSingle;
            Profiles.BackColor = Theme.FieldFill;
            Profiles.Font = Theme.Body;
            Profiles.IntegralHeight = false;
            Content.Controls.Add(Profiles);

            _empty.TextAlign = ContentAlignment.MiddleLeft;
            Content.Controls.Add(_empty);

            Theme.StyleField(NameBox);
            NameBox.BorderStyle = BorderStyle.FixedSingle;
            Content.Controls.Add(NameBox);

            Save.Text = "保存为配置";
            Apply.Text = "应用";
            Rename.Text = "重命名";
            Delete.Text = "删除";
            Add.Text = "新建";
            Theme.StyleButton(Save, true);
            Theme.StyleButton(Apply, false);
            Theme.StyleButton(Rename, false);
            Theme.StyleButton(Delete, false);
            Theme.StyleButton(Add, false);
            Content.Controls.Add(Save);
            Content.Controls.Add(Apply);
            Content.Controls.Add(Rename);
            Content.Controls.Add(Delete);
            Content.Controls.Add(Add);

            SetEmptyState(true);
        }

        private bool _isEmpty = true;

        private static int ContentFor(int listHeight)
        {
            return HeadlineRow + Theme.RowGap + listHeight + Theme.RowGap
                   + Theme.RowHeight + Theme.RowGap + Theme.RowHeight;
        }

        /// <summary>没有配置时列表位收窄成一行说明，卡片不留空框。</summary>
        protected override int ContentHeight
        {
            get { return ContentFor(_isEmpty ? EmptyListHeight : ListHeight); }
        }

        /// <summary>没有已保存配置时用一行说明替代空白列表。</summary>
        public void SetEmptyState(bool empty)
        {
            _isEmpty = empty;
            Profiles.Visible = !empty;
            _empty.Visible = empty;
            Apply.Enabled = !empty;
            Rename.Enabled = !empty;
            Delete.Enabled = !empty;
        }

        protected override void LayoutContent()
        {
            int w = Content.Width;
            CurrentDpi.SetBounds(0, 0, w, HeadlineRow);

            int listTop = HeadlineRow + Theme.RowGap;
            // 没有配置时说明行只占一行高度，多出来的空间交给行间距，不把空框撑大。
            int listHeight = _isEmpty
                ? EmptyListHeight
                : Math.Max(46, Content.Height - ContentFor(0));
            Profiles.SetBounds(0, listTop, w, listHeight);
            _empty.SetBounds(0, listTop, w, listHeight);

            int nameTop = listTop + listHeight + Theme.RowGap;
            const int saveWidth = 104;
            NameBox.SetBounds(0, nameTop + Math.Max(0, (Theme.RowHeight - NameBox.Height) / 2),
                Math.Max(80, w - saveWidth - 8), NameBox.Height);
            Save.SetBounds(w - saveWidth, nameTop, saveWidth, Theme.RowHeight);

            int actionTop = nameTop + Theme.RowHeight + Theme.RowGap;
            const int gap = 8;
            int actionWidth = Math.Max(64, (w - gap * 3) / 4);
            Apply.SetBounds(0, actionTop, actionWidth, Theme.RowHeight);
            Rename.SetBounds(actionWidth + gap, actionTop, actionWidth, Theme.RowHeight);
            Delete.SetBounds((actionWidth + gap) * 2, actionTop, actionWidth, Theme.RowHeight);
            Add.SetBounds((actionWidth + gap) * 3, actionTop, w - (actionWidth + gap) * 3, Theme.RowHeight);
        }

    }
}
