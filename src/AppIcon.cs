using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace HyzMouseHub
{
    /// <summary>
    /// 程序图标：半透明线条风格的鼠标。
    /// 在 32×32 逻辑坐标里画轮廓，再按目标尺寸缩放，一次性打包成多尺寸 .ico，
    /// 这样任务栏、标题栏、资源管理器、开始菜单都能取到原生尺寸，不会糊。
    /// </summary>
    internal static class AppIcon
    {
        private static readonly int[] IconSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

        // 主体轮廓：上深下浅的蓝色渐变，整体半透明，避免死黑在深色任务栏上糊成一团。
        // 小尺寸会把 alpha 抬高一点，否则 16px 缩下去就只剩一层灰。
        private static readonly Color StrokeTop = Color.FromArgb(0x4A, 0x9B, 0xDC);
        private static readonly Color StrokeBottom = Color.FromArgb(0x15, 0x55, 0x8E);
        private static readonly Color FillColor = Color.FromArgb(0x2E, 0x7C, 0xBE);
        private static readonly Color DetailColor = Color.FromArgb(0x18, 0x5E, 0x9C);

        private static byte[] _bytes;
        private static Icon _shared;
        private static Icon _small;

        /// <summary>多尺寸图标，用于窗口标题栏与任务栏。</summary>
        public static Icon Shared
        {
            get
            {
                if (_shared == null)
                {
                    using (MemoryStream ms = new MemoryStream(Bytes))
                    using (Icon tmp = new Icon(ms))
                    {
                        // Clone 之后不再依赖流，避免流被回收后图标失效。
                        _shared = (Icon)tmp.Clone();
                    }
                }
                return _shared;
            }
        }

        /// <summary>16×16 的版本，用于托盘占位图标。</summary>
        public static Icon Small
        {
            get
            {
                if (_small == null)
                {
                    using (MemoryStream ms = new MemoryStream(Bytes))
                    using (Icon tmp = new Icon(ms, 16, 16))
                    {
                        _small = (Icon)tmp.Clone();
                    }
                }
                return _small;
            }
        }

        public static byte[] Bytes
        {
            get
            {
                if (_bytes == null) _bytes = BuildIco();
                return _bytes;
            }
        }

        public static void SaveIco(string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, Bytes);
        }

        /// <summary>按目标像素尺寸绘制图标，返回 32bpp 带 alpha 的位图。</summary>
        public static Bitmap Render(int size)
        {
            if (size < 8) size = 8;
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);

                float scale = size / 32f;
                g.ScaleTransform(scale, scale);

                // 小尺寸补偿：16px 下 0.78px 的线会灰掉，抬到约 1.15px 才不虚。
                float pen = Math.Max(1.55f, 1.15f / scale);

                // 16~24px 时不画中缝（会和滚轮糊成一坨），滚轮也收小一点。
                bool compact = size <= 24;
                Color strokeTop = Color.FromArgb(compact ? 230 : 200, StrokeTop);
                Color strokeBottom = Color.FromArgb(compact ? 230 : 200, StrokeBottom);

                RectangleF body = new RectangleF(7.2f, 2.8f, 17.6f, 26.4f);

                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(body);

                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(compact ? 34 : 30, FillColor)))
                        g.FillPath(fill, path);

                    using (LinearGradientBrush gradient = new LinearGradientBrush(
                        new PointF(16f, 2f), new PointF(16f, 30f), strokeTop, strokeBottom))
                    using (Pen outline = new Pen(gradient, pen))
                    {
                        outline.LineJoin = LineJoin.Round;
                        outline.StartCap = LineCap.Round;
                        outline.EndCap = LineCap.Round;
                        g.DrawPath(outline, path);
                    }
                }

                // 上盖中缝：从顶边落到滚轮位置，表示左右键分界。
                if (!compact)
                {
                    using (Pen seam = new Pen(Color.FromArgb(160, DetailColor), Math.Max(1.2f, pen * 0.8f)))
                    {
                        seam.StartCap = LineCap.Round;
                        seam.EndCap = LineCap.Round;
                        g.DrawLine(seam, 16f, 3.6f, 16f, 9.1f);
                    }
                }

                // 滚轮：小尺寸直接填实（描边会糊），大尺寸画成空心胶囊更精致。
                RectangleF wheelRect = compact
                    ? new RectangleF(14.95f, 9.8f, 2.1f, 4.6f)
                    : new RectangleF(14.6f, 9.4f, 2.8f, 4.6f);
                using (GraphicsPath wheel = RoundedRect(wheelRect, wheelRect.Width / 2f))
                {
                    if (compact)
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(205, 0x14, 0x50, 0x86)))
                            g.FillPath(b, wheel);
                    }
                    else
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(64, 0x1E, 0x6C, 0xAA)))
                            g.FillPath(b, wheel);
                        using (Pen p = new Pen(Color.FromArgb(180, 0x14, 0x50, 0x86), Math.Max(1.1f, pen * 0.66f)))
                            g.DrawPath(p, wheel);
                    }
                }
            }
            return bmp;
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static byte[] BuildIco()
        {
            List<byte[]> images = new List<byte[]>();
            List<int> dims = new List<int>();
            foreach (int size in IconSizes)
            {
                using (Bitmap bmp = Render(size))
                {
                    // 大尺寸用 PNG 压缩，小尺寸用传统 DIB，兼容性最好、体积也小。
                    images.Add(size >= 128 ? PngBytes(bmp) : DibBytes(bmp));
                    dims.Add(size);
                }
            }

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write((short)0);          // reserved
                bw.Write((short)1);          // type = icon
                bw.Write((short)images.Count);

                int offset = 6 + 16 * images.Count;
                for (int i = 0; i < images.Count; i++)
                {
                    bw.Write((byte)(dims[i] >= 256 ? 0 : dims[i]));
                    bw.Write((byte)(dims[i] >= 256 ? 0 : dims[i]));
                    bw.Write((byte)0);       // color count
                    bw.Write((byte)0);       // reserved
                    bw.Write((short)1);      // planes
                    bw.Write((short)32);     // bpp
                    bw.Write(images[i].Length);
                    bw.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) bw.Write(img);
                bw.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] PngBytes(Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private static byte[] DibBytes(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] pixels = new byte[data.Stride * h];
            try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
            finally { bmp.UnlockBits(data); }

            int maskStride = ((w + 31) / 32) * 4;

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(40);                // biSize
                bw.Write(w);                 // biWidth
                bw.Write(h * 2);             // biHeight = XOR + AND
                bw.Write((short)1);          // biPlanes
                bw.Write((short)32);         // biBitCount
                bw.Write(0);                 // biCompression = BI_RGB
                bw.Write(w * h * 4);         // biSizeImage
                bw.Write(0);                 // biXPelsPerMeter
                bw.Write(0);                 // biYPelsPerMeter
                bw.Write(0);                 // biClrUsed
                bw.Write(0);                 // biClrImportant

                for (int y = h - 1; y >= 0; y--)
                    bw.Write(pixels, y * data.Stride, w * 4);

                bw.Write(new byte[maskStride * h]); // AND 掩码全 0，靠 alpha 通道透明
                bw.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>诊断用：把图标在浅色 / 深色背景和放大倍率下的效果画到一张 PNG，便于人眼验收。</summary>
        public static void SavePreview(string path)
        {
            int[] strip = { 16, 20, 24, 32, 48, 64, 128 };
            using (Bitmap canvas = new Bitmap(940, 470, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.FromArgb(0xF7, 0xF9, 0xFB));
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(0x1B, 0x1B, 0x1B)))
                    g.FillRectangle(dark, 0, 160, 940, 150);
                using (SolidBrush mid = new SolidBrush(Color.FromArgb(0x2B, 0x35, 0x40)))
                    g.FillRectangle(mid, 0, 310, 940, 160);

                using (Font f = new Font("Segoe UI", 8.5f))
                using (SolidBrush light = new SolidBrush(Color.FromArgb(120, 120, 120)))
                using (SolidBrush lightOnDark = new SolidBrush(Color.FromArgb(150, 160, 170)))
                {
                    int x = 26;
                    foreach (int s in strip)
                    {
                        using (Bitmap b = Render(s))
                        {
                            g.DrawImageUnscaled(b, x, 80 - s / 2);
                            g.DrawImageUnscaled(b, x, 235 - s / 2);
                        }
                        g.DrawString(s.ToString(), f, light, x, 130 - 12);
                        g.DrawString(s.ToString(), f, lightOnDark, x, 290 - 12);
                        x += s + 22;
                    }

                    // 底部：16px / 32px 放大 8 倍，检查线条是否干净、有没有越界。
                    g.DrawString("16px ×8（深底）", f, lightOnDark, 30, 322);
                    g.DrawString("32px ×4（深底）", f, lightOnDark, 220, 322);
                    g.DrawString("128px（原始）", f, lightOnDark, 460, 322);

                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    using (Bitmap s16 = Render(16))
                        g.DrawImage(s16, new Rectangle(30, 344, 128, 128));
                    using (Bitmap s32 = Render(32))
                        g.DrawImage(s32, new Rectangle(220, 344, 128, 128));
                    using (Bitmap s128 = Render(128))
                        g.DrawImageUnscaled(s128, 460, 330);
                }

                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                canvas.Save(path, ImageFormat.Png);
            }
        }
    }
}
