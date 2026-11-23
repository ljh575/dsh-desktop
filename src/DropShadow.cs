using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DshDesktop
{
    // Soft all-around drop shadow for a frameless window.
    //
    // Why a separate window: WinForms cannot paint over DWM glass, so the usual
    // DwmExtendFrameIntoClientArea recipe turns the frame area black or white instead
    // of a soft shadow (measured on Windows 10). A Region clip is 1-bit. The only
    // reliable way is a per-pixel-alpha layered window that sits directly beneath the
    // owner; this is identical on Windows 10 and 11 and never touches the WebView2
    // child (a layered *parent* would hide it).
    internal sealed class DropShadow : Form
    {
        private readonly Form _owner;
        private readonly int _margin;
        private readonly int _radius;
        private readonly byte _strength;
        private bool _attached;
        private bool _built;
        private int _lastW, _lastH;

        public DropShadow(Form owner, int radius, int margin, byte strength)
        {
            _owner = owner;
            _radius = Math.Max(8, radius);
            _margin = Math.Max(1, margin);
            _strength = strength;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000   // WS_EX_LAYERED
                            | 0x00000080   // WS_EX_TOOLWINDOW (no taskbar button)
                            | 0x08000000;  // WS_EX_NOACTIVATE (never takes focus)
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;

            _owner.Move += delegate { Reposition(); };
            _owner.Resize += delegate { Reposition(); };       // cheap: track the top-left
            _owner.ResizeEnd += delegate { Rebuild(); };        // bitmap once per drag-resize
            _owner.Activated += delegate { Reposition(); };
            _owner.VisibleChanged += delegate
            {
                bool hidden = !_owner.Visible || _owner.WindowState == FormWindowState.Minimized;
                try { if (hidden) Hide(); else { RebuildAndPosition(); Show(); } }
                catch { }
            };
        }

        // Call once the owner has a handle and its final size.
        public void ShowShadow()
        {
            if (!_attached) Attach();
            if (!_built || _owner.Width != _lastW || _owner.Height != _lastH)
            {
                _built = true;
                RebuildAndPosition();
            }
            else Reposition();
            Show();
        }

        private void RebuildAndPosition()
        {
            Reposition();
            Rebuild();
        }

        private void Rebuild()
        {
            int w = Math.Max(1, _owner.Width);
            int h = Math.Max(1, _owner.Height);
            _lastW = _owner.Width;
            _lastH = _owner.Height;
            int bw = w + _margin * 2;
            int bh = h + _margin * 2;

            using (Bitmap bmp = new Bitmap(bw, bh, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath path = Rounded(_margin, _margin, w, h, _radius))
                    using (SolidBrush brush = new SolidBrush(Color.Black))
                        g.FillPath(brush, path);
                }
                BlurAlpha(bmp, Math.Max(1, _margin / 2));
                ApplyAlpha(bmp);
            }
        }

        private static GraphicsPath Rounded(int x, int y, int w, int h, int r)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Min(r * 2, Math.Min(w, h));
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Two-pass box blur over the alpha channel only: opaque at the window
        // silhouette, fading to transparent at the margin edge.
        private static void BlurAlpha(Bitmap bmp, int radius)
        {
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int w = data.Width, h = data.Height, stride = data.Stride;
                byte[] buf = new byte[stride * h];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                byte[] tmp = new byte[buf.Length];
                int rad = Math.Max(1, radius);

                for (int y = 0; y < h; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int sum = 0, cnt = 0;
                        for (int k = -rad; k <= rad; k++)
                        {
                            int xx = x + k;
                            if (xx < 0 || xx >= w) continue;
                            sum += buf[row + xx * 4 + 3];
                            cnt++;
                        }
                        tmp[row + x * 4 + 3] = (byte)(sum / cnt);
                    }
                }
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        int sum = 0, cnt = 0;
                        for (int k = -rad; k <= rad; k++)
                        {
                            int yy = y + k;
                            if (yy < 0 || yy >= h) continue;
                            sum += tmp[yy * stride + x * 4 + 3];
                            cnt++;
                        }
                        buf[y * stride + x * 4 + 3] = (byte)(sum / cnt);
                    }
                }
                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            }
            finally { bmp.UnlockBits(data); }
        }

        private void ApplyAlpha(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(memDc, hBitmap);
            try
            {
                SIZE size = new SIZE(bmp.Width, bmp.Height);
                POINT src = new POINT();
                POINT dst = new POINT { X = _owner.Left - _margin, Y = _owner.Top - _margin };
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = 0;                       // AC_SRC_OVER
                blend.SourceConstantAlpha = _strength;
                blend.AlphaFormat = 1;                   // AC_SRC_ALPHA
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src,
                    0, ref blend, 2 /* ULW_ALPHA */);
            }
            finally
            {
                SelectObject(memDc, old);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private void Reposition()
        {
            if (!_attached || _owner.WindowState == FormWindowState.Minimized || !_owner.Visible) return;
            int x = _owner.Left - _margin;
            int y = _owner.Top - _margin;
            // Move only; Rebuild() (UpdateLayeredWindow) is the authority on size, so the
            // expensive bitmap is not redone while the window is being drag-resized.
            SetWindowPos(Handle, _owner.Handle, x, y, 0, 0, 0x0001 | 0x0010 /* SWP_NOSIZE | SWP_NOACTIVATE */);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int Cx, Cy; public SIZE(int cx, int cy) { Cx = cx; Cy = cy; } }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
