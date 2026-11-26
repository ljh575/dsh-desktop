// The launcher's own dialogs: downloading a WebView2 runtime, and updating the
// bundled dsh in place with pnpm. Both follow the same visual language as the
// settings form, which follows the page.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace DshDesktop
{
    // Shared frame: rounded borderless window, themed, with a drawn title row.
    internal abstract class DsDialog : Form
    {
        protected readonly Theme Palette;
        protected new readonly float Scale;
        private readonly Panel _header;
        private readonly Label _titleLabel;
        private readonly ChromeButton _close;

        protected DsDialog(Theme palette, string title, int width, int height) : this(palette, title, width, height, 28) { }

        protected DsDialog(Theme palette, string title, int width, int height, int cornerRadius)
        {
            Palette = palette;
            Scale = App.Scale();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            // The form's own background is the surface colour, so every custom-painted
            // child that clears with Parent.BackColor blends in. Windows 10 draws a 1px
            // gray rail in OnPaintBackground and insets its children; Windows 11 has no
            // rail and gets its frame and soft shadow from DWM.
            BackColor = palette.Surface;
            Padding = new Padding(NativeMethods.Windows11 ? 0 : 1);
            ForeColor = palette.Text;
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(S(width), S(height));
            try { Icon = App.PickThemeIcon(App.S != null ? App.S.Icon : "default"); } catch { }
            DoubleBuffered = true;

            _header = new Panel();
            _header.Dock = DockStyle.Top;
            _header.Height = S(52);
            _header.BackColor = palette.Header;
            _header.Paint += delegate(object sender, PaintEventArgs e) { PaintHeader(e.Graphics); };
            Controls.Add(_header);

            _titleLabel = new Label();
            _titleLabel.Text = title;
            _titleLabel.AutoSize = false;
            _titleLabel.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            _titleLabel.ForeColor = palette.Text;
            _titleLabel.BackColor = palette.Header;
            _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            _header.Controls.Add(_titleLabel);

            _close = new ChromeButton(ChromeKind.Close);
            _close.SetTheme(palette.Dark, palette.Header, palette.Text);
            _close.SetGlyphScale(Scale);
            _close.Click += delegate { OnTitleClose(); };
            _header.Controls.Add(_close);

            // The panel is anchored over the window, like DSH's own settings panel:
            // dragging it would only break that, so the header does not grab.

            _header.Resize += delegate { LayoutHeader(); };
            // Setting the preference before the window is shown can be ignored by
            // the compositor, so ask again once it is up.
            Shown += delegate { NativeMethods.RoundCorners(Handle); };
            LayoutHeader();
            Resize += delegate { ApplyRegion(); };
            ApplyRegion();
        }

        protected virtual void OnTitleClose() { DialogResult = DialogResult.Cancel; Close(); }

        protected int S(int value) { return (int)Math.Round(value * Scale); }

        // The 1px gray rail inset on Windows 10; zero on Windows 11.
        protected int BorderSize { get { return NativeMethods.Windows11 ? 0 : 1; } }

        private void LayoutHeader()
        {
            if (_header == null) return;
            _header.Height = S(52);
            int button = S(40);
            _close.SetBounds(_header.ClientSize.Width - button, 0, button, _header.Height);
            _titleLabel.SetBounds(S(20), 0, Math.Max(0, _close.Left - S(28)), _header.Height);
        }

        private void PaintHeader(Graphics g)
        {
            using (SolidBrush brush = new SolidBrush(Palette.Header))
                g.FillRectangle(brush, _header.ClientRectangle);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            // Windows 10: the 1px gray rail is drawn here (before the children), so the
            // form's BackColor stays the surface colour and custom controls that clear
            // with Parent.BackColor keep blending instead of leaving a gray square.
            if (!NativeMethods.Windows11)
            {
                using (Pen pen = new Pen(Palette.Dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(200, 200, 200)))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        private void ApplyRegion()
        {
            try
            {
                // Windows 10 dialogs stay square with the default style; Windows 11
                // gets its rounded corners from DWM, not a region clip.
                Region = null;
                if (!NativeMethods.Windows11) return;
                NativeMethods.SuppressBorder(Handle);
                NativeMethods.RoundCorners(Handle);
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // No outline: the surface step plus the drop shadow already separate
            // the panel from the page, and a stroke on a rounded corner tends to
            // look like a seam. A faint top highlight keeps the edge readable on a
            // dark page without reading as a border.

        }

        protected Label AddText(string text, int x, int y, int width, int height, Font font, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.SetBounds(S(x), S(y), S(width), S(height));
            label.ForeColor = color;
            label.BackColor = Palette.Surface;
            label.Font = font;
            Controls.Add(label);
            label.BringToFront();
            return label;
        }

        protected PillButton AddButton(string text, int x, int y, int width, bool primary)
        {
            PillButton button = new PillButton(text,
                primary ? Palette.Accent : Palette.Elevated,
                primary ? Palette.AccentText : Palette.Text,
                primary ? Color.Empty : Palette.Border, Scale);
            button.SetBounds(S(x), S(y), S(width), S(34));
            Controls.Add(button);
            button.BringToFront();
            return button;
        }
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DwmWindowCornerPreference = 33;
        private const int DwmBorderColor = 34;
        private const int DwmRound = 2;
        private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

        // Windows 11 paints a one-pixel frame around a window it rounds. These windows
        // have no frame of their own, so that frame reads as a stray grey outline;
        // asking for "no border" drops it and keeps the rounded silhouette.
        public static void SuppressBorder(IntPtr handle)
        {
            if (!Windows11) return;
            try
            {
                int none = DwmColorNone;
                DwmSetWindowAttribute(handle, DwmBorderColor, ref none, sizeof(int));
            }
            catch { }
        }

        public static bool Windows11
        {
            get { return Environment.OSVersion.Version.Build >= 22000; }
        }

        // Windows 11 rounds and antialiases the window itself, which looks far
        // better than clipping a Region. Returns true when the window was handed
        // to the compositor, in which case a Region must not also be set.
        public static bool RoundCorners(IntPtr handle)
        {
            if (!Windows11) { App.Log("dwm round skipped: not Windows 11"); return false; }
            return SetCorners(handle, DwmRound);
        }

        // DWMWCP_DONOTROUND: a maximised window must not be clipped, or the desktop
        // shows through its corners as a ring.
        public static bool SquareCorners(IntPtr handle)
        {
            if (!Windows11) return false;
            return SetCorners(handle, 1);
        }

        private static bool SetCorners(IntPtr handle, int preference)
        {
            try
            {
                int value = preference;
                int result = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref value, sizeof(int));
                if (result != 0) App.Log("dwm corners failed: " + result.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return result == 0;
            }
            catch { return false; }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct WindowRect { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out WindowRect rect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct MonitorInfo
        {
            public int cbSize;
            public WindowRect rcMonitor;
            public WindowRect rcWork;
            public uint dwFlags;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);

        public const int WH_MOUSE_LL = 14;

        public delegate IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct POINT32
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct MOUSEHOOKSTRUCT
        {
            public POINT32 pt;
            public IntPtr hwnd;
            public uint wHitTestCode;
            public IntPtr dwExtraInfo;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int id, MouseHookProc callback, IntPtr module, uint thread);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string name);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int key);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT32 point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    // Slim themed progress bar; the WinForms one always paints its own gradient.
    internal sealed class ProgressBarEx : Control
    {
        private readonly Theme _theme;
        private readonly float _scale;
        private int _value;

        public ProgressBarEx(Theme theme, float scale)
        {
            _theme = theme;
            _scale = scale <= 0 ? 1f : scale;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = theme.Surface;
            Canvas = theme.Surface;
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(0, Math.Min(100, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
            }
        }

        // What the bar is drawn on: the dialog surface by default, or whatever the
        // host paints behind it.
        public Color Canvas;

        private int S(int v) { return (int)Math.Round(v * _scale); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush back = new SolidBrush(Canvas))
                g.FillRectangle(back, ClientRectangle);

            int h = S(6);
            Rectangle track = new Rectangle(0, (Height - h) / 2, Width, h);
            Color trackColor = _theme.Dark ? App.Shift(_theme.Surface, 34) : Color.FromArgb(230, 233, 238);
            using (GraphicsPath path = Theme.Rounded(track, h / 2))
            using (SolidBrush brush = new SolidBrush(trackColor))
                g.FillPath(brush, path);

            int filled = (int)(Width * (_value / 100.0));
            if (filled <= 0) return;
            Rectangle fill = new Rectangle(0, track.Top, Math.Max(h, filled), h);
            using (GraphicsPath path = Theme.Rounded(fill, h / 2))
            using (SolidBrush brush = new SolidBrush(_theme.Accent))
                g.FillPath(brush, path);
        }
    }

    // Shown when the machine has no WebView2 and no copy of our own yet.
    internal sealed class DownloadDialog : DsDialog
    {
        private readonly RuntimeProgress _progress = new RuntimeProgress();
        private readonly Label _detail;
        private readonly Label _stage;
        private readonly ProgressBarEx _bar;
        private readonly PillButton _cancel;
        private readonly PillButton _retry;
        private readonly System.Windows.Forms.Timer _timer;

        public string RuntimeFolder;
        public bool ForceDownload;

        public DownloadDialog(Theme theme) : base(theme, Strings.T("dl.title"), 520, 260)
        {
            AddText(Strings.T("dl.body"), 20, 52, 480, 62,
                new Font("Microsoft YaHei UI", 9f), Palette.TextMuted);

            _bar = new ProgressBarEx(theme, Scale);
            _bar.SetBounds(S(20), S(126), S(480), S(18));
            Controls.Add(_bar);

            _stage = AddText(Strings.T("dl.downloading"), 20, 150, 300, 22,
                new Font("Microsoft YaHei UI", 9f), Palette.Text);
            _detail = AddText("", 20, 174, 480, 22,
                new Font("Microsoft YaHei UI", 8.5f), Palette.TextMuted);

            _cancel = AddButton(Strings.T("dl.cancel"), 428, 206, 72, false);
            _cancel.Click += delegate { _progress.Cancelled = true; DialogResult = DialogResult.Cancel; Close(); };

            _retry = AddButton(Strings.T("dl.retry"), 348, 206, 72, true);
            _retry.Visible = false;
            _retry.Click += delegate
            {
                _retry.Visible = false;
                _progress.Cancelled = false;
                _progress.Failed = false;
                _progress.Done = false;
                _progress.Error = null;
                _bar.Value = 0;
                Run();
            };

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 200;
            _timer.Tick += delegate { Tick(); };
            _timer.Start();

            Shown += delegate { Run(); };
            FormClosing += delegate { _timer.Stop(); };
        }

        private void Run()
        {
            Thread worker = new Thread(delegate()
            {
                try { _progress.Result = WebViewRuntime.Ensure(_progress, ForceDownload); }
                catch (Exception ex)
                {
                    _progress.Failed = true;
                    _progress.Error = ex.Message;
                }
                finally { _progress.Done = true; }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void Tick()
        {
            _bar.Value = _progress.Percent;
            string stage = _progress.Stage;
            if (stage == "download") _stage.Text = Strings.T("dl.downloading");
            else if (stage == "extract") _stage.Text = Strings.T("dl.extracting");
            else if (stage == "install") _stage.Text = Strings.T("dl.verifying");
            else if (stage == "system" || stage == "local") _stage.Text = Strings.T("dl.verifying");
            _detail.Text = _progress.Detail ?? "";

            if (_progress.Failed)
            {
                _timer.Stop();
                _stage.Text = Strings.T("dl.failed");
                _detail.Text = _progress.Error ?? Strings.T("dl.noNetwork");
                _retry.Visible = true;
                _cancel.Text = Strings.T("update.close");
                App.Log("webview2 unavailable: " + (_progress.Error ?? "unknown"));
                return;
            }
            // Only finish once the worker has stored its result: progress can reach
            // 100% inside Ensure() a moment before the assignment lands, and closing
            // on that would return an empty runtime path.
            if (_progress.Cancelled || (_progress.Done && !_progress.Failed))
            {
                _timer.Stop();
                RuntimeFolder = _progress.Result;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }

    // Checks the npm registry and, on request, replaces the bundled dsh in place
    // with pnpm. Nothing is installed system-wide.
    // A themed replacement for MessageBox: a native message box in the middle of
    // an otherwise themed flow looks like a different program.
    internal sealed class ConfirmDialog : DsDialog
    {
        public ConfirmDialog(Theme theme, string title, string message, string yes, string no)
            : base(theme, title, 420, 200)
        {
            AddText(message, 20, 58, 380, 52, new Font("Microsoft YaHei UI", 9f), Palette.Text);

            PillButton later = AddButton(no, 196, 136, 96, false);
            later.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            PillButton restart = AddButton(yes, 304, 136, 96, true);
            restart.Click += delegate { DialogResult = DialogResult.OK; Close(); };

            AcceptButton = restart;
            CancelButton = later;
        }
    }

    // About: the launcher version, what it is running on, and who wrote it. A
    // themed panel rather than a MessageBox so the whole flow stays consistent.
    internal sealed class AboutDialog : DsDialog
    {
        public AboutDialog(Theme theme, string mode, string address, string dshVersion,
            string nodeVersion, string webviewVersion, string dataDir, string iniPath, string logPath)
            : base(theme, Strings.T("about.title"), 520, 440)
        {
            string[] labels = new string[]
            {
                Strings.T("about.version"), Strings.T("about.dsh"), Strings.T("about.node"),
                Strings.T("about.webview"), Strings.T("about.mode"), Strings.T("about.address"),
                Strings.T("about.data"), Strings.T("about.ini"), Strings.T("about.log")
            };
            string[] values = new string[]
            {
                App.Version, dshVersion, nodeVersion, webviewVersion, mode, address, dataDir, iniPath, logPath
            };

            Label name = AddText("DSH Desktop", 20, 60, 300, 30,
                new Font("Microsoft YaHei UI", 13f, FontStyle.Bold), Palette.Text);
            name.TextAlign = ContentAlignment.MiddleLeft;
            AddText(Strings.T("about.tagline"), 20, 88, 460, 20,
                new Font("Microsoft YaHei UI", 9f), Palette.TextMuted);

            int y = 120;
            for (int i = 0; i < labels.Length; i++)
            {
                Label key = AddText(labels[i], 20, y, 108, 22, new Font("Microsoft YaHei UI", 9f), Palette.TextMuted);
                key.TextAlign = ContentAlignment.MiddleLeft;
                Label value = AddText(values[i] ?? "", 134, y, 366, 22, new Font("Microsoft YaHei UI", 9f), Palette.Text);
                value.TextAlign = ContentAlignment.MiddleLeft;
                value.AutoEllipsis = true;
                y += 26;
            }

            AddText("© 2026 ljh575 · " + Strings.T("about.rights"), 20, y + 6, 460, 22,
                new Font("Microsoft YaHei UI", 9f), Palette.TextMuted);

            PillButton copy = AddButton(Strings.T("about.copy"), 236, 378, 104, false);
            copy.Click += delegate
            {
                string text = "DSH Desktop " + App.Version + Environment.NewLine
                    + "dsh " + dshVersion + Environment.NewLine
                    + "node " + nodeVersion + Environment.NewLine
                    + "WebView2 " + webviewVersion + Environment.NewLine
                    + "mode " + mode + Environment.NewLine
                    + address + Environment.NewLine
                    + dataDir + Environment.NewLine
                    + iniPath + Environment.NewLine
                    + logPath;
                try { Clipboard.SetText(text); copy.Text = Strings.T("about.copied"); }
                catch (Exception ex) { App.Log("clipboard failed: " + ex.Message); }
            };
            PillButton close = AddButton(Strings.T("update.close"), 348, 378, 104, true);
            close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            CancelButton = close;
        }
    }


    internal sealed class UpdateDialog : DsDialog
    {
        private readonly Label _installed;
        private readonly Label _latest;
        private readonly Label _status;
        private readonly TextBox _log;
        private readonly PillButton _update;
        private readonly PillButton _close;
        private readonly SelectCell _channel;
        private readonly ToggleSwitch _force;
        private readonly float _scale;
        public bool RestartRequested;

        private string _latestVersion;
        private string _installedVersion = "?";

        private readonly Action _beforeUpdate;

        // The two channels the registry publishes. "latest" is the release stream,
        // "next" carries previews; the check re-runs whenever the choice changes.
        private static readonly string[] Channels = new string[] { "latest", "next" };

        public UpdateDialog(Theme theme, Action beforeUpdate) : base(theme, Strings.T("update.title"), 600, 478)
        {
            _beforeUpdate = beforeUpdate;
            _scale = Scale;
            _theme_dark = theme.Dark;

            _installedVersion = Maintenance.InstalledVersion();
            AddText(Strings.T("update.current") + ":", 20, 64, 120, 22, new Font("Microsoft YaHei UI", 9f), Palette.TextMuted);
            _installed = AddText(_installedVersion, 146, 64, 430, 22, new Font("Microsoft YaHei UI", 9f), Palette.Text);

            AddText(Strings.T("update.channel") + ":", 20, 94, 120, 22, new Font("Microsoft YaHei UI", 9f), Palette.TextMuted);
            _channel = new SelectCell(theme, _scale);
            _channel.Items = new string[] { "latest", "next" };
            _channel.SelectedIndex = 0;
            _channel.SetBounds(S(146), S(90), S(160), S(30));
            _channel.SelectionChanged += delegate { Check(); };
            Controls.Add(_channel);
            _channel.BringToFront();

            AddText(Strings.T("update.latest") + ":", 20, 128, 120, 22, new Font("Microsoft YaHei UI", 9f), Palette.TextMuted);
            _latest = AddText("…", 146, 128, 430, 22, new Font("Microsoft YaHei UI", 9f), Palette.Text);

            AddText(Strings.T("update.force"), 20, 158, 300, 22, new Font("Microsoft YaHei UI", 9f), Palette.Text);
            _force = new ToggleSwitch(theme, false, _scale);
            _force.SetBounds(S(452), S(160), S(46), S(24));
            _force.CheckedChanged += delegate { UpdateAvailability(); };
            Controls.Add(_force);
            _force.BringToFront();

            _status = AddText(Strings.T("update.hint"), 20, 190, 560, 34,
                new Font("Microsoft YaHei UI", 8.5f), Palette.TextMuted);

            _log = new TextBox();
            _log.Multiline = true;
            _log.ReadOnly = true;
            // Native text-box scroll bars ignore the theme and stand out badly on
            // a dark dialog; the box follows the caret instead.
            _log.ScrollBars = ScrollBars.None;
            _log.WordWrap = false;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = theme.Dark ? App.Shift(theme.Surface, -6) : Color.FromArgb(248, 249, 251);
            _log.ForeColor = theme.TextMuted;
            _log.Font = new Font("Consolas", 8.5f);
            _log.SetBounds(S(20), S(232), S(560), S(168));
            Controls.Add(_log);
            _log.BringToFront();

            _close = AddButton(Strings.T("update.close"), 508, 432, 72, false);
            _close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            _update = AddButton(Strings.T("update.now"), 392, 432, 108, true);
            _update.Enabled = false;
            _update.Click += delegate { Apply(); };
            SetUpdateEnabled(false);

            Shown += delegate { Check(); };
        }

        // A disabled Button keeps its custom colours, so the primary style is
        // swapped for a muted one instead of relying on the greyed-out text.
        private void SetUpdateEnabled(bool enabled)
        {
            _update.Enabled = enabled;
            // Only the pill's own fill changes: assigning BackColor made WinForms paint
            // a square behind the rounded shape.
            _update.SetFill(
                enabled ? Palette.Accent : (_theme_dark ? App.Shift(Palette.Surface, 22) : Color.FromArgb(226, 230, 236)),
                enabled ? Palette.AccentText : Palette.TextMuted);
        }

        private bool _theme_dark;

        private string Channel { get { return Channels[Math.Max(0, Math.Min(Channels.Length - 1, _channel.SelectedIndex))]; } }

        private void Append(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            _log.AppendText(line + Environment.NewLine);
            App.Log("[update] " + line);
        }

        private void Check()
        {
            _latestVersion = null;
            _latest.Text = "…";
            _status.Text = Strings.T("update.checking");
            SetUpdateEnabled(false);
            _log.Clear();
            string channel = Channel;
            Thread worker = new Thread(delegate()
            {
                string found = null;
                try { found = Maintenance.TagVersion(channel); } catch (Exception ex) { App.Log("update check failed: " + ex.Message); }
                if (string.IsNullOrEmpty(found) && channel == "latest") found = Maintenance.LatestVersion();

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _latestVersion = found;
                        _latest.Text = found ?? "?";
                        if (string.IsNullOrEmpty(found))
                        {
                            _status.Text = Strings.T("update.noNetwork");
                            SetUpdateEnabled(_force.Checked);
                            return;
                        }
                        UpdateAvailability();
                    });
                }
                catch { }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        // Decide whether the button may run, honouring the force switch.
        private void UpdateAvailability()
        {
            if (string.IsNullOrEmpty(_latestVersion))
            {
                SetUpdateEnabled(false);
                return;
            }
            bool same = string.Equals(_latestVersion, _installedVersion, StringComparison.OrdinalIgnoreCase);
            bool older = Maintenance.Compare(_latestVersion, _installedVersion) <= 0;
            if (_force.Checked)
            {
                // Reinstall whatever the channel points at, even the same build.
                _status.Text = same
                    ? Strings.T("update.forceSame")
                    : (older ? Strings.T("update.forceOlder") : Strings.T("update.found"));
                SetUpdateEnabled(true);
                return;
            }
            if (same) { _status.Text = Strings.T("update.upToDate"); SetUpdateEnabled(false); return; }
            if (older)
            {
                // Never offer a downgrade without the force switch.
                _status.Text = Strings.T("update.upToDate") + " (" + _latestVersion + ")";
                SetUpdateEnabled(false);
                return;
            }
            _status.Text = Strings.T("update.found");
            SetUpdateEnabled(true);
        }

        private void Apply()
        {
            if (_latestVersion == null) return;
            SetUpdateEnabled(false);
            _close.Enabled = false;
            _channel.Enabled = false;
            _force.Enabled = false;
            _status.Text = Strings.T("update.running");
            _log.Clear();
            // The bundled server holds files inside app\node_modules; the swap
            // replaces that directory, so the caller stops it first.
            try { if (_beforeUpdate != null) _beforeUpdate(); }
            catch (Exception ex) { App.Log("update: stop server failed: " + ex.Message); }

            Thread worker = new Thread(delegate()
            {
                bool ok = Maintenance.UpdateTo(_latestVersion, delegate(string line)
                {
                    try
                    {
                        if (IsHandleCreated && !IsDisposed)
                            BeginInvoke((MethodInvoker)delegate { Append(line); });
                    }
                    catch { }
                });

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _close.Enabled = true;
                        if (ok)
                        {
                            _status.Text = Strings.T("update.done");
                            _installedVersion = Maintenance.InstalledVersion();
                            _installed.Text = _installedVersion;
                            DialogResult = DialogResult.OK;
                            using (ConfirmDialog ask = new ConfirmDialog(
                                Theme.FromPage(_theme_dark, Palette.Surface), Strings.T("update.title"),
                                Strings.T("update.restartAsk"), Strings.T("common.yes"), Strings.T("common.no")))
                            {
                                if (ask.ShowDialog(this) == DialogResult.OK) RestartRequested = true;
                            }
                            Close();
                        }
                        else
                        {
                            _status.Text = Strings.T("update.failed");
                            _channel.Enabled = true;
                            _force.Enabled = true;
                            SetUpdateEnabled(true);
                        }
                    });
                }
                catch { }
            });
            worker.IsBackground = true;
            worker.Start();
        }
    }
}
