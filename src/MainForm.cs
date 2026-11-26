// The window: a frameless shell with a drawn title bar that follows the page's
// colours, a WebView2 surface, and the menus that reach settings and updates.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DshDesktop
{
    internal sealed class MainForm : Form
    {
        private readonly Settings _s;
        private DshServer _server;
        private WebView2 _view;
        private Label _splash;
        private ProgressBarEx _splashBar;
        private Label _splashSub;
        private System.Windows.Forms.Timer _installWatch;
        private int _installPercent;
        private long _installBytesMax;
        private Panel _bar;
        private TextBox _address;
        private Panel _addressCell;
        private string _addressValue = "";
        private bool _addressEditing;
        private GlyphButton _back, _forward, _reload, _home, _zoomOut, _zoomIn;
        private Label _zoomLabel;
        private System.Windows.Forms.Timer _selfTestTimer;
        private bool _finished;
        private bool _failureShown;
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private bool _exiting;
        private FormWindowState _stateBeforeHide = FormWindowState.Normal;
        private bool _trayHintShown;
        private bool _savedLayout;
        private string _entryUrl;      // tokenised URL; the only form DSH accepts

        // Immersive chrome, modelled on the official desktop shell: a frameless
        // window with a 40px title bar that paints itself in the page's own
        // colours, the way Electron's titleBarOverlay does there.
        private Panel _title;
        private Panel _topResizeBar;
        private Label _titleText;
        private PictureBox _titleIcon;
        private ChromeButton _btnSettings, _btnMenu, _btnMin, _btnMax, _btnClose;
        private float _scale = 1f;
        private bool _dark;
        private Color _chromeLeft = Color.White;    // page colour behind the title text
        private Color _chromeRight = Color.White;   // page colour behind the window buttons
        private int _chromeSplit;                   // pixel where the two meet
        private int _chromeHeight = 40;
        private Theme _theme;

        public MainForm(Settings settings)
        {
            _s = settings;
            Text = "DSH Desktop";
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1280, 820);
            MinimumSize = new Size(560, 420);
            BackColor = Color.FromArgb(224, 224, 224);
            Padding = new Padding(0);
            try { Icon = App.PickThemeIcon(_s.Icon); }
            catch { }
            _savedLayout = LoadLayout();

            DropRetiredIniKeys();
            _scale = App.Scale();
            if (_scale <= 0) _scale = 1f;
            _chromeHeight = (int)Math.Round(40 * _scale);

            BuildTitleBar();
            _dark = !App.SystemUsesLightTheme();
            _chromeLeft = _dark ? Color.FromArgb(24, 25, 28) : Color.FromArgb(247, 248, 250);
            _chromeRight = _chromeLeft;
            ApplyChrome();

            if (_s.CloseToTray && !_s.SelfTest) SetupTray();

            if (_s.Toolbar) BuildToolbar();
            // Dock=Top stacks by z-order; the title bar has to stay above the bar.
            if (_bar != null) { _bar.SendToBack(); _title.BringToFront(); }

            _splash = new Label();
            _splash.Dock = DockStyle.Fill;
            _splash.TextAlign = ContentAlignment.MiddleCenter;
            // The original splash face: Segoe UI 11pt, not the UI font used elsewhere.
            _splash.Font = new Font("Segoe UI", 11f);
            _splash.Text = _s.Server
                ? (Strings.Current == Lang.Zh
                    ? "正在启动 DeepSeek Harness…\r\n\r\n首次启动会初始化工作区与插件，可能需要十几秒。"
                    : "Starting DeepSeek Harness…\r\n\r\nThe first launch initialises the workspace and plugins; this can take a while.")
                : (Strings.Current == Lang.Zh ? "正在连接 " : "Connecting to ") + _s.Url + " …";
            Controls.Add(_splash);

            // First launch installs dsh, so the splash gets a bar and a byte count.
            _splashBar = new ProgressBarEx(_theme ?? Theme.FromPage(_dark, _chromeRight), _scale);
            _splashBar.Visible = false;
            _splashSub = new Label();
            _splashSub.AutoSize = false;
            _splashSub.TextAlign = ContentAlignment.MiddleCenter;
            _splashSub.Font = new Font("Microsoft YaHei UI", 9f);
            _splashSub.Visible = false;
            Controls.Add(_splashBar);
            Controls.Add(_splashSub);
            ApplyChrome();

            Shown += delegate
            {
                FitToDesktop();
                if (_bar != null) LayoutBar(_bar);
                Launch();
            };

            // Windows 11 rounds frameless windows only when asked; the official
            // desktop shell comes out rounded, so match it. Windows 10 stays square.
            HandleCreated += delegate { RoundShell(); };
            FormClosing += OnFormClosing;
            KeyPreview = true;
            KeyDown += OnKeyDown;
        }

        // The official desktop shell has rounded corners on Windows 11. DWM rounds
        // a framed window itself; a frameless one has to be clipped, and then only
        // while it is not maximised (a maximised window must fill its bounds).
        private void RoundShell()
        {
            try
            {
                // Square by default. Windows 11 rounds the window through DWM (which also
                // draws the system's own soft shadow); Windows 10 stays square and uses the
                // default style, matching dshview-client.
                Region = null;
                if (WindowState == FormWindowState.Maximized)
                {
                    NativeMethods.SquareCorners(Handle);
                    return;
                }
                NativeMethods.SuppressBorder(Handle);
                NativeMethods.RoundCorners(Handle);
            }
            catch { }
        }

        private int S(int value) { return (int)Math.Round(value * _scale); }

        // The page is its own HWND in another process, so form key handlers never
        // see keystrokes typed over it. A hot key registered while this window is
        // active gives the settings menu a keyboard route anyway; it is released
        // on deactivate so it never steals the combination from other apps.
        private const int HotKeySettings = 0xA17;
        private bool _hotKeyRegistered;

        private void UpdateHotKey(bool active)
        {
            try
            {
                if (active && !_hotKeyRegistered)
                {
                    // MOD_CONTROL | MOD_NOREPEAT, VK_OEM_COMMA
                    _hotKeyRegistered = NativeMethods.RegisterHotKey(Handle, HotKeySettings, 0x0002 | 0x4000, 0xBC);
                    App.Log("settings hotkey registered: " + _hotKeyRegistered);
                }
                else if (!active && _hotKeyRegistered)
                {
                    NativeMethods.UnregisterHotKey(Handle, HotKeySettings);
                    _hotKeyRegistered = false;
                }
            }
            catch { }
        }

        private void BuildTitleBar()
        {
            _title = new Panel();
            _title.Dock = DockStyle.Top;
            _title.Height = _chromeHeight;
            _title.BackColor = _chromeRight;
            _title.Paint += delegate(object sender, PaintEventArgs e) { PaintChrome(e.Graphics); };
            Controls.Add(_title);

            _titleIcon = new PictureBox();
            _titleIcon.SizeMode = PictureBoxSizeMode.Zoom;
            _titleIcon.BackColor = _chromeLeft;
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream("dsh_default.ico"))
                {
                    if (stream != null)
                    {
                        using (Icon icon = new Icon(stream, new Size(S(16), S(16))))
                            _titleIcon.Image = icon.ToBitmap();
                    }
                }
            }
            catch { }
            _title.Controls.Add(_titleIcon);

            _titleText = new Label();
            _titleText.Text = Strings.T("app.title");
            _titleText.AutoSize = false;
            _titleText.TextAlign = ContentAlignment.MiddleLeft;
            _titleText.Font = new Font("Microsoft YaHei UI", 9f);
            _titleText.BackColor = _chromeLeft;
            _title.Controls.Add(_titleText);

            _btnSettings = new ChromeButton(ChromeKind.Settings);
            _btnSettings.Click += delegate { ShowSettings(); };
            _btnMenu = new ChromeButton(ChromeKind.Menu);
            _btnMenu.Click += delegate(object sender, EventArgs e) { ShowMenu(_btnMenu); };
            _btnMin = new ChromeButton(ChromeKind.Minimize);
            _btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            _btnMax = new ChromeButton(ChromeKind.Maximize);
            _btnMax.Click += delegate { ToggleMaximize(); };
            _btnClose = new ChromeButton(ChromeKind.Close);
            _btnClose.Click += delegate { Close(); };

            _title.Controls.Add(_btnSettings);
            _title.Controls.Add(_btnMenu);
            _title.Controls.Add(_btnMin);
            _title.Controls.Add(_btnMax);
            _title.Controls.Add(_btnClose);

            // The whole strip is a drag handle, like a native caption. On Windows 11 the
            // top edge has no non-client frame (WM_NCCALCSIZE pushed the client area to
            // the very top), so the top resize zone lives here instead: the strip is an
            // in-process control, so the hit test is ours to make. Everything else drags.
            MouseEventHandler drag = delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                IntPtr edge = IntPtr.Zero;
                if (NativeMethods.Windows11 && WindowState == FormWindowState.Normal)
                {
                    int zone = S(6);
                    bool left = e.X <= zone, right = e.X >= _title.ClientSize.Width - zone;
                    if (e.Y <= zone)
                    {
                        if (left) edge = (IntPtr)13;         // HTTOPLEFT
                        else if (right) edge = (IntPtr)14;   // HTTOPRIGHT
                        else edge = (IntPtr)12;              // HTTOP
                    }
                    else if (left && e.Y <= S(12)) edge = (IntPtr)13;   // top-left corner zone
                    else if (right && e.Y <= S(12)) edge = (IntPtr)14;  // top-right corner zone
                }
                NativeMethods.ReleaseCapture();
                // WM_NCLBUTTONDOWN enters a modal sizing loop inside DefWindowProc, so the
                // Chromium redraw lock (dropping WS_VISIBLE for the duration of the call)
                // must NOT wrap it: the whole drag would run on a window the system
                // considers invisible, leaving the newly grown client area unpainted -
                // the black band the user saw. Plain SendMessage only.
                NativeMethods.SendMessage(Handle, 0x00A1, edge != IntPtr.Zero ? edge : (IntPtr)2, IntPtr.Zero);
            };
            _title.MouseDown += drag;
            _titleText.MouseDown += drag;
            _titleIcon.MouseDown += drag;
            // Windows 11: the top edge is client area, and hover cursors come from the
            // window that actually owns the pixel (the system never asks our hit test
            // for it - verified by logging, and the CEFSharp crowd hit the same wall).
            // A 6px strip with Cursor=SizeNS makes the resize arrow appear natively,
            // and its MouseDown rides the same drag handler, so clicking it resizes.
            if (NativeMethods.Windows11)
            {
                _topResizeBar = new Panel();
                _topResizeBar.SetBounds(0, 0, _title.ClientSize.Width, Math.Max(4, S(12)));
                _topResizeBar.Cursor = Cursors.SizeNS;
                _topResizeBar.BackColor = _chromeLeft;   // painted over by PaintChrome's split
                _topResizeBar.MouseDown += drag;
                _title.Controls.Add(_topResizeBar);
                _topResizeBar.BringToFront();
            }
            _title.DoubleClick += delegate { ToggleMaximize(); };
            _titleText.DoubleClick += delegate { ToggleMaximize(); };
            _title.Resize += delegate { LayoutTitle(); };
            LayoutTitle();
        }

        // Two bands, because the page's left rail and its content surface are
        // different colours. Splitting the bar where the page splits keeps the
        // seam invisible in both themes.
        private void PaintChrome(Graphics g)
        {
            if (_title == null) return;
            int width = _title.ClientSize.Width;
            int height = _title.ClientSize.Height;
            int split = Math.Max(0, Math.Min(width, _chromeSplit));
            using (SolidBrush left = new SolidBrush(_chromeLeft))
                g.FillRectangle(left, 0, 0, split, height);
            using (SolidBrush right = new SolidBrush(_chromeRight))
                g.FillRectangle(right, split, 0, width - split, height);
        }

        private void LayoutTitle()
        {
            if (_title == null) return;
            _title.Height = _chromeHeight;
            int pad = S(10);
            int iconSize = S(18);
            int buttonWidth = S(46);
            int right = _title.ClientSize.Width;

            _btnClose.SetBounds(right - buttonWidth, 0, buttonWidth, _chromeHeight);
            _btnMax.SetBounds(right - buttonWidth * 2, 0, buttonWidth, _chromeHeight);
            _btnMin.SetBounds(right - buttonWidth * 3, 0, buttonWidth, _chromeHeight);
            _btnMenu.SetBounds(right - buttonWidth * 3 - S(38), 0, S(38), _chromeHeight);
            _btnSettings.SetBounds(right - buttonWidth * 3 - S(76), 0, S(38), _chromeHeight);

            _titleIcon.SetBounds(pad, (_chromeHeight - iconSize) / 2, iconSize, iconSize);
            int textLeft = pad + iconSize + S(10);
            _titleText.SetBounds(textLeft, 0, Math.Max(0, _btnSettings.Left - textLeft - S(8)), _chromeHeight);

            _btnMax.Kind = WindowState == FormWindowState.Maximized ? ChromeKind.Restore : ChromeKind.Maximize;
            foreach (ChromeButton button in new ChromeButton[] { _btnSettings, _btnMenu, _btnMin, _btnMax, _btnClose })
                button.SetGlyphScale(_scale);

            // The top resize strip spans the title bar and stays on top of its siblings.
            if (_topResizeBar != null)
                _topResizeBar.SetBounds(0, 0, _title.ClientSize.Width, Math.Max(4, S(12)));
        }

        private void ApplyChrome()
        {
            _dark = App.IsDark(_chromeRight);
            _theme = Theme.FromPage(_dark, _chromeRight);
            Color symbolControls = _dark ? Color.FromArgb(249, 250, 251) : Color.FromArgb(15, 17, 21);
            Color symbolLeft = App.IsDark(_chromeLeft) ? Color.FromArgb(249, 250, 251) : Color.FromArgb(15, 17, 21);
            Color titleColor = _dark ? Color.FromArgb(196, 200, 206) : Color.FromArgb(72, 78, 88);

            if (_title != null)
            {
                _title.BackColor = _chromeRight;
                _title.Invalidate();
            }
            // The resize strip sits over the caption and must follow its colour,
            // otherwise it would read as a foreign band.
            if (_topResizeBar != null) _topResizeBar.BackColor = _chromeRight;
            if (_titleText != null)
            {
                _titleText.ForeColor = titleColor;
                _titleText.BackColor = _chromeLeft;
            }
            if (_titleIcon != null) _titleIcon.BackColor = _chromeLeft;

            foreach (ChromeButton button in new ChromeButton[] { _btnSettings, _btnMenu, _btnMin, _btnMax, _btnClose })
            {
                if (button != null) button.SetTheme(_dark, _chromeRight, symbolControls);
            }

            // Windows 11 has no painted rail: the content reaches the edge and DWM
            // draws the rounded frame and soft shadow. Windows 10 keeps the
            // dshview-client look: a 1px gray border around the window, no rounding.
            if (NativeMethods.Windows11)
            {
                BackColor = _chromeRight;
                Padding = new Padding(0);
            }
            else
            {
                BackColor = _dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(200, 200, 200);
                Padding = new Padding(1);
            }

            if (_splash != null)
            {
                _splash.BackColor = _chromeRight;
                _splash.ForeColor = symbolLeft;
            }
            if (_bar != null)
            {
                _bar.BackColor = _dark ? Color.FromArgb(32, 33, 36) : Color.FromArgb(246, 247, 249);
                if (_addressCell != null)
                {
                    _addressCell.BackColor = _dark ? Color.FromArgb(44, 46, 50) : Color.White;
                    if (_address != null)
                    {
                        _address.BackColor = _addressCell.BackColor;
                        _address.ForeColor = _dark ? Color.FromArgb(238, 241, 245) : Color.FromArgb(27, 29, 34);
                        if (_addressCell != null) _addressCell.Invalidate();
                        App.Log("bar theme: cell=" + _addressCell.BackColor + " box=" + _address.BackColor
                            + " visible=" + _address.Visible + " bounds=" + _address.Bounds
                            + " cellBounds=" + _addressCell.Bounds + " bar=" + _bar.BackColor + " dark=" + _dark);
                    }
                }
                foreach (Control control in _bar.Controls)
                {
                    control.ForeColor = symbolControls;
                    GlyphButton glyph = control as GlyphButton;
                    if (glyph != null) glyph.SetTheme(_dark, symbolControls);
                }
            }
            Invalidate();
        }

        // The page is the only reliable source for its own colours: read the
        // surface behind the bar at both ends, find where it changes, and paint
        // the bar to match. Falls back to the system theme until the page paints.
        private FileSystemWatcher _themeFiles;


        // DSH keeps its theme preference in the profile folder. Watching that file is
        // event driven and catches flips that never touch the DOM the observer sees.
        private void StartThemeFileWatch(string profilesDir)
        {
            try
            {
                if (_themeFiles != null || !Directory.Exists(profilesDir)) return;
                _themeFiles = new FileSystemWatcher(profilesDir, "*.yml");
                // the file lives one level down, in profiles\web
                _themeFiles.IncludeSubdirectories = true;
                _themeFiles.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                    | NotifyFilters.Size | NotifyFilters.CreationTime;
                FileSystemEventHandler onWrite = delegate
                {
                    App.Log("theme file changed: resampling");
                    ResampleTheme(400);
                };
                _themeFiles.Changed += onWrite;
                _themeFiles.Created += onWrite;
                _themeFiles.Renamed += delegate { ResampleTheme(400); };
                _themeFiles.EnableRaisingEvents = true;
                App.Log("theme file watch on " + profilesDir);
            }
            catch (Exception ex) { App.Log("theme file watch failed: " + ex.Message); }
        }

        // A one-shot extra sample: cheap, and it covers the gap between the page
        // announcing a change and the new colours actually being painted.
        private void ResampleTheme(int delayMs)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { ResampleTheme(delayMs); }); }
                catch { }
                return;
            }
            System.Windows.Forms.Timer once = new System.Windows.Forms.Timer();
            once.Interval = delayMs;
            once.Tick += delegate
            {
                once.Stop();
                once.Dispose();
                SyncThemeFromPage();
            };
            once.Start();
        }

        private async void SyncThemeFromPage()
        {
            if (_view == null || _view.CoreWebView2 == null) return;
            try
            {
                const string script =
                    "(function(){try{" +
                    "function opaque(el){" +
                    "while(el){var c=getComputedStyle(el).backgroundColor;" +
                    "if(c){var m=c.match(/rgba?\\(([\\d.]+),\\s*([\\d.]+),\\s*([\\d.]+)(?:,\\s*([\\d.]+))?\\)/);" +
                    "if(m&&(m[4]===undefined||parseFloat(m[4])>=0.99))return c;}" +
                    "el=el.parentElement;}" +
                    "return '';}" +
                    "function at(x,y){var el=document.elementFromPoint(x,y);" +
                    "if(el&&el.closest&&el.closest('#dsh-desktop-scrim'))return 'scrim';" +
                    "return el?opaque(el):'';}" +
                    "var W=window.innerWidth,H=window.innerHeight,y=Math.min(8,H-1);" +
                    "var L=at(4,y),R=at(W-4,y);" +
                    "var split=W;" +
                    "if(L&&R&&L!==R){var lo=4,hi=W-4;" +
                    "for(var i=0;i<14&&hi-lo>1;i++){var mid=(lo+hi)>>1;if(at(mid,y)===L)lo=mid;else hi=mid;}" +
                    "split=hi;}" +
                    "return [L,R,split,getComputedStyle(document.body).backgroundColor," +
                    "(document.documentElement.getAttribute('lang')||navigator.language||'')].join('|');" +
                    "}catch(e){return ''}})()";
                string result = await _view.CoreWebView2.ExecuteScriptAsync(script);
                if (string.IsNullOrEmpty(result)) return;
                result = result.Trim();
                if (result.Length >= 2 && result[0] == '"' && result[result.Length - 1] == '"')
                    result = result.Substring(1, result.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
                string[] parts = result.Split('|');
                if (parts.Length < 4) return;

                // Keep the launcher wording in the language the page picked.
                if (parts.Length > 4 && Strings.SetFromPage(parts[4]))
                    App.Log("language from page: " + Strings.L + " (" + parts[4] + ")");

                Color left = App.ParseCssColor(parts[0]);
                Color right = App.ParseCssColor(parts[1]);
                int splitCss;
                int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out splitCss);
                if (left.IsEmpty && right.IsEmpty)
                {
                    Color body = App.ParseCssColor(parts[3]);
                    if (body.IsEmpty) return;
                    left = body;
                    right = body;
                }
                else if (left.IsEmpty) left = right;
                else if (right.IsEmpty) right = left;

                // The dim overlay of a dialog is not chrome, so those samples are
                // dropped. A theme that paints the whole page one colour is adopted:
                // the bar simply loses its seam.
                if (parts[0] == "scrim" || parts[1] == "scrim") return;

                double zoom = 1.0;
                try { zoom = _view.ZoomFactor; } catch { }
                if (zoom <= 0.1) zoom = 1.0;

                int split = _chromeSplit;
                if (left != right && splitCss > 0 && splitCss < int.MaxValue)
                    split = (int)Math.Round(splitCss * _scale * zoom);

                if (left == _chromeLeft && right == _chromeRight && split == _chromeSplit) return;
                _chromeLeft = left;
                _chromeRight = right;
                _chromeSplit = split;
                ApplyChrome();
                App.Log("chrome colour from page: left=" + left.R + "," + left.G + "," + left.B
                    + " right=" + right.R + "," + right.G + "," + right.B
                    + " split=" + split.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { App.Log("theme probe failed: " + ex.Message); }
        }

        // Diagnostic: proves the page-to-native theme channel is live and how fast
        // it is, without needing a human to flip the theme by hand.
        private async void ProbeThemeWatch()
        {
            try
            {
                App.Log("theme probe: sending three page messages");
                _view.CoreWebView2.ExecuteScriptAsync(
                    "try{window.chrome.webview.postMessage('dsh-desktop:theme');"
                    + "setTimeout(function(){window.chrome.webview.postMessage('dsh-desktop:theme');},600);"
                    + "setTimeout(function(){window.chrome.webview.postMessage('dsh-desktop:theme');},1200);}catch(e){}");

                // Also dump the radius scale the page itself uses, so the launchar
                // panels can match it instead of guessing.
                const string radii =
                    "(function(){try{var s=getComputedStyle(document.documentElement);"
                    + "var out=[];['sm','md','lg','xl','panel','full'].forEach(function(k){"
                    + "out.push(k+'='+s.getPropertyValue('--dsw-radius-'+k).trim());});"
                    + "return out.join(' ');}catch(e){return 'probe failed';}})()";
                string value = await _view.CoreWebView2.ExecuteScriptAsync(radii);
                App.Log("radius probe: " + value);
            }
            catch (Exception ex) { App.Log("theme probe failed: " + ex.Message); }
        }

        // Runs in every document, so the watcher survives navigation and reloads.
        // Everything here is event driven: no polling timer, the observer and the
        // media query only speak up when the page actually changes.
        private const string ThemeWatchScript =
            "(function(){try{" +
            "if(window.__dshDesktopThemeWatch)return;" +
            "var last='';" +
            // Compare the colours the page actually paints with, not only its
            // attributes: DSH flips themes by swapping variables, which does not
            // always touch data-theme or the class list.
            // Any change in the observed places is worth a fresh sample: DSH swaps
            // variables without necessarily changing what html/body paint with, so
            // comparing colours here would keep quiet exactly when it matters.
            "var pending=false;" +
            "function notify(){window.__dshPokes=(window.__dshPokes|0)+1+0;if(pending)return;pending=true;" +
            "setTimeout(function(){pending=false;" +
            "try{window.chrome.webview.postMessage(\"dsh-desktop:theme\");}catch(e){}},150);}" +
            // The document created hook fires before there is a documentElement, so
            // the observers have to wait for the DOM.
            "function install(){try{if(window.__dshDesktopThemeWatch)return;" +
            "var r=document.documentElement;var mo=new MutationObserver(notify);" +
            "if(r)mo.observe(r,{attributes:true,attributeFilter:[\"data-theme\",\"class\",\"style\",\"lang\",\"data-color-scheme\"]});" +
            "if(document.body)mo.observe(document.body,{attributes:true,childList:true,attributeFilter:[\"class\",\"data-theme\",\"style\"]});" +
            "if(document.head)mo.observe(document.head,{childList:true,subtree:true,characterData:true,attributes:true});" +
            "try{window.matchMedia(\"(prefers-color-scheme: dark)\").addEventListener(\"change\",notify);}catch(e){}" +
            "window.__dshDesktopThemeWatch=true;notify();}catch(e){}}" +
            "if(document.readyState===\"loading\")document.addEventListener(\"DOMContentLoaded\",install,{once:true});" +
            "else install();" +
            "}catch(e){}})();";

        private void StartThemeWatch()
        {
            // Ask the page to tell us when its theme flips, so the bar follows at
            // once. Nothing is polled: the page speaks up when it changes, and a
            // navigation or a window activation asks for one extra sample.
            try
            {
                _view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ThemeWatchScript);
                _view.CoreWebView2.ExecuteScriptAsync(ThemeWatchScript);
            }
            catch (Exception ex) { App.Log("theme watch failed: " + ex.Message); }
        }

        private void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            else
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }
            ApplyChrome();
            LayoutTitle();
            RoundShell();
        }

        private void Launch()
        {

            if (!_s.Server)
            {
                // Client only: nothing to start, go straight to the window.
                App.Log("client mode, target " + _s.Url);
                _entryUrl = _s.Url;
                BeginInit();
                return;
            }

            Thread thread = new Thread(delegate()
            {
                // The packaged folder ships without dsh: the first launch fetches it
                // here, before anything tries to start the server.
                if (!Maintenance.HasApp())
                {
                    SetSplashText(Strings.T("splash.install"));
                    try { BeginInvoke((MethodInvoker)delegate { BeginInstallProgress(); }); }
                    catch { }
                    bool ready = Maintenance.EnsureApp(delegate(string line) { App.Log(line); });
                    try { BeginInvoke((MethodInvoker)delegate { EndInstallProgress(); }); }
                    catch { }
                    if (!ready)
                    {
                        try { BeginInvoke((MethodInvoker)delegate { OnInstallFailed(); }); }
                        catch { }
                        return;
                    }
                    SetSplashText(Strings.T("splash.ready"));
                }


                DshServer server = new DshServer(_s);
                bool ok = server.Start();
                try { BeginInvoke((MethodInvoker)delegate { OnServerReady(server, ok); }); }
                catch { }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        // Where the bar and its caption sit: under the middle of the splash text.
        private void LayoutSplashProgress()
        {
            if (_splashBar == null || _splashSub == null) return;
            int total = Math.Min(S(520), Math.Max(S(260), ClientSize.Width - S(120)));
            int caption = S(130);
            int gap = S(12);
            int barWidth = Math.Max(S(120), total - caption - gap);
            int left = (ClientSize.Width - total) / 2;
            int top = (int)(ClientSize.Height * 0.62);
            _splashBar.SetBounds(left, top, barWidth, S(20));
            // The count sits beside the bar rather than under it.
            _splashSub.SetBounds(left + barWidth + gap, top + S(1), caption, S(20));
            _splashSub.TextAlign = ContentAlignment.MiddleLeft;
            if (_splash != null) _splashSub.BackColor = _splash.BackColor;
        }

        // Sizes on disk are the only progress signal pnpm gives us, so the bar is
        // driven by how much has landed in the install folders.
        private void BeginInstallProgress()
        {
            if (_installWatch != null) return;
            try
            {
                _installPercent = 0;
                _installBytesMax = 0;
                if (_splashBar != null)
                {
                    _splashBar.Value = 0;
                    _splashBar.Visible = true;
                    _splashBar.BringToFront();
                }
                if (_splashSub != null)
                {
                    _splashSub.Text = Strings.T("splash.install.preparing");
                    _splashSub.Visible = true;
                    _splashSub.BringToFront();
                }
                LayoutSplashProgress();
                _installWatch = new System.Windows.Forms.Timer();
                _installWatch.Interval = 400;
                _installWatch.Tick += delegate { UpdateInstallProgress(); };
                _installWatch.Start();
            }
            catch (Exception ex) { App.Log("install progress failed: " + ex.Message); }
        }

        private long InstallBytes()
        {
            long total = 0;
            string[] roots = new string[]
            {
                System.IO.Path.Combine(App.ResolveDir(_s.Data, "data"), "update-staging"),
                System.IO.Path.Combine(App.ResolveDir(_s.AppDir, "app"), "node_modules")
            };
            foreach (string root in roots)
            {
                try
                {
                    if (!System.IO.Directory.Exists(root)) continue;
                    foreach (string file in System.IO.Directory.GetFiles(root, "*", System.IO.SearchOption.AllDirectories))
                    {
                        try { total += new System.IO.FileInfo(file).Length; } catch { }
                    }
                }
                catch { }
            }
            return total;
        }

        private void UpdateInstallProgress()
        {
            try
            {
                long bytes = InstallBytes();
                // A full tree before pruning is about 760MB; cap at 95 so the bar only
                // reaches the end when the install really returned.
                long target = 760L * 1024 * 1024;
                int percent = (int)Math.Min(95, bytes * 100 / target);
                // The tree shrinks when the payload is pruned, so the bar must never
                // walk backwards.
                if (percent > _installPercent) _installPercent = percent;
                // The tree shrinks when the payload is pruned, so the size shown only
                // ever grows: 740MB must not fall back to 290MB mid-install.
                if (bytes > _installBytesMax) _installBytesMax = bytes;
                if (_splashBar != null) _splashBar.Value = _installPercent;
                if (_splashSub != null)
                    _splashSub.Text = _installBytesMax <= 0
                        ? Strings.T("splash.install.preparing")
                        : string.Format(CultureInfo.InvariantCulture, Strings.T("splash.install.progress"),
                            _installPercent, _installBytesMax / 1048576);
            }
            catch { }
        }

        private void EndInstallProgress()
        {
            try
            {
                if (_installWatch != null)
                {
                    _installWatch.Stop();
                    _installWatch.Dispose();
                    _installWatch = null;
                }
                if (_splashBar != null)
                {
                    _splashBar.Value = 100;
                    _splashBar.Visible = false;
                }
                if (_splashSub != null) _splashSub.Visible = false;
            }
            catch { }
        }

        private void SetSplashText(string text)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    if (_splash != null && !_splash.IsDisposed) _splash.Text = text;
                });
            }
            catch { }
        }

        // dsh could not be installed: no network, or npm was unreachable.
        private void OnInstallFailed()
        {
            _finished = true;
            string detail = Strings.T("splash.install.failed") + Environment.NewLine + Environment.NewLine + App.LogFile;
            if (_splash != null) _splash.Text = detail;
            App.Log("dsh install failed; see the log");
            if (_s.SelfTest) { FinishSelfTest(false, "dsh install failed"); return; }
            MessageBox.Show(this, detail + Environment.NewLine + Environment.NewLine + Strings.T("splash.install.hint"),
                "DSH Desktop", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void OnServerReady(DshServer server, bool ok)
        {
            _server = server;
            if (!ok)
            {
                string detail = server.LastError ?? "?";
                string tail = server.Tail;
                App.Log("startup failed: " + detail);
                try { server.Dispose(); } catch { }
                _server = null;
                if (_splash != null) _splash.Text = detail;
                if (_s.SelfTest) { FinishSelfTest(false, detail); return; }
                MessageBox.Show(this, detail
                    + (string.IsNullOrWhiteSpace(tail) ? "" : Environment.NewLine + Environment.NewLine + tail)
                    + Environment.NewLine + Environment.NewLine + App.LogFile,
                    "DSH Desktop", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            App.Log("server ready: " + server.EntryUrl);
            _entryUrl = server.EntryUrl;


            BeginInvoke((MethodInvoker)delegate { BeginInit(); });
        }

        private void BuildToolbar()
        {
            _bar = new Panel();
            _bar.Dock = DockStyle.Top;
            _bar.Height = S(40);
            _bar.BackColor = Color.FromArgb(246, 247, 249);
            Controls.Add(_bar);

            _back = MakeButton("\u2190", "Alt+Left");
            _forward = MakeButton("\u2192", "Alt+Right");
            _reload = MakeButton("\u27F3", "F5");
            _home = MakeButton("\u2302", "Home");
            _zoomOut = MakeButton("\u2212", "Ctrl+-");
            _zoomIn = MakeButton("+", "Ctrl++");

            _back.Click += delegate { GoBack(); };
            _forward.Click += delegate { GoForward(); };
            _reload.Click += delegate { Reload(); };
            _home.Click += delegate { Navigate(ResolveOwner()); };
            _zoomOut.Click += delegate { SetZoom(-0.1); };
            _zoomIn.Click += delegate { SetZoom(0.1); };

            // The address field sits in a rounded cell like the settings inputs.
            _addressCell = new Panel();
            _addressCell.BackColor = Color.White;
            _addressCell.Paint += delegate(object sender, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Clear with the bar colour and fill only inside the rounded shape:
                // a square panel background under a rounded border showed as a
                // second box around the address field.
                if (_bar != null)
                {
                    using (SolidBrush back = new SolidBrush(_bar.BackColor))
                        g.FillRectangle(back, _addressCell.ClientRectangle);
                }
                Rectangle r = new Rectangle(0, 0, _addressCell.ClientSize.Width - 1, _addressCell.ClientSize.Height - 1);
                using (GraphicsPath path = Theme.Rounded(r, S(8)))
                {
                    using (SolidBrush fill = new SolidBrush(_addressCell.BackColor))
                        g.FillPath(fill, path);
                    // The URL is drawn here, inside the rounded shape: a child control
                    // would have square corners to show at the ends of the field.
                    if (!_addressEditing && _address != null && !_address.Visible)
                    {
                        Rectangle textArea = new Rectangle(S(11), 0, _addressCell.ClientSize.Width - S(22), _addressCell.ClientSize.Height);
                        TextRenderer.DrawText(g, _addressValue ?? "", _address.Font, textArea,
                            _dark ? Color.FromArgb(238, 241, 245) : Color.FromArgb(27, 29, 34),
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                            | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                            | TextFormatFlags.NoPrefix);
                    }
                    using (Pen pen = new Pen(_dark ? Color.FromArgb(74, 77, 84) : Color.FromArgb(216, 221, 229)))
                        g.DrawPath(pen, path);
                }
            };

            _addressCell.Cursor = Cursors.IBeam;
            _addressCell.Click += delegate { BeginAddressEdit(); };

            _address = new TextBox();
            _address.BorderStyle = BorderStyle.None;
            _address.BackColor = _addressCell.BackColor;
            _address.ForeColor = _dark ? Color.FromArgb(238, 241, 245) : Color.FromArgb(27, 29, 34);
            _address.Visible = false;
            _address.Leave += delegate { EndAddressEdit(); };
            _address.Font = new Font("Microsoft YaHei UI", 9f);
            _address.BackColor = Color.White;
            _address.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Navigate(_address.Text.Trim());
                }
            };

            _zoomLabel = new Label();
            _zoomLabel.Text = "100%";
            _zoomLabel.TextAlign = ContentAlignment.MiddleCenter;
            _zoomLabel.Font = new Font("Segoe UI", 8.5f);
            _zoomLabel.BackColor = Color.Transparent;

            _bar.Controls.Add(_back);
            _bar.Controls.Add(_forward);
            _bar.Controls.Add(_reload);
            _bar.Controls.Add(_home);
            _bar.Controls.Add(_addressCell);
            _bar.Controls.Add(_zoomOut);
            _bar.Controls.Add(_zoomLabel);
            _bar.Controls.Add(_zoomIn);
            _bar.Resize += delegate { LayoutBar(_bar); };
            App.Log("bar built: cell=" + _addressCell.BackColor + " box=" + _address.BackColor
                + " boxVisible=" + _address.Visible + " boxBounds=" + _address.Bounds
                + " cellBounds=" + _addressCell.Bounds + " bar=" + _bar.BackColor);
        }

        // The tray icon keeps the process alive with no window and no taskbar
        // button (a hidden top-level window drops out of the taskbar by itself,
        // so ShowInTaskbar is left alone -- toggling it would recreate the HWND
        // and tear down the WebView2 controller).
        // Settings that no longer exist are dropped from an existing ini once.
        private void DropRetiredIniKeys()
        {
            try
            {
                if (!System.IO.File.Exists(App.IniPath)) return;
                IniFile file = IniFile.Load(App.IniPath);
                bool had = file.Get("ui", null) != null || file.Get("theme", null) != null;
                if (!had) return;
                file.Remove("ui");
                file.Remove("theme");
                file.Save(App.IniPath);
                App.Log("removed retired ini keys (ui, theme)");
            }
            catch (Exception ex) { App.Log("ini cleanup failed: " + ex.Message); }
        }

        // The WebView2's Chromium window re-takes focus the instant the tray menu opens,
        // which dismisses the menu via AppFocusChange. Clear the WinForms active control
        // and also move the Chromium focus out (Next) so it cannot grab focus back.
        // True while this window, or a window inside it (the WebView2 is one), owns the
        // foreground: the way to tell the WebView2's self-inflicted focus bounce apart
        // from the user really switching to another application.
        private bool OwnForeground()
        {
            try
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero) return false;
                if (foreground == Handle) return true;
                return NativeMethods.GetAncestor(foreground, 2) == Handle;   // GA_ROOT
            }
            catch { return false; }
        }

        // Chromium's ScopedRedrawLock, verbatim: Windows sometimes paints the default
        // non-client frame right over a custom frame from inside DefWindowProc's
        // handling of WM_NCACTIVATE / WM_NCLBUTTONDOWN / WM_SETTEXT / WM_SETICON -
        // handling WM_NCPAINT does not help because the painting is not done there.
        // Temporarily dropping WS_VISIBLE stops the system from painting at all for
        // the duration of the DefWindowProc call.
        private void DefWindowProcLocked(int msg, IntPtr wParam, IntPtr lParam)
        {
            const int GWL_STYLE = -16;
            const int WS_VISIBLE = 0x10000000;
            int style = NativeMethods.GetWindowLong(Handle, GWL_STYLE);
            NativeMethods.SetWindowLong(Handle, GWL_STYLE, style & ~WS_VISIBLE);
            try
            {
                NativeMethods.DefWindowProc(Handle, msg, wParam, lParam);
            }
            finally
            {
                NativeMethods.SetWindowLong(Handle, GWL_STYLE, style | WS_VISIBLE);
            }
        }

        private void MoveFocusOffWebView2()
        {
            try { ActiveControl = null; } catch { }
            try
            {
                if (_view == null) return;
                var f = typeof(Microsoft.Web.WebView2.WinForms.WebView2)
                    .GetField("_coreWebView2Controller", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (f == null) return;
                var ctrl = f.GetValue(_view);
                if (ctrl == null) return;
                var m = ctrl.GetType().GetMethod("MoveFocus");
                if (m != null) m.Invoke(ctrl, new object[] { 1 }); // CoreWebView2MoveFocusReason.Next
            }
            catch { }
        }

        private void SetupTray()
        {
            _tray = new NotifyIcon();
            try { _tray.Icon = App.PickThemeIcon(_s.Icon); }
            catch { _tray.Icon = SystemIcons.Application; }
            _tray.Text = "DSH Desktop";
            _tray.Visible = true;

            RoundedMenu menu = new RoundedMenu(S(6));
            // The tray popup is over the desktop, so it closes on deactivate; the click-away
            // timer that serves the "more" menu only disturbed its re-show, so skip it here.
            menu.NoAutoClose = true;
            // Windows 10 only: NotifyIcon.ShowContextMenu() hands the foreground to the
            // icon window before the popup shows (the shell hack from KB135788), and the
            // WebView2 takes the foreground straight back, so the popup dies on
            // AppFocusChange the instant it opens. Swallow exactly that bounce: only when
            // the foreground still belongs to this window (the WebView2 is a child of it),
            // never when the user really switched to another application. The polling
            // timer takes over the click-away path that the swallowed close would have
            // served. Windows 11 does not bounce like this, so it keeps the original path.
            if (!NativeMethods.Windows11)
            {
                menu.NoAutoClose = false;
                menu.Closing += delegate(object s, ToolStripDropDownClosingEventArgs e)
                {
                    if (e.CloseReason == ToolStripDropDownCloseReason.AppFocusChange && OwnForeground())
                        e.Cancel = true;
                };
            }
            Theme theme = _theme ?? Theme.FromPage(_dark, _chromeRight);
            // The renderer is OS-aware: rounded + system shadow on Windows 11, square
            // with a 1px gray frame on Windows 10. The background follows the theme.
            MenuRenderer renderer = new MenuRenderer(theme, _scale);
            renderer.Border = true;
            menu.Renderer = renderer;
            menu.BackColor = theme.Dark ? theme.MenuSurface : Color.White;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            if (NativeMethods.Windows11)
            {
                menu.Font = new Font("Microsoft YaHei UI", 9f);
                menu.Padding = new Padding(S(7));
            }
            menu.Items.Add(Strings.T("tray.show"), null, delegate { RestoreFromTray(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Strings.T("menu.settings"), null, delegate { ShowSettings(); });
            menu.Items.Add(Strings.T("menu.about"), null, delegate
            {
                // The dialog is owned by the window, so bring it back first.
                RestoreFromTray();
                ShowAbout();
            });
            menu.Items.Add(Strings.T("tray.quit"), null, delegate { ExitFromTray(); });
            // The menu outlives language and theme changes, so its labels and colours
            // are refreshed right before it opens.
            menu.Opening += delegate
            {
                App.Log("tray menu Opening");
                MoveFocusOffWebView2();
                SyncTrayMenu(menu);
            };
            menu.Opened += delegate { App.Log("tray menu Opened, bounds=" + menu.Bounds); };
            menu.Closing += delegate(object s, ToolStripDropDownClosingEventArgs e)
            {
                App.Log("tray menu Closing, reason=" + e.CloseReason);
            };
            menu.Closed += delegate(object s, ToolStripDropDownClosedEventArgs e)
            {
                App.Log("tray menu Closed, reason=" + e.CloseReason);
            };
            _trayMenu = menu;
            if (_tray != null) _tray.ContextMenuStrip = menu;
            foreach (ToolStripItem entry in menu.Items)
            {
                entry.ForeColor = theme.Text;
                int textH = TextRenderer.MeasureText("Ag", entry.Font ?? SystemFonts.MenuFont).Height;
                int padY = Math.Max(S(4), (Theme.MenuRowHeight(_scale) - textH) / 2);
                entry.Padding = new Padding(0, padY, 0, padY);
            }
            // Let the strip hug its own content: a forced width is what made the tray
            // menu far wider than its labels.
            menu.AutoSize = true;
            menu.MinimumSize = new Size(S(120), 0);
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { RestoreFromTray(); };
            App.Log("tray icon created");
        }

        // Called just before the tray menu opens: the labels follow the launcher
        // language and the row colours follow the current theme.
        private void SyncTrayMenu(ContextMenuStrip menu)
        {
            Theme theme = _theme ?? Theme.FromPage(_dark, _chromeRight);
            MenuRenderer renderer = new MenuRenderer(theme, _scale);
            renderer.Border = true;
            menu.Renderer = renderer;
            menu.BackColor = theme.Dark ? theme.MenuSurface : Color.White;
            if (menu.Items.Count >= 5)
            {
                menu.Items[0].Text = Strings.T("tray.show");
                menu.Items[2].Text = Strings.T("menu.settings");
                menu.Items[3].Text = Strings.T("menu.about");
                menu.Items[4].Text = Strings.T("tray.quit");
            }
            foreach (ToolStripItem entry in menu.Items)
            {
                entry.ForeColor = theme.Text;
                int textH = TextRenderer.MeasureText("Ag", entry.Font ?? SystemFonts.MenuFont).Height;
                int padY = Math.Max(S(4), (Theme.MenuRowHeight(_scale) - textH) / 2);
                entry.Padding = new Padding(0, padY, 0, padY);
            }
            // Let the strip hug its own content: a forced width is what made the tray
            // menu far wider than its labels.
            menu.AutoSize = true;
            menu.MinimumSize = new Size(S(120), 0);
        }

        private void HideToTray()
        {
            if (_tray == null) SetupTray();
            SaveLayout();
            _stateBeforeHide = WindowState;
            Hide();
            App.Log("hidden to tray");

            if (!_trayHintShown)
            {
                _trayHintShown = true;
                try
                {
                    _tray.BalloonTipTitle = "DSH Desktop";
                    _tray.BalloonTipText = Strings.Current == Lang.Zh
                        ? "已最小化到托盘，服务继续运行。双击图标恢复，右键可退出。"
                        : "Minimised to the tray; the server keeps running. Double-click to restore, right-click to quit.";
                    _tray.ShowBalloonTip(4000);
                }
                catch { }
            }
        }

        private void RestoreFromTray()
        {
            Show();
            if (_stateBeforeHide == FormWindowState.Maximized) WindowState = FormWindowState.Maximized;
            else if (_stateBeforeHide == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            App.Log("restored from tray");
        }

        private void ExitFromTray()
        {
            _exiting = true;
            App.Log("exit requested from tray");
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            Close();
            Application.Exit();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            SaveLayout();

            if (!_exiting && _s.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            // Stopping the server here (instead of in Dispose) keeps the port free
            // before the window disappears, so a relaunch never races the old one.
            if (_server != null)
            {
                try { _server.Stop(); } catch { }
            }
        }

        private GlyphButton MakeButton(string text, string tip)
        {
            return new GlyphButton(text, tip, S(1));
        }

        private void BeginAddressEdit()
        {
            if (_address == null) return;
            _address.Text = _addressValue ?? "";
            _addressEditing = true;
            _addressCell.Controls.Add(_address);
            _address.Visible = true;
            _address.Focus();
            _address.SelectAll();
            if (_addressCell != null) _addressCell.Invalidate();
        }

        private void EndAddressEdit()
        {
            if (_address == null) return;
            _addressValue = _address.Text;
            _addressEditing = false;
            _address.Visible = false;
            _addressCell.Controls.Remove(_address);
            if (_addressCell != null) _addressCell.Invalidate();
        }

        private void LayoutBar(Panel bar)
        {
            int h = S(28), top = S(6), x = S(6);
            int right = bar.Width - S(6);
            if (right < S(500)) return;

            int zoomInX = right - S(32);
            int zoomLabelX = zoomInX - S(46);
            int zoomOutX = zoomLabelX - S(32);

            _back.SetBounds(x, top, S(32), h); x += S(34);
            _forward.SetBounds(x, top, S(32), h); x += S(34);
            _reload.SetBounds(x, top, S(32), h); x += S(34);
            _home.SetBounds(x, top, S(32), h); x += S(40);

            int width = zoomOutX - x - S(8);
            if (width < S(80)) width = S(80);
            int limit = bar.Width - x - S(8);
            if (limit > S(80) && width > limit) width = limit;
            _addressCell.SetBounds(x, top, width, h);
            _address.SetBounds(S(11), S(5), width - S(22), h - S(10));

            _zoomOut.SetBounds(zoomOutX, top, S(32), h);
            _zoomLabel.SetBounds(zoomLabelX, top, S(46), h);
            _zoomIn.SetBounds(zoomInX, top, S(32), h);
        }

        private string ResolveOwner()
        {
            if (_server != null && _server.BaseUrl != null) return _server.BaseUrl;
            if (!_s.Server) return DshServer.StripToken(_s.Url ?? "");
            return "http://" + _s.Host + ":" + Math.Max(1, _s.Port).ToString(CultureInfo.InvariantCulture) + "/";
        }

        // Resolve a runtime: a copy inside this folder, else the installed one,
        // else download one into this folder so nothing has to be installed.
        private string EnsureRuntime()
        {
            string local = WebViewRuntime.LocalDir;
            if (_s.SelfTest)
            {
                // Headless: never show a dialog, or an unattended --selftest run
                // would block on one. Download inline and report through the log.
                RuntimeProgress progress = new RuntimeProgress();
                string folder = WebViewRuntime.Ensure(progress, _s.ForceWebView2Download);
                App.Log("selftest webview2: " + (progress.Failed ? "failed: " + progress.Error : (folder ?? "(system)")));
                return progress.Failed ? null : folder;
            }
            if (_s.ForceWebView2Download)
            {
                App.Log("webview2 download forced by --download-webview2");
                using (DownloadDialog forced = new DownloadDialog(_theme ?? Theme.FromPage(_dark, _chromeRight)))
                {
                    forced.ForceDownload = true;
                    if (forced.ShowDialog(this) != DialogResult.OK) return null;
                    return forced.RuntimeFolder;
                }
            }
            if (WebViewRuntime.LocalValid(local)) return local;
            if (WebViewRuntime.SystemAvailable())
            {
                App.Log("using the installed WebView2 " + WebViewRuntime.SystemVersion());
                return null;
            }

            App.Log("no WebView2 runtime available; downloading a fixed version");
            using (DownloadDialog dialog = new DownloadDialog(_theme ?? Theme.FromPage(_dark, _chromeRight)))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                return dialog.RuntimeFolder;
            }
        }

        private async void BeginInit()
        {
            string runtime = EnsureRuntime();
            if (runtime == null && !WebViewRuntime.SystemAvailable())
            {
                Fail(Strings.Current == Lang.Zh ? "WebView2 不可用" : "WebView2 is unavailable",
                    Strings.Current == Lang.Zh
                        ? "本机没有 WebView2，也没能下载成功。可以在设置里把界面形式改成「系统浏览器」继续使用。"
                        : "There is no WebView2 on this machine and the download did not succeed. Set the interface to the system browser in the settings to keep working.",
                    new InvalidOperationException(App.LogFile));
                return;
            }

            string dataDir = App.ResolveDir(_s.Profile, "profile");
            try { Directory.CreateDirectory(dataDir); }
            catch (Exception ex) { Fail("profile", dataDir, ex); return; }

            // A machine-wide WEBVIEW2_BROWSER_EXECUTABLE_FOLDER silently overrides the
            // folder we pass in (common on machines with a package-managed WebView2).
            string envOverride = Environment.GetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER");
            if (runtime != null && !string.IsNullOrEmpty(envOverride))
            {
                Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", null);
                App.Log("ignored WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=" + envOverride + " (bundled runtime wins)");
            }

            App.Log("webview2=" + (runtime ?? "(system)") + " profile=" + dataDir);
            try { App.Log("webview2 version=" + CoreWebView2Environment.GetAvailableBrowserVersionString(runtime)); }
            catch (Exception ex) { App.Log("webview2 version probe failed: " + ex.Message); }

            _view = new WebView2();
            _view.Dock = DockStyle.Fill;
            _view.DefaultBackgroundColor = _dark ? Color.FromArgb(24, 25, 28) : Color.White;
            Controls.Add(_view);
            // Dock layout runs from the back of the z-order to the front, so the
            // Fill control must end up front-most: the Top strips then take their
            // space first and the view fills what is left.
            _view.BringToFront();

            CoreWebView2Environment env;
            try
            {
                CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions(BuildWebViewArgs());
                env = await CoreWebView2Environment.CreateAsync(runtime, dataDir, options);
            }
            catch (Exception ex)
            {
                Fail(Strings.Current == Lang.Zh ? "WebView2 运行时初始化失败" : "WebView2 failed to start",
                    runtime == null
                        ? (Strings.Current == Lang.Zh ? "本机安装的 WebView2 无法启动。" : "The installed WebView2 did not start.")
                        : runtime, ex);
                return;
            }

            try { await _view.EnsureCoreWebView2Async(env); }
            catch (Exception ex) { Fail("WebView2", App.LogFile, ex); return; }



            // Coming back to the window is a good moment to re-read the page colours,
            // without any timer running in the background.
            Activated += delegate { SyncThemeFromPage(); };

            CoreWebView2 core = _view.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.AreBrowserAcceleratorKeysEnabled = true;
            core.Settings.IsPasswordAutosaveEnabled = false;

            core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
            {
                if (_address != null) _address.Text = e.Uri;
                if (!_addressEditing)
                {
                    _addressValue = e.Uri;
                    if (_addressCell != null) _addressCell.Invalidate();
                }
                if (_reload != null) _reload.Text = "\u2715";
            };
            core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                SyncThemeFromPage();
                if (_reload != null) _reload.Text = "\u27F3";
                if (_back != null) _back.Enabled = core.CanGoBack;
                if (_forward != null) _forward.Enabled = core.CanGoForward;
                try
                {
                    if (_address != null) _address.Text = core.Source;
                    if (!_addressEditing)
                    {
                        _addressValue = core.Source;
                        if (_addressCell != null) _addressCell.Invalidate();
                        App.Log("bar theme: cell=" + _addressCell.BackColor + " box=" + _address.BackColor
                            + " visible=" + _address.Visible + " bounds=" + _address.Bounds
                            + " cellBounds=" + _addressCell.Bounds + " bar=" + _bar.BackColor + " dark=" + _dark);
                    }
                }
                catch { }
                App.Log("nav " + (e.IsSuccess ? "ok" : "fail:" + e.WebErrorStatus) + " " + (core.Source ?? ""));
                if (e.IsSuccess)
                {
                    // The bar sits above the page rather than over it, so the page
                    // is deliberately left without the data-windows-titlebar
                    // contract: setting it would reserve a second strip at its top.
                    SyncThemeFromPage();
                    StartThemeWatch();
            StartThemeFileWatch(System.IO.Path.Combine(App.ResolveDir(_s.Data, "data"), "profiles"));
                    if (_s.ProbeTheme) ProbeThemeWatch();
                }
                if (!e.IsSuccess && !_s.SelfTest) ShowLoadFailure(e.WebErrorStatus);
                if (_s.SelfTest) FinishSelfTest(e.IsSuccess, e.IsSuccess ? "loaded" : e.WebErrorStatus.ToString());
            };
            core.DocumentTitleChanged += delegate
            {
                if (_s.Toolbar)
                {
                    string t = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "DSH Desktop" : core.DocumentTitle;
                    Text = t + " \u2014 DSH Desktop";
                }
            };
            core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e)
            {
                e.State = CoreWebView2PermissionState.Allow;
                App.Log("permission " + e.PermissionKind + " allowed");
            };
            core.WebMessageReceived += delegate(object sender, CoreWebView2WebMessageReceivedEventArgs e)
            {
                string message = null;
                try { message = e.TryGetWebMessageAsString(); } catch (Exception ex) { App.Log("message read failed: " + ex.Message); }
                if (message == "dsh-desktop:theme")
                {
                    App.Log("page theme message");
                    // The DOM says it changed before the paint catches up, so read the
                    // colours twice: right away and once the new style has landed.
                    SyncThemeFromPage();
                    ResampleTheme(160);
                    ResampleTheme(700);
                }
            };

            core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e)
            {
                string uri = e.Uri ?? "";
                // Only real web addresses belong in the browser. A sign-in flow opens an
                // about:blank window first and fills it in later; handing that to the
                // shell produced "Get an app to open this 'about' link".
                if (!IsWebUrl(uri))
                {
                    App.Log("new window kept in the page: " + (uri.Length == 0 ? "(empty)" : uri));
                    return;
                }
                e.Handled = true;
                if (IsSameOrigin(uri)) Navigate(uri);
                else OpenInBrowser(uri);
            };
            core.ProcessFailed += delegate(object sender, CoreWebView2ProcessFailedEventArgs e)
            {
                App.Log("browser process failed: " + e.ProcessFailedKind + " exit=" + e.ExitCode);
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited && !_s.SelfTest)
                    MessageBox.Show(this, "WebView2 exited unexpectedly. Please reopen DSH Desktop." + Environment.NewLine + App.LogFile,
                        "DSH Desktop", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            if (_s.Zoom > 0.3 && _s.Zoom < 3.0)
            {
                _view.ZoomFactor = _s.Zoom;
                if (_zoomLabel != null) _zoomLabel.Text = Math.Round(_s.Zoom * 100).ToString(CultureInfo.InvariantCulture) + "%";
            }

            if (_splash != null) { Controls.Remove(_splash); _splash.Dispose(); _splash = null; }

            if (_s.SelfTest)
            {
                _selfTestTimer = new System.Windows.Forms.Timer();
                _selfTestTimer.Interval = 60000;
                _selfTestTimer.Tick += delegate { FinishSelfTest(false, "timeout"); };
                _selfTestTimer.Start();
            }

            Navigate(_s.Server ? _server.EntryUrl : _s.Url);
        }

        private string BuildWebViewArgs()
        {
            List<string> parts = new List<string>();
            if (!_s.Bare)
            {
                parts.Add("--disable-gpu");
                parts.Add("--disable-software-rasterizer");
                parts.Add("--disable-background-networking");
                parts.Add("--disable-sync");
                parts.Add("--no-first-run");
                parts.Add("--no-default-browser-check");
                parts.Add("--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection");
            }
            return string.Join(" ", parts.ToArray());
        }

        // No toolbar means no place to fix a broken page, so surface the log file.
        private void ShowLoadFailure(CoreWebView2WebErrorStatus status)
        {
            if (_failureShown) return;
            _failureShown = true;
            MessageBox.Show(this,
                (Strings.Current == Lang.Zh ? "页面没有加载成功：" : "The page did not load: ") + status
                + Environment.NewLine + Environment.NewLine + ResolveOwner()
                + Environment.NewLine + Environment.NewLine + App.LogFile,
                "DSH Desktop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void GoBack()
        {
            try { if (_view.CoreWebView2.CanGoBack) _view.CoreWebView2.GoBack(); } catch { }
        }

        private void GoForward()
        {
            try { if (_view.CoreWebView2.CanGoForward) _view.CoreWebView2.GoForward(); } catch { }
        }

        private bool IsSameOrigin(string url)
        {
            try
            {
                Uri a = new Uri(url);
                Uri b = new Uri(ResolveOwner());
                return a.Scheme == b.Scheme && a.Host == b.Host && a.Port == b.Port;
            }
            catch { return false; }
        }

        // The page drops ?token= once it is authenticated, and DSH refuses a URL
        // without it, so "open in browser" has to reuse the entry URL.
        private string ShareUrl()
        {
            if (!string.IsNullOrEmpty(_entryUrl) && IsSameOrigin(_entryUrl)) return _entryUrl;
            return _s.Server && _server != null ? _server.EntryUrl : CurrentUrl();
        }

        private static bool IsWebUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        // Only http(s) goes to the shell: anything else (about:, blob:, data:) is a
        // page internal and must stay inside the view.
        private void OpenInBrowser(string url)
        {
            if (!IsWebUrl(url)) { App.Log("refused to open non-web url: " + url); return; }
            try { Process.Start(url); }
            catch (Exception ex) { App.Log("open browser failed: " + ex.Message); }
        }

        private void Fail(string title, string hint, Exception ex)
        {
            App.Log("FATAL " + title + ": " + ex);
            if (_s.SelfTest) { FinishSelfTest(false, title + " | " + ex.Message); return; }
            MessageBox.Show(this, title + Environment.NewLine + Environment.NewLine + hint,
                "DSH Desktop", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void FinishSelfTest(bool ok, string detail)
        {
            if (_finished) return;
            _finished = true;
            try
            {
                if (_selfTestTimer != null) { _selfTestTimer.Stop(); _selfTestTimer.Dispose(); }
                string body = "{\"ok\":" + (ok ? "true" : "false")
                    + ",\"url\":" + JsonString(_server != null ? _server.BaseUrl : (_s.Server ? null : _s.Url))
                    + ",\"detail\":" + JsonString(detail)
                    + ",\"server\":" + (_s.Server ? "true" : "false")
                    + ",\"node\":" + JsonString(_server != null ? _server.NodePath : null)
                    + ",\"version\":" + JsonString(ProbeVersion()) + "}";
                File.WriteAllText(Path.Combine(App.BaseDir, "selftest-result.json"), body, Encoding.UTF8);
            }
            catch { }
            Environment.ExitCode = ok ? 0 : 1;
            try { Application.Exit(); } catch { }
            Environment.Exit(Environment.ExitCode);
        }

        private static string ProbeVersion()
        {
            try
            {
                string runtime = WebViewRuntime.LocalValid(WebViewRuntime.LocalDir) ? WebViewRuntime.LocalDir : null;
                return CoreWebView2Environment.GetAvailableBrowserVersionString(runtime);
            }
            catch { return "?"; }
        }

        private static string JsonString(string s)
        {
            if (s == null) s = "";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.R) { Reload(); e.SuppressKeyPress = true; }
            else if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)) { SetZoom(0.1); e.SuppressKeyPress = true; }
            else if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract)) { SetZoom(-0.1); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.D0) { SetZoomAbsolute(1.0); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F5) { Reload(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F12) { OpenDevTools(); e.SuppressKeyPress = true; }
            else if (_address != null && e.Control && e.KeyCode == Keys.L) { _address.Focus(); _address.SelectAll(); e.SuppressKeyPress = true; }
        }

        // DSH dims and desaturates the page behind its own modal panels. Our
        // dialogs are separate windows, so the same effect is applied inside the
        // page: a fixed scrim with a grayscale backdrop filter. It also swallows
        // clicks, so the page behind a modal cannot be operated by accident.
        private async void SetPageDim(bool on)
        {
            if (_view == null || _view.CoreWebView2 == null) return;
            try
            {
                const string script =
                    "(function(){try{var id='dsh-desktop-scrim';var old=document.getElementById(id);" +
                    "if(!%ON%){if(old)old.remove();return;}" +
                    "if(old)return;" +
                    "var d=document.createElement('div');d.id=id;" +
                    "d.style.cssText='position:fixed;inset:0;z-index:2147483646;" +
                    "background:rgba(0,0,0,0.34);backdrop-filter:grayscale(1) brightness(0.9);" +
                    "-webkit-backdrop-filter:grayscale(1) brightness(0.9);';" +
                    "document.documentElement.appendChild(d);" +
                    "}catch(e){}})()";
                await _view.CoreWebView2.ExecuteScriptAsync(
                    script.Replace("%ON%", on ? "true" : "false"));
            }
            catch (Exception ex) { App.Log("page dim failed: " + ex.Message); }
        }

        private void ShowSettings()
        {
            SetPageDim(true);
            try
            {
                using (SettingsDialog dialog = new SettingsDialog(_s, _theme ?? Theme.FromPage(_dark, _chromeRight)))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    if (dialog.RestartRequested) Restart();
                }
            }
            finally { SetPageDim(false); }
        }

        private void ShowUpdate()
        {
            if (!_s.Server)
            {
                MessageBox.Show(this, Strings.T("update.notBundled"), Strings.T("update.title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (UpdateDialog dialog = new UpdateDialog(_theme ?? Theme.FromPage(_dark, _chromeRight), StopBundledServer))
            {
                SetPageDim(true);
                DialogResult result;
                try { result = dialog.ShowDialog(this); }
                finally { SetPageDim(false); }
                if (dialog.RestartRequested) Restart();
                else if (result == DialogResult.OK)
                {
                    // The runtime changed on disk; a restart is the only way to
                    // reload it, so offer it once more.
                    if (MessageBox.Show(this, Strings.T("update.restartAsk"), Strings.T("update.title"),
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        Restart();
                }
            }
        }

        // Called before an in-place update: the running dsh keeps files open in
        // app\\node_modules, so replacing that folder requires stopping it first.
        private void StopBundledServer()
        {
            if (_server == null) return;
            App.Log("stopping the bundled server for an update");
            try { _server.Stop(); } catch (Exception ex) { App.Log("stop failed: " + ex.Message); }
            try { _server.Dispose(); } catch { }
            _server = null;
        }

        private void Restart()
        {
            App.Log("restart requested");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.Arguments = "--wait-pid=" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                App.Log("restart failed: " + ex.Message);
                MessageBox.Show(this, "Restart failed. Please reopen dsh-desktop.exe." + Environment.NewLine + ex.Message,
                    "DSH Desktop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _exiting = true;
            Close();
        }

        private void ShowMenu(Control anchor)
        {
            Theme theme = _theme ?? Theme.FromPage(_dark, _chromeRight);
            RoundedMenu menu = new RoundedMenu(S(6));

            // The renderer is OS-aware: rounded on Windows 11, square on Windows 10.
            // The background follows the theme on both, and the left icon/check column
            // is hidden so no darker strip shows beside the labels.
            menu.Renderer = new MenuRenderer(theme);
            menu.BackColor = theme.Dark ? theme.MenuSurface : Color.White;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            if (NativeMethods.Windows11)
            {
                menu.ForeColor = theme.Text;
                menu.Font = new Font("Microsoft YaHei UI", 9f);
                menu.Padding = new Padding(S(7));
            }

            menu.Items.Add(Strings.T("menu.settings"), null, delegate { ShowSettings(); });
            menu.Items.Add(Strings.T("menu.update"), null, delegate { ShowUpdate(); });

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Strings.T("menu.openInBrowser"), null, delegate { OpenInBrowser(ShareUrl()); });
            menu.Items.Add(Strings.T("menu.copyUrl"), null, delegate { try { Clipboard.SetText(ShareUrl()); App.Log("copied " + ShareUrl()); } catch { } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Strings.T("menu.devtools"), null, delegate { OpenDevTools(); });
            menu.Items.Add(Strings.T("menu.openWorkspace"), null, delegate { OpenFolder(App.ResolveDir(_s.Workspace, "workspace")); });
            menu.Items.Add(Strings.T("menu.openData"), null, delegate { OpenFolder(App.ResolveDir(_s.Data, "data")); });
            menu.Items.Add(Strings.T("menu.openIni"), null, delegate { OpenIni(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Strings.T("menu.about"), null, delegate { ShowAbout(); });

            // Pad every row to a fixed height and centre its label on both OSes.
            int rowHeight = Theme.MenuRowHeight(_scale);
            int textHeight = TextRenderer.MeasureText("Ag", menu.Font ?? SystemFonts.MenuFont).Height;
            int padY = Math.Max(S(4), (rowHeight - textHeight) / 2);
            foreach (ToolStripItem item in menu.Items)
            {
                ToolStripMenuItem entry = item as ToolStripMenuItem;
                if (entry != null)
                {
                    entry.Padding = new Padding(S(12), padY, S(12), padY);
                    entry.TextAlign = ContentAlignment.MiddleLeft;
                }
                else
                {
                    item.AutoSize = false;
                    item.Height = S(7);
                }
            }

            menu.Show(anchor, new Point(0, anchor.Height));
            // The menu is a separate top-level window, so the page never sees a
            // click outside it; dimming while it is open and clearing on close
            // keeps the page readable but clearly inactive.
            SetPageDim(true);
            menu.Closed += delegate { SetPageDim(false); };

        }

        private void OpenIni()
        {
            try { Process.Start(App.IniPath); }
            catch
            {
                try { Process.Start("notepad.exe", "\"" + App.IniPath + "\""); }
                catch (Exception ex) { App.Log("open ini failed: " + ex.Message); }
            }
        }

        internal void ShowAboutForTest()
        {
            ShowAbout();
        }


        private void ShowAbout()
        {
            string runtime = WebViewRuntime.LocalValid(WebViewRuntime.LocalDir)
                ? WebViewRuntime.Version(WebViewRuntime.LocalDir)
                : WebViewRuntime.SystemVersion();
            string dsh = Maintenance.InstalledVersion();
            if (string.IsNullOrEmpty(dsh)) dsh = Strings.T("about.notStarted");
            string node = NodeVersion();

            using (AboutDialog dialog = new AboutDialog(
                _theme ?? Theme.FromPage(_dark, _chromeRight),
                _s.Server ? Strings.T("about.mode.server") : Strings.T("about.mode.client"),
                _entryUrl ?? ResolveOwner(), dsh, node, runtime,
                App.ResolveDir(_s.Data, "data"), App.IniPath, App.LogFile))
            {
                SetPageDim(true);
                try { dialog.ShowDialog(this); }
                finally { SetPageDim(false); }
            }
        }

        private string NodeVersion()
        {
            try
            {
                string node = System.IO.Path.Combine(App.ResolveDir(_s.RuntimeDir, "runtime"), "node.exe");
                if (!System.IO.File.Exists(node)) return Strings.T("about.notStarted");
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(node);
                return string.IsNullOrEmpty(info.FileVersion) ? "24" : info.FileVersion;
            }
            catch { return "?"; }
        }

        private void OpenFolder(string path)
        {
            try { Process.Start("explorer.exe", "\"" + path + "\""); }
            catch (Exception ex) { App.Log("open folder failed: " + ex.Message); }
        }

        private string CurrentUrl()
        {
            try { return _view.CoreWebView2.Source; }
            catch { return ResolveOwner(); }
        }

        private void OpenDevTools()
        {
            try { _view.CoreWebView2.OpenDevToolsWindow(); }
            catch (Exception ex) { App.Log("devtools failed: " + ex.Message); }
        }

        private void Reload()
        {
            try { if (_view.CoreWebView2 != null) _view.CoreWebView2.Reload(); } catch { }
        }

        private void Navigate(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("file://"))
                url = "http://" + url;
            try
            {
                App.Log("navigate " + url);
                _view.CoreWebView2.Navigate(url);
            }
            catch (Exception ex) { App.Log("navigate failed: " + ex.Message); }
        }

        private void SetZoom(double delta)
        {
            if (_view == null) return;
            double z = _view.ZoomFactor + delta;
            if (z < 0.5) z = 0.5;
            if (z > 2.0) z = 2.0;
            _view.ZoomFactor = z;
            if (_zoomLabel != null) _zoomLabel.Text = Math.Round(z * 100).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private void SetZoomAbsolute(double z)
        {
            if (_view == null) return;
            _view.ZoomFactor = z;
            if (_zoomLabel != null) _zoomLabel.Text = Math.Round(z * 100).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private static string StateFile()
        {
            return Path.Combine(App.BaseDir, "dsh-desktop.state");
        }

        private bool LoadLayout()
        {
            try
            {
                string f = StateFile();
                if (!File.Exists(f)) return false;
                string[] p = File.ReadAllText(f).Split(',');
                if (p.Length < 5) return false;
                Rectangle r = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
                foreach (Screen s in Screen.AllScreens)
                {
                    if (!s.WorkingArea.IntersectsWith(r)) continue;
                    // A layout saved on a bigger monitor must not push the window
                    // past this screen's working area.
                    r.Width = Math.Min(r.Width, s.WorkingArea.Width);
                    r.Height = Math.Min(r.Height, s.WorkingArea.Height);
                    r.X = Math.Max(s.WorkingArea.Left, Math.Min(r.X, s.WorkingArea.Right - r.Width));
                    r.Y = Math.Max(s.WorkingArea.Top, Math.Min(r.Y, s.WorkingArea.Bottom - r.Height));
                    Bounds = r;
                    break;
                }
                if (p[4] == "1") WindowState = FormWindowState.Maximized;
                return true;
            }
            catch { return false; }
        }

        // Called once the window is shown, when Screen reports the real DPI-scaled
        // desktop rather than the pre-handle placeholder geometry.
        private void FitToDesktop()
        {
            if (WindowState != FormWindowState.Normal) return;
            Rectangle area = Screen.FromHandle(Handle).WorkingArea;
            if (_savedLayout)
            {
                Rectangle r = Bounds;
                if (r.Width <= area.Width && r.Height <= area.Height
                    && r.Left >= area.Left && r.Top >= area.Top
                    && r.Right <= area.Right && r.Bottom <= area.Bottom) return;
                r.Width = Math.Min(r.Width, area.Width);
                r.Height = Math.Min(r.Height, area.Height);
                r.X = Math.Max(area.Left, Math.Min(r.X, area.Right - r.Width));
                r.Y = Math.Max(area.Top, Math.Min(r.Y, area.Bottom - r.Height));
                Bounds = r;
                return;
            }
            // First run: the comfortable size, but never larger than the desktop.
            Size size = new Size(Math.Min(1280, area.Width), Math.Min(820, area.Height));
            Bounds = new Rectangle(
                area.Left + Math.Max(0, (area.Width - size.Width) / 2),
                area.Top + Math.Max(0, (area.Height - size.Height) / 2),
                size.Width, size.Height);
        }

        private void SaveLayout()
        {
            try
            {
                Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                File.WriteAllText(StateFile(), string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4}", r.X, r.Y, r.Width, r.Height,
                    WindowState == FormWindowState.Maximized ? "1" : "0"));
            }
            catch { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_title != null) LayoutTitle();
            ApplyChrome();
            // Maximising by Win+Up or by snapping never goes through the title-bar
            // button, so the rounded shell has to be re-evaluated here as well.
            RoundShell();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            UpdateHotKey(true);
            SyncThemeFromPage();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            UpdateHotKey(false);
        }

        // Windows 11: a thick frame supplies the native resize border along the left,
        // right and bottom edges (WM_NCCALCSIZE above removes only the caption). The
        // WebView2 fills the client area from another process, so the form must own a
        // non-client band for the resize hit test to reach it - the same construction
        // explorer and Windows Terminal use. Windows 10 keeps the plain WS_POPUP path:
        // its 1px padding rail already works and must not change.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (NativeMethods.Windows11)
                {
                    cp.Style |= 0x00040000; // WS_THICKFRAME
                    cp.Style |= 0x00020000; // WS_MINIMIZEBOX
                    cp.Style |= 0x00010000; // WS_MAXIMIZEBOX
                }
                return cp;
            }
        }

        // Non-client metrics for WM_NCCALCSIZE. Only the fields the message actually
        // touches are declared: rgrc[0] is the proposed client rect on the way in and
        // the new client rect on the way out.
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NcCalcSizeRect { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NcCalcSizeParams
        {
            public NcCalcSizeRect rgrc0;
            public NcCalcSizeRect rgrc1;
            public NcCalcSizeRect rgrc2;
            public IntPtr lppos;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint ptReserved;
            public NativePoint ptMaxSize;
            public NativePoint ptMaxPosition;
            public NativePoint ptMinTrackSize;
            public NativePoint ptMaxTrackSize;
        }

        // A maximized thick-frame window is sized to the whole monitor with its frame
        // hanging off-screen, which in a DPI-virtualized WinForms window grows past the
        // screen by the frame amount. Pin the maximize geometry to the working area, the
        // way a native window does.
        private void ClampMaximizeToWorkArea(IntPtr lParam)
        {
            try
            {
                Screen screen = Screen.FromHandle(Handle);
                Rectangle work = screen.WorkingArea;
                Rectangle bounds = screen.Bounds;
                MinMaxInfo info = (MinMaxInfo)System.Runtime.InteropServices.Marshal
                    .PtrToStructure(lParam, typeof(MinMaxInfo));
                info.ptMaxPosition.X = work.X - bounds.X;
                info.ptMaxPosition.Y = work.Y - bounds.Y;
                info.ptMaxSize.X = work.Width;
                info.ptMaxSize.Y = work.Height;
                System.Runtime.InteropServices.Marshal.StructureToPtr(info, lParam, false);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int WM_HOTKEY = 0x0312;
            const int WM_NCCALCSIZE = 0x0083;
            const int HTCLIENT = 1, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
                HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotKeySettings)
            {
                ShowSettings();
                return;
            }

            const int WM_GETMINMAXINFO = 0x0024;
            if (NativeMethods.Windows11 && m.Msg == WM_GETMINMAXINFO)
            {
                base.WndProc(ref m);
                ClampMaximizeToWorkArea(m.LParam);
                return;
            }

            // Windows paints the default non-client frame over the custom frame from
            // inside DefWindowProc's WM_NCACTIVATE handling (Chromium documents this),
            // which shows up as the grey band at the top. Route it through the redraw
            // lock so the system never sees a visible window while it would paint.
            const int WM_NCACTIVATE = 0x0086;
            if (NativeMethods.Windows11 && m.Msg == WM_NCACTIVATE)
            {
                DefWindowProcLocked(m.Msg, m.WParam, m.LParam);
                m.Result = (IntPtr)1;
                return;
            }

            // Windows 11, title-less thick frame: the system removes the caption but keeps
            // the sizing border on the left, right and bottom edges, so the WebView2 (a
            // child of another process that fills the client area) can never cover the
            // pixels the resize hit test needs - the construction explorer and Windows
            // Terminal use. WinForms' own borderless handling is skipped (it would cover
            // the whole window again); DefWindowProc supplies the real thick-frame layout,
            // then the top is pushed back to the window top so no caption band is left.
            // The top resize zone lives in the title bar instead (BuildTitleBar). A
            // maximized window keeps the system layout, or it would overlap the taskbar.
            if (NativeMethods.Windows11 && m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                NcCalcSizeParams p = (NcCalcSizeParams)System.Runtime.InteropServices.Marshal
                    .PtrToStructure(m.LParam, typeof(NcCalcSizeParams));
                int windowTop = p.rgrc0.Top;    // on input rgrc0 is the new window rect
                m.Result = NativeMethods.DefWindowProc(m.HWnd, m.Msg, m.WParam, m.LParam);
                if (WindowState != FormWindowState.Maximized)
                {
                    p = (NcCalcSizeParams)System.Runtime.InteropServices.Marshal
                        .PtrToStructure(m.LParam, typeof(NcCalcSizeParams));
                    p.rgrc0.Top = windowTop;
                    System.Runtime.InteropServices.Marshal.StructureToPtr(p, m.LParam, false);
                }
                return;
            }

            // A frameless window loses the native resize border, so restore it by
            // hand: eight hit-test zones along the edges of a normal window.
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    long raw = m.LParam.ToInt64();
                    int screenX = unchecked((short)(raw & 0xFFFF));
                    int screenY = unchecked((short)((raw >> 16) & 0xFFFF));
                    // Compare against the window rect in the same coordinate space the
                    // message delivered (both come through the same DPI context), rather
                    // than PointToClient: on Windows 11 the client conversion of a
                    // DPI-unaware process misplaces the point, so the top edge zone
                    // missed and the click fell into the WebView2.
                    NativeMethods.WindowRect wr;
                    NativeMethods.GetWindowRect(Handle, out wr);
                    int b = Math.Max(4, S(6));
                    bool left = screenX - wr.Left <= b;
                    bool right = wr.Right - screenX <= b;
                    bool top = screenY - wr.Top <= b;
                    bool bottom = wr.Bottom - screenY <= b;
                    int hit = 0;
                    if (top && left) hit = HTTOPLEFT;
                    else if (top && right) hit = HTTOPRIGHT;
                    else if (bottom && left) hit = HTBOTTOMLEFT;
                    else if (bottom && right) hit = HTBOTTOMRIGHT;
                    else if (left) hit = HTLEFT;
                    else if (right) hit = HTRIGHT;
                    else if (top) hit = HTTOP;
                    else if (bottom) hit = HTBOTTOM;
                    if (hit != 0) m.Result = (IntPtr)hit;
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                        if (_tray != null)
                {
                    try { _tray.Visible = false; _tray.Dispose(); } catch { }
                    _tray = null;
                }
                if (_view != null)
                {
                    try { _view.Dispose(); } catch { }
                }
                if (_server != null)
                {
                    try { _server.Dispose(); } catch { }
                    _server = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
