// Shared look for everything the launcher draws itself: colour tokens derived
// from the page, the title-bar buttons, the settings controls, and the context
// menu. Kept in one place so the window, the settings form and the menus can
// never drift apart.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshDesktop
{
    internal sealed class Theme
    {
        public bool Dark;
        public Color Surface;       // cards, dialog background
        public Color Elevated;      // inputs, popups
        public Color Border;
        public Color Text;
        public Color TextMuted;
        public Color Accent;
        public Color AccentText;
        public Color Hover;

        // Derived from the colour the page is actually using, so a themed dialog
        // does not clash with the window it opens over. The dialog has to read as
        // a separate surface floating above the page, so it is a clear step
        // lighter than the page and keeps a visible border.
        public static Theme FromPage(bool dark, Color page)
        {
            Theme t = new Theme();
            t.Dark = dark;
            if (dark)
            {
                t.Surface = App.Shift(page, 18);
                t.Elevated = App.Shift(page, 26);
                t.Border = App.Shift(page, 78);
                t.Text = Color.FromArgb(238, 241, 245);
                t.TextMuted = Color.FromArgb(158, 165, 176);
                t.Accent = Color.FromArgb(76, 125, 255);
                t.AccentText = Color.White;
                t.Hover = App.Shift(page, 34);
            }
            else
            {
                t.Surface = Color.FromArgb(255, 255, 255);
                t.Elevated = Color.FromArgb(255, 255, 255);
                t.Border = Color.FromArgb(196, 203, 213);
                t.Text = Color.FromArgb(27, 29, 34);
                t.TextMuted = Color.FromArgb(107, 114, 128);
                t.Accent = Color.FromArgb(41, 98, 255);
                t.AccentText = Color.White;
                t.Hover = Color.FromArgb(243, 245, 248);
            }
            return t;
        }

        // Slightly separated bands at the top and bottom of a dialog, the way
        // DSH's panels carry a header and a footer strip.
        // Popups sit one step away from the panel they open over, so the list reads as
        // its own layer. Equal channels: a neutral grey never picks up the tint of the
        // page behind it.
        public Color MenuSurface
        {
            get
            {
                if (!Dark) return Color.FromArgb(240, 240, 240);
                int level = Math.Max(0, Math.Min(255, (Surface.R + Surface.G + Surface.B) / 3 + 15));
                return Color.FromArgb(level, level, level);
            }
        }

        public Color Header
        {
            get { return Dark ? App.Shift(Surface, -7) : Color.FromArgb(245, 247, 250); }
        }

        public Color Footer
        {
            get { return Dark ? App.Shift(Surface, -4) : Color.FromArgb(247, 249, 251); }
        }

        // Square top, rounded bottom: a popup that hangs off a control reads as
        // attached at the top.
        public static GraphicsPath BottomRounded(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int r = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
            path.AddLine(bounds.Left, bounds.Top, bounds.Right, bounds.Top);
            path.AddLine(bounds.Right, bounds.Top, bounds.Right, bounds.Bottom - r);
            path.AddArc(bounds.Right - r * 2, bounds.Bottom - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - r * 2, r * 2, r * 2, 90, 90);
            path.AddLine(bounds.Left, bounds.Bottom - r, bounds.Left, bounds.Top);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }
            int d = radius * 2;
            d = Math.Min(d, Math.Min(bounds.Width, bounds.Height));
            Rectangle arc = new Rectangle(bounds.X, bounds.Y, d, d);
            path.AddArc(arc, 180, 90);

            arc.X = bounds.Right - d - 1;
            path.AddArc(arc, 270, 90);

            arc.Y = bounds.Bottom - d - 1;
            path.AddArc(arc, 0, 90);

            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        // A window Region has to cover the final pixel row and column. Rounded() keeps
        // its geometry one pixel inside the bounds so a 1px stroke stays inside, which
        // is right for a painted panel but leaves that last row and column of a Region
        // unpainted - the desktop behind then shows through as a one-pixel line.
        public static GraphicsPath RoundedExact(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }
            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            Rectangle arc = new Rectangle(bounds.X, bounds.Y, d, d);
            path.AddArc(arc, 180, 90);

            arc.X = bounds.Right - d;
            path.AddArc(arc, 270, 90);

            arc.Y = bounds.Bottom - d;
            path.AddArc(arc, 0, 90);

            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void FillRounded(Graphics g, Rectangle bounds, int radius, Color color)
        {
            using (GraphicsPath path = Rounded(bounds, radius))
            using (SolidBrush brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public void DrawRoundedBorder(Graphics g, Rectangle bounds, int radius, Color color)
        {
            using (GraphicsPath path = Rounded(bounds, radius))
            using (Pen pen = new Pen(color))
                g.DrawPath(pen, path);
        }
    }

    internal enum ChromeKind { Settings, Menu, Minimize, Maximize, Restore, Close }

    // One drawn button of the immersive title bar. Owner drawing keeps the glyphs
    // crisp at any DPI and lets a theme change repaint them without recreating
    // handles, which would disturb the WebView2 host.
    // A select that belongs to the same family as the rest of the panel: a rounded
    // cell with a chevron, opening a themed menu. WinForms' ComboBox always draws
    // its own square frame, which is what made the form look pieced together.
    internal sealed class SelectCell : Control
    {
        private readonly Theme _theme;
        private readonly int _radius;
        private readonly float _scale;
        private string[] _items = new string[0];
        private int _index;
        private bool _hover;
        private bool _open;

        public SelectCell(Theme theme, float scale)
        {
            _theme = theme;
            _scale = scale <= 0 ? 1f : scale;
            _radius = (int)Math.Round(8 * _scale);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Font = new Font("Microsoft YaHei UI", 9f);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public event EventHandler SelectionChanged;

        private static ContextMenuStrip _active;

        // The strip is a separate window, so scrolling the page behind it does not
        // dismiss it on its own. The dialog message filter calls this.
        public static void CloseActiveMenu()
        {
            ContextMenuStrip menu = _active;
            if (menu != null && menu.Visible) menu.Close();
        }

        public string[] Items
        {
            get { return _items; }
            set { _items = value ?? new string[0]; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                int clamped = Math.Max(0, Math.Min(Math.Max(0, _items.Length - 1), value));
                if (clamped == _index) return;
                _index = clamped;
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        public string SelectedItem
        {
            get { return _items.Length == 0 ? "" : _items[SelectedIndex]; }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Dropdown();
        }

        private void Dropdown()
        {
            if (_items.Length == 0) return;
            RoundedMenu menu = new RoundedMenu(Math.Max(4, (int)Math.Round(6 * _scale)));
            menu.Renderer = new MenuRenderer(_theme, _scale);
            menu.Font = new Font("Microsoft YaHei UI", 9f);
            menu.Padding = new Padding((int)Math.Round(6 * _scale));
            menu.BackColor = _theme.MenuSurface;
            // The native check margin paints a small square, so the tick is drawn
            // by the renderer at the right edge of the row instead.
            menu.ShowCheckMargin = false;
            menu.ShowImageMargin = false;
            int widest = 0;
            foreach (string label in _items)
                widest = Math.Max(widest, TextRenderer.MeasureText(label, menu.Font).Width);
            int rowHeight = (int)Math.Round(34 * _scale);
            int rowWidth = widest + (int)Math.Round(58 * _scale);

            {
                for (int i = 0; i < _items.Length; i++)
                {
                    int index = i;
                    ToolStripMenuItem item = new ToolStripMenuItem(_items[i]);
                    int rowH = (int)Math.Round(34 * _scale);
                int textH = TextRenderer.MeasureText("Ag", menu.Font).Height;
                int padY = Math.Max(4, (rowH - textH) / 2);
                item.Padding = new Padding(8, padY, (int)Math.Round(34 * _scale), padY);
                    item.Font = menu.Font;
                    item.Tag = i == SelectedIndex ? (object)true : null;
                    item.Click += delegate { SelectedIndex = index; };
                    menu.Items.Add(item);
                }
            }
            _open = true;
            _active = menu;
            menu.Closed += delegate { _open = false; if (ReferenceEquals(_active, menu)) _active = null; Invalidate(); };
            // The strip sizes itself narrower than its padded rows, which clipped the
            // tick; size it to the rows instead.
            int widestItem = 0;
            int totalHeight = menu.Padding.Vertical;
            foreach (ToolStripItem entry in menu.Items)
            {
                Size wanted = entry.GetPreferredSize(Size.Empty);
                widestItem = Math.Max(widestItem, wanted.Width);
                totalHeight += wanted.Height;
            }
            menu.AutoSize = false;
            menu.Size = new Size(widestItem + menu.Padding.Horizontal, totalHeight);
            menu.Show(this, new Point(0, Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Clear the control rectangle with the parent colour first: the cell only
            // paints inside its rounded shape, and back when it did not the square
            // corners showed around the border.
            if (Parent != null)
            {
                using (SolidBrush clear = new SolidBrush(Parent.BackColor))
                    g.FillRectangle(clear, ClientRectangle);
            }

            Color back = _theme.Dark ? App.Shift(_theme.Surface, -10) : Color.White;
            if (_hover) back = App.Shift(back, _theme.Dark ? 12 : -6);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.Rounded(r, _radius))
            {
                using (SolidBrush brush = new SolidBrush(back)) g.FillPath(brush, path);
                using (Pen pen = new Pen(_theme.Border)) g.DrawPath(pen, path);
            }

            // chevron
            int cx = Width - 16;
            int cy = Height / 2;
            using (Pen pen = new Pen(_theme.TextMuted, 1.4f))
            {
                g.DrawLine(pen, cx - 4, cy - 2, cx, cy + 2);
                g.DrawLine(pen, cx, cy + 2, cx + 4, cy - 2);
            }

            Rectangle text = new Rectangle(12, 0, Width - 36, Height);
            TextRenderer.DrawText(g, SelectedItem, Font, text, _theme.Text, TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // A rounded button drawn from scratch. It derives from Control rather than
    // Button on purpose: ButtonBase still paints its own square background and
    // border underneath, which left a rectangle showing around the pill.
    internal sealed class PillButton : Control, IButtonControl
    {
        private bool _hover;
        private bool _down;
        private Color _fill;
        private readonly Color _border;
        private readonly int _radius;
        private readonly bool _primary;
        private bool _isDefault;

        public PillButton(string text, Color fill, Color textColor, Color border, float scale)
        {
            Text = text;
            _fill = fill;
            _border = border;
            _primary = border == Color.Empty;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            ForeColor = textColor;
            Font = new Font("Microsoft YaHei UI", 9f);
            Cursor = Cursors.Hand;
            TabStop = true;
            _radius = (int)Math.Round(12 * (scale <= 0 ? 1f : scale));
        }

        public DialogResult DialogResult { get; set; }

        public void NotifyDefault(bool value)
        {
            _isDefault = value;
            Invalidate();
        }

        public void PerformClick()
        {
            if (Enabled) OnClick(EventArgs.Empty);
        }

        public void SetFill(Color fill, Color textColor)
        {
            _fill = fill;
            ForeColor = textColor;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Focus(); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            // Space and Enter belong to the button, not to the dialog's navigation.
            if (keyData == Keys.Space || keyData == Keys.Enter) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                PerformClick();
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Whatever is behind the pill shows through its corners.
            if (Parent != null)
            {
                using (SolidBrush back = new SolidBrush(Parent.BackColor))
                    g.FillRectangle(back, ClientRectangle);
            }

            Color fill = _fill;
            if (!Enabled) fill = _primary ? App.Shift(_fill, -40) : _fill;
            else if (_down) fill = App.Shift(_fill, App.Luma(_fill) > 128 ? -14 : 10);
            else if (_hover) fill = App.Shift(_fill, App.Luma(_fill) > 128 ? -6 : 6);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.Rounded(r, _radius))
            {
                using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                Color stroke = _border == Color.Empty ? Color.Empty : _border;
                if (stroke != Color.Empty)
                {
                    using (Pen pen = new Pen(stroke)) g.DrawPath(pen, path);
                }
                else if (_isDefault)
                {
                    using (Pen pen = new Pen(Color.FromArgb(90, ForeColor)))
                    {
                        Rectangle inner = new Rectangle(2, 2, Width - 5, Height - 5);
                        using (GraphicsPath innerPath = Theme.Rounded(inner, Math.Max(2, _radius - 2)))
                            g.DrawPath(pen, innerPath);
                    }
                }
            }

            Color text = Enabled ? ForeColor : Color.FromArgb(150, ForeColor);
            TextRenderer.DrawText(g, Text, Font, r, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
    // A small glyph button for the address bar: no frame at rest, a rounded
    // highlight while pointed at, like the icon buttons in the app itself.
    internal sealed class GlyphButton : Control
    {
        private bool _hover;
        private bool _dark;
        private Color _symbol = Color.Black;
        private readonly float _scale;

        public GlyphButton(string glyph, string tip, float scale)
        {
            Text = glyph;
            _scale = scale <= 0 ? 1f : scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Font = new Font("Segoe UI", 11f);
            Cursor = Cursors.Hand;
            TabStop = false;
            BackColor = Color.Transparent;
            new ToolTip().SetToolTip(this, tip);
        }

        public void SetTheme(bool dark, Color symbol)
        {
            _dark = dark;
            _symbol = symbol;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Parent != null)
            {
                using (SolidBrush back = new SolidBrush(Parent.BackColor))
                    g.FillRectangle(back, ClientRectangle);
            }
            if (_hover)
            {
                int radius = Math.Max(4, (int)Math.Round(8 * _scale));
                using (GraphicsPath path = Theme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), radius))
                using (SolidBrush brush = new SolidBrush(_dark ? Color.FromArgb(52, 54, 58) : Color.FromArgb(226, 230, 236)))
                    g.FillPath(brush, path);
            }
            Color symbol = Enabled ? _symbol : Color.FromArgb(120, _symbol);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, symbol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class ChromeButton : Control
    {
        public ChromeKind Kind;
        private bool _dark;
        private Color _background = Color.White;
        private Color _symbol = Color.Black;
        private bool _hover;
        private float _glyphScale = 1f;

        public ChromeButton(ChromeKind kind)
        {
            Kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            TabStop = false;
            Cursor = Cursors.Default;
        }

        public void SetTheme(bool dark, Color background, Color symbol)
        {
            _dark = dark;
            _background = background;
            _symbol = symbol;
            Invalidate();
        }

        public void SetGlyphScale(float scale) { _glyphScale = scale <= 0 ? 1f : scale; }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        private Color HoverBack()
        {
            if (Kind == ChromeKind.Close) return Color.FromArgb(232, 17, 35);
            return _dark ? App.Shift(_background, 26) : App.Shift(_background, -18);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush back = new SolidBrush(_background))
                g.FillRectangle(back, ClientRectangle);
            if (_hover)
            {
                // Rounded hover, the way DSH's own icon buttons highlight.
                Rectangle hover = new Rectangle(3, 3, Width - 6, Height - 6);
                using (GraphicsPath path = Theme.Rounded(hover, Math.Max(4, (int)(8 * _glyphScale))))
                using (SolidBrush brush = new SolidBrush(HoverBack()))
                    g.FillPath(brush, path);
            }

            Color symbol = (_hover && Kind == ChromeKind.Close) ? Color.White : _symbol;
            float scale = _glyphScale;
            using (Pen pen = new Pen(symbol, Math.Max(1f, scale)))
            using (SolidBrush brush = new SolidBrush(symbol))
            {
                float cx = Width / 2f, cy = Height / 2f;
                float s = 4.5f * scale;

                switch (Kind)
                {
                    case ChromeKind.Minimize:
                        g.DrawLine(pen, cx - s, cy, cx + s, cy);
                        break;
                    case ChromeKind.Maximize:
                        g.DrawRectangle(pen, cx - s, cy - s, s * 2, s * 2);
                        break;
                    case ChromeKind.Restore:
                        g.DrawRectangle(pen, cx - s + 2 * scale, cy - s, s * 2, s * 2);
                        using (SolidBrush fill = new SolidBrush(_hover ? HoverBack() : _background))
                            g.FillRectangle(fill, cx - s, cy - s + 2 * scale, s * 2 - scale, s * 2 - scale);
                        g.DrawRectangle(pen, cx - s, cy - s + 2 * scale, s * 2, s * 2);
                        break;
                    case ChromeKind.Close:
                        g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                        g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s);
                        break;
                    case ChromeKind.Menu:
                        {
                            float r = Math.Max(1.2f, 1.3f * scale);
                            for (int i = -1; i <= 1; i++)
                            {
                                float x = cx + i * 4.2f * scale;
                                g.FillEllipse(brush, x - r, cy - r, r * 2, r * 2);
                            }
                        }
                        break;
                    case ChromeKind.Settings:
                        {
                            float outer = 5.2f * scale, inner = 2.0f * scale;
                            using (Pen gearPen = new Pen(symbol, Math.Max(1f, 1.15f * scale)))
                            {
                                g.DrawEllipse(gearPen, cx - outer, cy - outer, outer * 2, outer * 2);
                                for (int i = 0; i < 8; i++)
                                {
                                    double angle = Math.PI * i / 4.0;
                                    float x1 = cx + (float)(Math.Cos(angle) * outer);
                                    float y1 = cy + (float)(Math.Sin(angle) * outer);
                                    float x2 = cx + (float)(Math.Cos(angle) * (outer + 2.1f * scale));
                                    float y2 = cy + (float)(Math.Sin(angle) * (outer + 2.1f * scale));
                                    g.DrawLine(gearPen, x1, y1, x2, y2);
                                }
                            }
                            g.FillEllipse(brush, cx - inner, cy - inner, inner * 2, inner * 2);
                        }
                        break;
                }
            }
        }
    }

    // A pill toggle, the way DSH's own settings page renders booleans. WinForms
    // check boxes cannot be themed, so this is drawn.
    internal sealed class ToggleSwitch : Control
    {
        private bool _checked;
        private Theme _theme;
        private bool _hover;
        private float _scale = 1f;

        public event EventHandler CheckedChanged;

        public ToggleSwitch(Theme theme, bool value, float scale)
        {
            _theme = theme;
            _checked = value;
            _scale = scale <= 0 ? 1f : scale;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(S(44), S(24));
        }

        private int S(int v) { return (int)Math.Round(v * _scale); }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public void SetTheme(Theme theme)
        {
            _theme = theme;
            BackColor = theme.Surface;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled) Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                Checked = !Checked;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush back = new SolidBrush(Parent != null ? Parent.BackColor : _theme.Surface))
                g.FillRectangle(back, ClientRectangle);

            int h = S(22);
            int w = S(40);
            Rectangle track = new Rectangle(0, (Height - h) / 2, w, h);
            Color trackColor = _checked
                ? (Enabled ? _theme.Accent : App.Shift(_theme.Accent, 60))
                : (Enabled ? (_theme.Dark ? App.Shift(_theme.Surface, 46) : Color.FromArgb(206, 211, 219))
                           : (_theme.Dark ? App.Shift(_theme.Surface, 26) : Color.FromArgb(226, 229, 234)));
            if (_hover && !_checked) trackColor = App.Shift(trackColor, _theme.Dark ? 12 : -10);

            using (GraphicsPath path = Theme.Rounded(track, h / 2))
            using (SolidBrush brush = new SolidBrush(trackColor))
                g.FillPath(brush, path);

            int knob = h - S(4);
            int knobX = _checked ? track.Right - knob - S(2) : track.Left + S(2);
            Rectangle knobRect = new Rectangle(knobX, track.Top + S(2), knob, knob);
            using (SolidBrush knobBrush = new SolidBrush(Color.White))
                g.FillEllipse(knobBrush, knobRect);
        }
    }

    // Dark- or light-themed context menu. The default renderer paints a light
    // menu with system colours, which looks foreign next to the page.
    // A popup with rounded corners. ToolStripDropDown resets its own region for the
    // drop shadow, so the shape has to be applied after the menu has opened rather
    // than once when it is created.
    internal sealed class RoundedMenu : ContextMenuStrip
    {
        private readonly int _radius;
        private NativeMethods.MouseHookProc _hook;
        private IntPtr _hookHandle = IntPtr.Zero;

        // The tray menu asks Windows for its own soft drop shadow, which follows the
        // rounded region. The other popups keep it off, since a shadow would then sit
        // outside the strip and add padding.
        public bool SoftShadow;

        // A compositor-drawn shadow reaches every side; the class style only draws
        // one at the bottom right. Applied to the popup handle once it exists.
        public bool AllRoundShadow;

        private void ApplyAllRoundShadow()
        {
            if (!AllRoundShadow) return;
            try
            {
                MARGINS margins = new MARGINS();
                margins.Left = 1;
                margins.Right = 1;
                margins.Top = 1;
                margins.Bottom = 1;
                DwmExtendFrameIntoClientArea(Handle, ref margins);
            }
            catch { }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (SoftShadow) cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        public RoundedMenu(int radius)
        {
            _radius = Math.Max(4, radius);
            DropShadowEnabled = false;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            ApplyShape();
            ApplyAllRoundShadow();

            // A menu is its own window, so a click on the page behind it never reaches
            // the strip and it stayed open. The hook sees every button press.
            _hook = delegate(int code, IntPtr wParam, IntPtr lParam)
            {
                if (code >= 0)
                {
                    int message = wParam.ToInt32();
                    if (message == 0x0201 || message == 0x0204)
                    {
                        NativeMethods.MOUSEHOOKSTRUCT point = (NativeMethods.MOUSEHOOKSTRUCT)
                            System.Runtime.InteropServices.Marshal.PtrToStructure(
                                lParam, typeof(NativeMethods.MOUSEHOOKSTRUCT));
                        if (!Bounds.Contains(point.pt.X, point.pt.Y) && Visible)
                        {
                            try { BeginInvoke((MethodInvoker)delegate { Close(); }); }
                            catch { }
                        }
                    }
                }
                return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
            };
            _hookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL, _hook, NativeMethods.GetModuleHandle(null), 0);
        }

        protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
        {
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
                _hook = null;
            }
            base.OnClosed(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyShape();
        }

        private void ApplyShape()
        {
            if (Width <= 0 || Height <= 0) return;
            try
            {
                using (GraphicsPath path = Theme.RoundedExact(new Rectangle(0, 0, Width, Height), _radius))
                    Region = new Region(path);
            }
            catch { }
        }
    }

    internal sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Theme _theme;

        private readonly float _scale;

        public MenuRenderer(Theme theme) : this(theme, 1f) { }

        public MenuRenderer(Theme theme, float scale) : base(new MenuColors(theme))
        {
            _theme = theme;
            _scale = scale <= 0 ? 1f : scale;
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            // The strip paints its own BackColor; filling here also ran while each
            // item painted, wiping every row drawn before the last one.
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // No frame: the popup is told apart by its own tone, and a rectangle
            // border inside a rounded region leaves visible cut corners.
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
            {
                Rectangle r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                // A hint of emphasis rather than a block of colour.
                Color tint = _theme.Dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(16, 0, 0, 0);
                using (GraphicsPath path = Theme.Rounded(r, 12))
                using (SolidBrush brush = new SolidBrush(tint))
                    e.Graphics.FillPath(brush, path);
            }

            // A select row carries its tick as an image used only as a marker; it is
            // drawn here, at the right end of the row, because a menu item cannot
            // right-align its own image.
            if (e.Item.Tag is bool && (bool)e.Item.Tag)
            {
                Rectangle bounds = e.Item.Bounds;
            App.Log("render text: [" + (e.Item.Text ?? "") + "] bounds=" + bounds + " h=" + e.Item.Height + " auto=" + e.Item.AutoSize);
                Rectangle box = new Rectangle(
                    bounds.Right - (int)Math.Round(_scale * 26), bounds.Top,
                    (int)Math.Round(_scale * 18), bounds.Height);
                TextRenderer.DrawText(e.Graphics, "\u2713", e.Item.Font, box, _theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private void PaintRow(ToolStripItemRenderEventArgs e)
        {
            ToolStripMenuItem entry = e.Item as ToolStripMenuItem;
            if (entry == null || string.IsNullOrEmpty(entry.Text)) return;
            Color color = entry.Enabled ? _theme.Text : _theme.TextMuted;
            // The graphics for an item is already translated to that item, so the
            // box has to be item-relative; using Bounds drew every row but the first
            // outside its own clip.
            int pad = (int)Math.Round(12 * _scale);
            RectangleF area = new RectangleF(pad, 0,
                Math.Max(8, entry.Width - pad * 2), entry.Height);
            using (StringFormat format = new StringFormat())
            using (SolidBrush brush = new SolidBrush(color))
            {
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
                Font font = entry.Font;
                if (font == null) font = SystemFonts.MenuFont;
                e.Graphics.DrawString(entry.Text, font, brush, area, format);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // A fixed line box, so the baseline does not move with the measured height
            // of each label (strings with a descender used to sit higher).
            ToolStripMenuItem entry = e.Item as ToolStripMenuItem;
            if (entry == null) return;
            // The graphics for an item is already translated to that item, so the
            // box has to be item-relative; using Bounds drew every row but the first
            // outside its own clip.
            int pad = (int)Math.Round(12 * _scale);
            RectangleF area = new RectangleF(pad, 0,
                Math.Max(8, entry.Width - pad * 2), entry.Height);
            Color color = entry.Enabled ? _theme.Text : _theme.TextMuted;
            Font font = entry.Font;
            if (font == null) font = SystemFonts.MenuFont;
            // GDI text, not GDI+: the same 9pt string renders larger through
            // Graphics.DrawString, which is why the menu labels did not match the
            // field they opened from.
            TextRenderer.DrawText(e.Graphics, entry.Text ?? "", font,
                Rectangle.Round(area), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen pen = new Pen(_theme.Border))
                e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = _theme.TextMuted;
            base.OnRenderArrow(e);
        }
    }

    internal sealed class MenuColors : ProfessionalColorTable
    {
        private readonly Theme _theme;
        public MenuColors(Theme theme) { _theme = theme; }

        public override Color ToolStripDropDownBackground { get { return _theme.Elevated; } }
        public override Color MenuItemSelected { get { return _theme.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return _theme.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return _theme.Hover; } }
        public override Color MenuItemBorder { get { return _theme.Hover; } }
        public override Color ImageMarginGradientBegin { get { return _theme.Elevated; } }
        public override Color ImageMarginGradientMiddle { get { return _theme.Elevated; } }
        public override Color ImageMarginGradientEnd { get { return _theme.Elevated; } }
        public override Color SeparatorDark { get { return _theme.Border; } }
        public override Color SeparatorLight { get { return _theme.Border; } }
    }

    // A thin scroll bar matching the theme. The native one ignores colour, and
    // the settings form needs to scroll to reach its last rows. Proportions copy
    // DSH's own scrollbars: a rounded thumb inset from the track, no track fill,
    // and a stronger thumb while pointed at.
    internal sealed class ScrollBarEx : Control
    {
        private Theme _theme;
        private int _value;
        private int _maximum = 100;
        private int _viewport = 100;
        private int _thumbTop;
        private int _thumbHeight;
        private bool _dragging;
        private int _dragOffset;
        private bool _hover;
        private readonly float _scale;

        public event EventHandler ValueChanged;

        public ScrollBarEx(Theme theme, float scale)
        {
            _theme = theme;
            _scale = scale <= 0 ? 1f : scale;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            Width = S(12);
            Cursor = Cursors.Hand;
        }

        private int S(int v) { return (int)Math.Round(v * _scale); }

        public void SetTheme(Theme theme) { _theme = theme; Invalidate(); }

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(0, Math.Min(MaxValue, value));
                if (clamped == _value) return;
                _value = clamped;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public int MaxValue { get { return Math.Max(0, _maximum - _viewport); } }

        public void Configure(int contentHeight, int viewportHeight)
        {
            _maximum = Math.Max(1, contentHeight);
            _viewport = Math.Max(1, viewportHeight);
            if (_value > MaxValue) _value = MaxValue;
            Invalidate();
        }

        private void ComputeThumb()
        {
            if (_maximum <= _viewport)
            {
                _thumbTop = 0;
                _thumbHeight = 0;
                return;
            }
            int inset = S(4);
            int trackTop = inset;
            int trackHeight = Math.Max(1, Height - inset * 2);
            _thumbHeight = Math.Max(S(28), (int)((double)_viewport / _maximum * trackHeight));
            int travel = Math.Max(1, trackHeight - _thumbHeight);
            _thumbTop = trackTop + (int)((double)_value / Math.Max(1, MaxValue) * travel);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush back = new SolidBrush(_theme.Surface))
                g.FillRectangle(back, ClientRectangle);
            ComputeThumb();
            if (_thumbHeight <= 0) return;

            // 6px visible thumb inside a 12px hit area, like the 2px transparent
            // border with padding-box clipping DSH applies to its own thumbs.
            bool active = _dragging || _hover;
            Color color = _theme.Dark
                ? (active ? Color.FromArgb(124, 132, 145) : Color.FromArgb(72, 78, 88))
                : (active ? Color.FromArgb(158, 166, 178) : Color.FromArgb(205, 211, 220));
            int width = S(6);
            int left = (Width - width) / 2;
            Rectangle thumb = new Rectangle(left, _thumbTop, width, _thumbHeight);
            using (GraphicsPath path = Theme.Rounded(thumb, width / 2))
            using (SolidBrush brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            ComputeThumb();
            if (e.Y >= _thumbTop && e.Y <= _thumbTop + _thumbHeight)
            {
                _dragging = true;
                _dragOffset = e.Y - _thumbTop;
            }
            else
            {
                int trackTop = S(4);
                int trackHeight = Math.Max(1, Height - S(8));
                int travel = Math.Max(1, trackHeight - _thumbHeight);
                double ratio = (double)Math.Max(0, e.Y - trackTop - _thumbHeight / 2) / travel;
                Value = (int)Math.Round(ratio * MaxValue);
            }
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                int trackTop = S(4);
                int trackHeight = Math.Max(1, Height - S(8));
                int travel = Math.Max(1, trackHeight - _thumbHeight);
                double ratio = (double)Math.Max(0, e.Y - _dragOffset - trackTop) / travel;
                Value = (int)Math.Round(ratio * MaxValue);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Value -= Math.Sign(e.Delta) * 3 * S(28);
            base.OnMouseWheel(e);
        }
    }
}
