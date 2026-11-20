using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace HyzMouseHub
{
    /// <summary>
    /// 关于窗口：版本信息 + 教程。主界面为了干净把说明文字全撤了，
    /// 配色口径、休眠与缓存、提醒、托盘、DPI、电池限制这些集中放这里，
    /// 第一次用的人看完这一页就知道每个数字是怎么来的。
    /// </summary>
    internal sealed class AboutForm : Form
    {
        private static readonly string[][] Sections =
        {
            new[]
            {
                "电量怎么看",
                "红色 ≤15%、黄色 ≤40%、绿色 >40%。数字样式看图标底部那条色条，图标样式看自下而上的填充；" +
                "充电时数字旁带黄色闪电。数字与设备轮廓都会跟随任务栏深浅色自动换色，切换主题立刻重绘。"
            },
            new[]
            {
                "灰色 -- 与红色 --",
                "灰色 -- 表示设备休眠，接口暂时读不到；红色 -- 带红点表示接口读取失败，原因可在右上角「读取诊断」里看。" +
                "两者不一样：前者动一下设备就好，后者通常是原厂软件占着接口。"
            },
            new[]
            {
                "休眠与缓存",
                "无线设备闲置几秒就会休眠。这段时间沿用上一次的电量，托盘图标整体压淡、菜单标注「休眠 · 缓存」；" +
                "连续无响应超过「休眠判定」时长（默认 5 分钟）才真正显示休眠并隐藏电量。判定时长按分钟配置，" +
                "程序内部折算成连续多少个刷新周期没响应，改刷新间隔不会改变这个时长。"
            },
            new[]
            {
                "提醒",
                "低电量阈值默认 15%，可在「外观与运行」里改全局值，也可以在「托盘显示设备」里给某台设备单独设，" +
                "单独设过就不再跟随全局。另有「充满提醒」（充电后停止充电且电量已满时提示）与「设备断开提醒」" +
                "（设备从列表消失时提示），可各自开关。"
            },
            new[]
            {
                "托盘",
                "只有「托盘显示设备」里勾选的设备才占任务栏位置；全部勾选等于显示所有设备，新插入的设备也会出现。" +
                "按住左键左右拖动图标可以调整顺序。Windows 11 默认把多个托盘图标收进 ^，展开小箭头可见，" +
                "也可以拖到任务栏上常驻。"
            },
            new[]
            {
                "板载 DPI 与配置",
                "「板载 DPI」读的是鼠标硬件里存的档位，不是系统指针速度；写入后会立刻回读确认，回读不一致即算失败。" +
                "常用档位可以「保存为配置」，之后一键应用。设备休眠、型号未识别或被原厂软件占用时只读不写，" +
                "避免写坏设备里的配置。\r\n读到的档位会记下来，跨重启保留。会去读它的时机：程序启动 / 自启动" +
                "（头 1 分钟不受刷新间隔限制、每 5 秒试一次，读到值或满 1 分钟为止）、手动点「读取硬件 DPI」、" +
                "手动写入板载 DPI，以及之后每隔「刷新间隔」的自动重读——所以在别的软件里换了档位，下一轮就能跟上。" +
                "设备休眠只是这一次没应答，界面上继续显示上次读到的档位，不会变成未知；等它应答了下一轮再读。"
            },
            new[]
            {
                "电池读数的限制",
                "外设上报的是分级电量（常见 5% 或 10% 一档），不是精确百分比，短时间内跳一档属正常。" +
                "容量衰减、循环次数这类数据厂商不对外提供，程序不会编造一个健康百分比出来。"
            },
            new[]
            {
                "配置与卸载",
                "%AppData%\\HYZMouseHub 下是 settings.ini（全部设置）与 error.log（异常记录）。" +
                "写配置先写临时文件再替换，断电不会留下半截文件；删掉整个目录等于恢复出厂。" +
                "卸载：取消「开机自动启动」，退出程序，删除 exe 与该目录。"
            },
            new[]
            {
                "单实例与命令行",
                "重复双击不会再起第二个托盘，程序会把已开着的窗口叫到前面。日常用不到命令行开关，" +
                "排障时可用 --list、--test、--watch、--probe=VID:PID 等，完整列表见 --help 与 MANUAL.md。"
            }
        };

        private readonly FlowLayoutPanel _body = new FlowLayoutPanel();
        private readonly List<Control> _blocks = new List<Control>();
        private int _builtWidth = -1;

        public AboutForm()
        {
            Text = "关于 HYZ Mouse Hub";
            Icon = AppIcon.Shared;
            ClientSize = new Size(620, 640);
            MinimumSize = new Size(520, 420);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.Back;
            Font = Theme.Body;
            DoubleBuffered = true;

            Controls.Add(BuildBody());
            Controls.Add(BuildFooter());
            Controls.Add(BuildHeader());

            Controls.SetChildIndex(Controls[0], 0);
            Controls.SetChildIndex(Controls[1], 1);
            Controls.SetChildIndex(Controls[2], 2);

            Shown += delegate { BuildContent(); };
            _body.Resize += delegate
            {
                int width = ContentWidth();
                if (width > 0 && width != _builtWidth) BuildContent();
            };
        }

        private Control BuildHeader()
        {
            Panel header = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = Theme.Header };

            header.Controls.Add(new Label
            {
                Text = "HYZ Mouse Hub",
                Font = Theme.Title,
                ForeColor = Theme.TextPrimary,
                AutoSize = false,
                Location = new Point(Theme.Pad + 4, 16),
                Size = new Size(420, 32),
                TextAlign = ContentAlignment.MiddleLeft
            });

            header.Controls.Add(new Label
            {
                Text = "v" + typeof(Program).Assembly.GetName().Version.ToString(3)
                       + "　　单文件绿色版 · 无遥测 · 不联网　　github.com/rogergzl/HYZMouseHub",
                Font = Theme.Small,
                ForeColor = Theme.TextMuted,
                AutoSize = false,
                Location = new Point(Theme.Pad + 4, 50),
                Size = new Size(560, 20),
                TextAlign = ContentAlignment.MiddleLeft
            });

            header.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Hairline });
            return header;
        }

        private Control BuildBody()
        {
            _body.Dock = DockStyle.Fill;
            _body.BackColor = Theme.Back;
            _body.AutoScroll = true;
            // 用流式布局而不是绝对坐标：面板滚动时 WinForms 会平移子控件，
            // 若再拿控件 Top 去推算下一段的位置，算出来的 y 会越来越小，段落就叠到标题上。
            _body.FlowDirection = FlowDirection.TopDown;
            _body.WrapContents = false;
            _body.Padding = new Padding(Theme.Pad + 6, Theme.Pad + 2, Theme.Pad + 6, Theme.Pad);
            return _body;
        }

        private Control BuildFooter()
        {
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Theme.Header };

            LinkLabel link = new LinkLabel
            {
                Text = "查看使用手册与更新",
                Font = Theme.Body,
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.AccentHover,
                AutoSize = false,
                Location = new Point(Theme.Pad + 4, 0),
                Size = new Size(200, 56),
                TextAlign = ContentAlignment.MiddleLeft
            };
            link.LinkClicked += delegate
            {
                try { System.Diagnostics.Process.Start("https://github.com/rogergzl/HYZMouseHub"); }
                catch { }
            };
            footer.Controls.Add(link);

            Button close = new Button { Text = "关闭", DialogResult = DialogResult.OK };
            Theme.StyleButton(close, true);
            close.Size = new Size(96, Theme.RowHeight);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            footer.Controls.Add(close);
            footer.Resize += delegate
            {
                close.Location = new Point(footer.ClientSize.Width - Theme.Pad - close.Width,
                    (footer.Height - close.Height) / 2);
            };

            footer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Hairline });
            AcceptButton = close;
            CancelButton = close;
            return footer;
        }

        private int ContentWidth()
        {
            // 内容一定超过一屏，滚动条常驻，所以宽度里固定扣掉滚动条宽度，
            // 面板的 ClientSize 不会因为滚动条出现而变化，也就不会来回重建。
            int width = _body.ClientSize.Width - _body.Padding.Horizontal
                        - SystemInformation.VerticalScrollBarWidth;
            return Math.Max(200, width);
        }

        private void BuildContent()
        {
            int width = ContentWidth();
            if (width <= 0 || width == _builtWidth) return;
            _builtWidth = width;

            _body.SuspendLayout();
            try
            {
                _body.AutoScrollPosition = new Point(0, 0);
                _body.AutoScrollMinSize = Size.Empty;

                foreach (Control block in _blocks) block.Dispose();
                _blocks.Clear();

                foreach (string[] section in Sections)
                {
                    Label title = new Label
                    {
                        Text = section[0],
                        Font = Theme.BodyStrong,
                        ForeColor = Theme.TextPrimary,
                        AutoSize = true,
                        MaximumSize = new Size(width, 0),
                        Location = new Point(0, 0)
                    };
                    Label text = new Label
                    {
                        Text = section[1],
                        Font = Theme.Body,
                        ForeColor = Theme.TextSecondary,
                        AutoSize = true,
                        MaximumSize = new Size(width, 0),
                        Location = new Point(0, title.PreferredHeight + 4)
                    };

                    // 每段装进一个固定高度的容器，由流式布局依次排下去。
                    // 高度只由两个标签的 PreferredHeight 决定，与滚动位置无关。
                    Panel block = new Panel
                    {
                        Width = width,
                        Height = title.PreferredHeight + 4 + text.PreferredHeight,
                        Margin = new Padding(0, 0, 0, 18),
                        BackColor = Theme.Back
                    };
                    block.Controls.Add(title);
                    block.Controls.Add(text);

                    _body.Controls.Add(block);
                    _blocks.Add(block);
                }

                // 流式布局自己算的滚动范围会漏掉最后一段的下边距，滚到底仍被裁掉一行。
                // 注意别在 SuspendLayout 期间读控件的 Bottom——那时子控件还没排布，
                // 读到的是默认值；这里按我们自己设的高度累加，才是真实内容高度。
                int stack = 0;
                foreach (Control block in _body.Controls) stack += block.Height + block.Margin.Bottom;
                _body.AutoScrollMinSize = new Size(0, stack + _body.Padding.Vertical);
            }
            finally
            {
                _body.ResumeLayout();
            }
        }

        /// <summary>诊断用：滚到底，检查段落有没有被滚动位置带歪。</summary>
        internal void ScrollBodyToEnd()
        {
            _body.AutoScrollPosition = new Point(0, Math.Max(1, _body.VerticalScroll.Maximum));
            _body.PerformLayout();
            Application.DoEvents();
        }

        /// <summary>诊断用：按顺序打印每段的位置，相邻两段有重叠即说明排版算错了。</summary>
        internal string DumpBlocks()
        {
            StringBuilder sb = new StringBuilder();
            int previousBottom = int.MinValue;
            foreach (Control c in _body.Controls)
            {
                string flag = previousBottom > c.Top ? "  <== 重叠" : "";
                sb.AppendLine("block top=" + c.Top + " bottom=" + c.Bottom + " h=" + c.Height + flag);
                previousBottom = c.Bottom;
            }
            return sb.ToString();
        }
    }
}
