// DSH Desktop - a portable, self-contained DeepSeek Harness desktop window.
//
// One double-click starts the bundled dsh web server with the bundled Node
// runtime, reads the authenticated URL it prints, and shows that page in a
// WebView2 window: the one installed on the machine, or a copy downloaded into
// this folder. Nothing is installed, nothing is registered, and closing the
// window stops the server.
//
// With server=0 in dsh-desktop.ini it is a client window only:
// connecting to the url= address instead of starting anything.
//
// Build:
//   csc /nologo /target:winexe /platform:anycpu /out:dsh-desktop.exe
//       /win32icon:app_dsh.ico /win32manifest:app.manifest
//       /r:Microsoft.Web.WebView2.Core.dll /r:Microsoft.Web.WebView2.WinForms.dll
//       /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll
//       /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll
//       src\*.cs
//
// C# 5 only (compiled by the in-box .NET Framework 4.x csc.exe).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DshDesktop
{
    internal sealed class Settings
    {
        public bool Server = true;          // false = client only
        public string Url = "http://127.0.0.1:3080/";
        public string Host = "127.0.0.1";
        public int Port = 0;                // 0 = let the OS pick a free port
        public string Workspace = "workspace";
        public string Data = "data";        // becomes DSH_HOME
        public string Profile = "profile";  // WebView2 browser data
        public string RuntimeDir = "runtime";
        public string AppDir = "app";
        public string ExtraArgs = "";
        public string Icon = "default";     // default | auto | light | dark
        public string WebView2Version = WebViewRuntime.DefaultVersion;
        // ask (prompt on close) | tray (hide to the notification area) | quit (exit).
        // Replaces the old boolean closetotray=, which is migrated in Parse.
        public string CloseAction = "ask";
        public bool Telemetry = false;
        public int StartupSeconds = 120;
        public bool SelfTest = false;
        public bool Bare = false;
        public bool ForceWebView2Download = false;
        public string UpdateToVersion;      // --update-dsh=<version>: update and exit
        public bool ProbeTheme;             // --probe-theme: measure the theme channel
        public bool AboutTest;              // --about-test: open About through the real path
        public int WaitPid = 0;             // set on a restart, to outwait the old process

        public static Settings Parse(string[] args)
        {
            Settings s = new Settings();
            bool closeActionSeen = false;   // closeaction= present in ini or args
            bool legacyTraySeen = false;    // old closetotray= present
            IniFile file = null;
            try
            {
                if (File.Exists(App.IniPath)) file = IniFile.Load(App.IniPath);
            }
            catch (Exception ex) { App.Log("ini read failed: " + ex.Message); }

            if (file != null)
            {
                s.Server = file.GetBool("server", s.Server);
                s.Url = file.Get("url", s.Url);
                        s.Host = file.Get("host", s.Host);
                s.Port = file.GetInt("port", s.Port);
                s.Workspace = file.Get("workspace", s.Workspace);
                s.Data = file.Get("data", s.Data);
                s.Profile = file.Get("profile", s.Profile);
                s.RuntimeDir = file.Get("runtime", s.RuntimeDir);
                s.AppDir = file.Get("app", s.AppDir);
                s.ExtraArgs = file.Get("args", s.ExtraArgs);
                s.Icon = file.Get("icon", s.Icon);

        
                s.WebView2Version = file.Get("webview2version", s.WebView2Version);
                string closeRaw = file.Get("closeaction", null);
                if (closeRaw != null) { s.CloseAction = NormalizeCloseAction(closeRaw); closeActionSeen = true; }
                string legacyTrayRaw = file.Get("closetotray", null);
                if (legacyTrayRaw != null) { legacyTraySeen = ParseBool(legacyTrayRaw, false); }
                s.Telemetry = file.GetBool("telemetry", s.Telemetry);
                s.StartupSeconds = file.GetInt("startupseconds", s.StartupSeconds);
            }

            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a == "--selftest") { s.SelfTest = true; continue; }
                    if (a == "--bare") { s.Bare = true; continue; }
                            if (a == "--client") { s.Server = false; continue; }
                    if (a == "--download-webview2") { s.ForceWebView2Download = true; continue; }
                    if (a.StartsWith("--update-dsh=")) { s.UpdateToVersion = a.Substring(13); continue; }
                    if (a == "--probe-theme") { s.ProbeTheme = true; continue; }
                    if (a == "--about-test") { s.AboutTest = true; continue; }
                    if (a.StartsWith("--wait-pid=")) s.WaitPid = ParseInt(a.Substring(11), 0);
                    else if (a.StartsWith("--server=")) s.Server = ParseBool(a.Substring(9), s.Server);
                    else if (a.StartsWith("--url=")) s.Url = a.Substring(6);
                            else if (a.StartsWith("--host=")) s.Host = a.Substring(7);
                    else if (a.StartsWith("--port=")) s.Port = ParseInt(a.Substring(7), s.Port);
                    else if (a.StartsWith("--workspace=")) s.Workspace = a.Substring(12);
                    else if (a.StartsWith("--data=")) s.Data = a.Substring(7);
                    else if (a.StartsWith("--profile=")) s.Profile = a.Substring(10);
                    else if (a.StartsWith("--runtime=")) s.RuntimeDir = a.Substring(10);
                    else if (a.StartsWith("--app=")) s.AppDir = a.Substring(6);
                    else if (a.StartsWith("--args=")) s.ExtraArgs = a.Substring(7);
                    else if (a.StartsWith("--icon=")) s.Icon = a.Substring(7);
            else if (a.StartsWith("--closeaction=")) { s.CloseAction = NormalizeCloseAction(a.Substring(14)); closeActionSeen = true; }
            else if (a.StartsWith("--closetotray=")) { legacyTraySeen = ParseBool(a.Substring(14), false); }
            else if (a.StartsWith("--telemetry=")) s.Telemetry = ParseBool(a.Substring(12), s.Telemetry);
            else if (a.StartsWith("--startupseconds=")) s.StartupSeconds = ParseInt(a.Substring(17), s.StartupSeconds);
            else if (a.StartsWith("--webview2version=")) s.WebView2Version = a.Substring(18);


                }
            }
            // closeaction wins when it is written down. Otherwise an older closetotray=1 keeps
            // its meaning; its absence means "ask", which is the new default.
            if (!closeActionSeen && legacyTraySeen) s.CloseAction = "tray";
            return s;
        }

        // Accepts the documented values plus a couple of obvious spellings, and never leaves the
        // field in a state the rest of the code cannot interpret.
        private static string NormalizeCloseAction(string v)
        {
            string value = (v ?? "").Trim().ToLowerInvariant();
            if (value == "tray" || value == "minimize" || value == "minimise") return "tray";
            if (value == "quit" || value == "exit" || value == "close") return "quit";
            return "ask";
        }

        public static bool ParseBool(string v, bool fallback)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            if (v.Length == 0) return fallback;
            return v == "1" || v == "true" || v == "yes" || v == "on";
        }

        public static double ParseDouble(string v, double fallback)
        {
            double d;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return fallback;
        }

        public static int ParseInt(string v, int fallback)
        {
            int i;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
            return fallback;
        }
    }

    // A key=value file that keeps the shipped comments intact: Set() rewrites an
    // existing key in place and appends one that is missing, so the hand-written
    // documentation in dsh-desktop.ini survives every save from the settings menu.
    internal sealed class IniFile
    {
        private readonly List<string> _lines = new List<string>();
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static IniFile Load(string path)
        {
            IniFile file = new IniFile();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                file._lines.Add(raw);
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                file._values[line.Substring(0, eq).Trim()] = Unquote(line.Substring(eq + 1).Trim());
            }
            return file;
        }

        public static IniFile Empty() { return new IniFile(); }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
                return value.Substring(1, value.Length - 2);
            return value;
        }

        public string Get(string key, string fallback)
        {
            string value;
            if (_values.TryGetValue(key, out value) && value.Length > 0) return value;
            return fallback;
        }

        public int GetInt(string key, int fallback)
        {
            return Settings.ParseInt(Get(key, null), fallback);
        }

        public double GetDouble(string key, double fallback)
        {
            return Settings.ParseDouble(Get(key, null), fallback);
        }

        public bool GetBool(string key, bool fallback)
        {
            string value;
            if (!_values.TryGetValue(key, out value) || value.Trim().Length == 0) return fallback;
            return Settings.ParseBool(value, fallback);
        }

        public void Set(string key, string value)
        {
            _values[key] = value;
            for (int i = 0; i < _lines.Count; i++)
            {
                string line = _lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    _lines[i] = key + "=" + value;
                    return;
                }
            }
            _lines.Add(key + "=" + value);
        }

        // Keys that no longer exist in the product are dropped from an existing
        // file instead of lingering there forever.
        public void Remove(string key)
        {
            _values.Remove(key);
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                string line = _lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    _lines.RemoveAt(i);
            }
        }

        public void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string line in _lines) sb.AppendLine(line);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }

    internal static class App
    {
        public static string BaseDir = Path.GetDirectoryName(Application.ExecutablePath);
        public static Settings S;
        public static string LogFile;
        private static readonly object LogLock = new object();

        public static string IniPath
        {
            get { return Path.Combine(BaseDir, "dsh-desktop.ini"); }
        }

        public static void Log(string message)
        {
            try
            {
                lock (LogLock)
                {
                    File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        // The package ships dsh-desktop.ini, but a folder assembled by hand from the
        // exe alone would otherwise fall back to hard-coded defaults silently.
        public static void EnsureIni()
        {
            try
            {
                if (File.Exists(IniPath)) return;

                // Carried over from when this was called DSHLite: an existing folder
                // keeps its settings instead of being handed a fresh default file.
                string legacy = Path.Combine(BaseDir, "dshlite.ini");
                if (File.Exists(legacy))
                {
                    try
                    {
                        File.Copy(legacy, IniPath);
                        Log("adopted dshlite.ini as dsh-desktop.ini");
                        return;
                    }
                    catch (Exception ex) { Log("could not adopt dshlite.ini: " + ex.Message); }
                }

                File.WriteAllText(IniPath, DefaultIni(), new UTF8Encoding(false));
                Log("created default ini: " + IniPath);
            }
            catch (Exception ex) { Log("EnsureIni failed: " + ex.Message); }
        }

        public static string DefaultIni()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# ============================================================================");
            sb.AppendLine("# DSH Desktop configuration / DSH Desktop 配置文件");
            sb.AppendLine("# ----------------------------------------------------------------------------");
            sb.AppendLine("# Everything here is also editable from the settings menu (the gear button");
            sb.AppendLine("# in the title bar), which writes this same file without losing these comments.");
            sb.AppendLine("# 这里的每一项都可以在标题栏右上角的齿轮「设置」里改，保存时不会丢掉下面的注释。");
            sb.AppendLine("# ============================================================================");
            sb.AppendLine();
            sb.AppendLine("# Start the bundled dsh web server / 是否启动内置的 dsh web 服务:");
            sb.AppendLine("#   1 = start it (default) / 启动内置服务（默认）");
            sb.AppendLine("#   0 = client only: nothing is started, the window just opens url= below");
            sb.AppendLine("#       仅作为客户端：不启动任何服务，只打开下面的 url=");
            sb.AppendLine("server=1");
            sb.AppendLine();
            sb.AppendLine("# Server address used when server=0 / 仅客户端模式要填的地址：");
            sb.AppendLine("#   url=http://10.0.0.123:3080/        another machine");
            sb.AppendLine("#   url=http://127.0.0.1:3080/?token=  first time, when dsh web printed one");
            sb.AppendLine("# 拿到 cookie 之后只填纯地址即可；首次访问（或换端口/换机器）时带上 ?token=...。");
            sb.AppendLine("url=http://127.0.0.1:3080/");
            sb.AppendLine();

            sb.AppendLine("# Listen address and port. 0 = let the OS pick a free port on every launch.");
            sb.AppendLine("# 监听地址与端口；port=0（默认）表示每次自动挑一个空闲端口。");
            sb.AppendLine("host=127.0.0.1");
            sb.AppendLine("port=0");
            sb.AppendLine();
            sb.AppendLine("# Agent workspace / 智能体的工作区根目录（相对路径相对于本程序所在目录）");
            sb.AppendLine("workspace=workspace");
            sb.AppendLine();
            sb.AppendLine("# Portable data home (DSH_HOME): settings, credentials, sessions, plugins.");
            sb.AppendLine("# 便携数据目录（DSH_HOME）：配置、密钥、会话、插件都在这里，删掉等于彻底重置。");
            sb.AppendLine("data=data");
            sb.AppendLine();
            sb.AppendLine("# WebView2 browser data (cookies, login state) / 浏览器数据（cookie、登录态）");
            sb.AppendLine("profile=profile");
            sb.AppendLine();
            sb.AppendLine("# Bundled runtime folders, relative to this program's folder.");
            sb.AppendLine("# 自带运行时目录，相对于本程序所在目录。");
            sb.AppendLine("runtime=runtime");
            sb.AppendLine("app=app");
            sb.AppendLine();
            sb.AppendLine("# Extra arguments for the dsh web command. Normally empty.");
            sb.AppendLine("# 追加给 dsh web 的参数，通常留空。");
            sb.AppendLine("args=");
            sb.AppendLine();
            sb.AppendLine("# Icon style / 图标样式: default | auto | light | dark");
            sb.AppendLine("#   default = Official artwork / 官方图标（默认）");
            sb.AppendLine("#   auto    = Follow system theme / 跟随系统主题");
            sb.AppendLine("#   light   = Dark whale / 黑鲸");
            sb.AppendLine("#   dark    = White whale / 白鲸");
            sb.AppendLine("icon=default");
            sb.AppendLine();
            sb.AppendLine("# WebView2 version to download when the machine has none, or when this");
            sb.AppendLine("# folder has no webview2\\ yet. The installed runtime is always preferred.");
            sb.AppendLine("# 本机没有 WebView2 且目录里也没有时，下载这个固定版本；有系统运行时就优先用系统。");
            sb.AppendLine("webview2version=" + WebViewRuntime.DefaultVersion);
            sb.AppendLine();
            sb.AppendLine("# Close button / 关闭按钮:");
            sb.AppendLine("#   ask  = prompt every time (default) / 每次询问（默认）");
            sb.AppendLine("#   tray = minimize to the notification area, the session keeps running");
            sb.AppendLine("#          最小化到托盘，服务继续运行");
            sb.AppendLine("#   quit = stop the server and exit / 停止服务并退出");
            sb.AppendLine("# An older closetotray=1 still means tray and is migrated on launch.");
            sb.AppendLine("# 旧的 closetotray=1 仍等同 tray，首次启动会自动改写。");
            sb.AppendLine("closeaction=ask");
            sb.AppendLine();
            sb.AppendLine("# Send anonymous usage telemetry to DeepSeek: 0 = off (default) / 1 = on");
            sb.AppendLine("# 是否上报匿名使用统计：0 = 关闭（默认）/ 1 = 开启");
            sb.AppendLine("telemetry=0");
            sb.AppendLine();
            sb.AppendLine("# Seconds to wait for the server before reporting a startup failure.");
            sb.AppendLine("# 启动等待秒数，超时则报错并给出日志。");
            sb.AppendLine("startupseconds=120");
            return sb.ToString();
        }

        public static string ResolveDir(string configured, string fallbackRelative)
        {
            string value = string.IsNullOrWhiteSpace(configured) ? fallbackRelative : configured.Trim();
            if (!Path.IsPathRooted(value)) value = Path.Combine(BaseDir, value);
            try { return Path.GetFullPath(value); }
            catch { return value; }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int index);

        // System DPI factor: 1.0 at 96 DPI, 1.5 at 144 DPI. The window is system
        // DPI aware, so one value serves the whole process and it is available
        // before any window handle exists.
        public static float Scale()
        {
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return 1f;
                int dpi = GetDeviceCaps(dc, 88); // LOGPIXELSX
                ReleaseDC(IntPtr.Zero, dc);
                if (dpi <= 0) return 1f;
                return dpi / 96f;
            }
            catch { return 1f; }
        }

        // icon=default uses the official DSH artwork (rounded backdrop, read anywhere).
        // icon=light|dark|auto switch between the monochrome whale variants.
        private static string _lastIconResource;   // suppresses repeated "icon=..." log lines
        public static Icon PickThemeIcon(string mode)
        {
            string choice = (mode ?? "default").Trim().ToLowerInvariant();
            string resource;
            if (choice == "light") resource = "dsh_black.ico";
            else if (choice == "dark") resource = "dsh_white.ico";
            else if (choice == "auto") resource = SystemUsesLightTheme() ? "dsh_black.ico" : "dsh_white.ico";
            else
            {
                if (choice != "default") Log("unknown icon mode '" + choice + "', falling back to default");
                resource = "dsh_default.ico";
            }

            // Logged once per distinct resource: PickThemeIcon runs for the window, the tray and
            // every dialog, so logging unconditionally filled the log with the same line.
            if (!string.Equals(resource, _lastIconResource, StringComparison.Ordinal))
            {
                _lastIconResource = resource;
                Log("icon=" + choice + " -> " + resource);
            }
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(resource))
                {
                    if (s != null) return new Icon(s);
                }
                Log("icon resource missing: " + resource);
            }
            catch (Exception ex) { Log("theme icon failed: " + ex.Message); }

            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { return null; }
        }

        public static bool SystemUsesLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AppsUseLightTheme");
                        if (value is int) return (int)value != 0;
                    }
                }
            }
            catch { }
            return true;
        }

        public static bool PortFree(string host, int port)
        {
            if (port <= 0) return true;
            TcpListener listener = null;
            try
            {
                IPAddress address = IPAddress.Loopback;
                IPAddress.TryParse(host, out address);
                if (address == null) address = IPAddress.Loopback;
                listener = new TcpListener(address, port);
                listener.Start();
                return true;
            }
            catch { return false; }
            finally { if (listener != null) { try { listener.Stop(); } catch { } } }
        }

        // Perceived brightness, used to pick readable foreground colours on
        // whatever background the page happens to use.
        public static bool IsDark(Color color)
        {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            return (0.2126 * r + 0.7152 * g + 0.0722 * b) < 0.5;
        }

                
                
        // Fallback when npm cannot be reached: the newest version this build was
        // verified against. Normal installs take whatever npm publishes last.
        public const string DefaultDshVersion = "0.2.2-rc.3";

        public const string Author = "ljh575";

        public static string Version
        {
            get
            {
                try
                {
                    Version v = Assembly.GetExecutingAssembly().GetName().Version;
                    return v.Major + "." + v.Minor + "." + v.Build;
                }
                catch { return "1.0.0"; }
            }
        }

        public static int Luma(Color color)
        {
            return (int)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
        }

        public static Color Shift(Color color, int delta)
        {
            return Color.FromArgb(
                Math.Max(0, Math.Min(255, color.R + delta)),
                Math.Max(0, Math.Min(255, color.G + delta)),
                Math.Max(0, Math.Min(255, color.B + delta)));
        }

        public static Color ParseCssColor(string css)
        {
            if (string.IsNullOrWhiteSpace(css)) return Color.Empty;
            Match m = Regex.Match(css, "rgba?\\(\\s*(\\d+)\\s*,\\s*(\\d+)\\s*,\\s*(\\d+)\\s*(?:,\\s*([0-9.]+)\\s*)?\\)");
            if (m.Success)
            {
                if (m.Groups[4].Success)
                {
                    double alpha;
                    if (double.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out alpha) && alpha <= 0.01)
                        return Color.Empty;
                }
                return Color.FromArgb(
                    int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
            }
            m = Regex.Match(css, "^#([0-9a-fA-F]{6})$");
            if (m.Success)
            {
                int value = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return Color.FromArgb((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
            }
            return Color.Empty;
        }
    }

    // Runs the bundled `dsh web` and exposes the authenticated URL it prints.
    // A Job Object guarantees the whole node process tree dies with this process,
    // so an unexpected crash cannot leave a server holding the port.
    internal sealed class DshServer : IDisposable
    {
        private readonly Settings _s;
        private Process _proc;
        private IntPtr _job = IntPtr.Zero;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private readonly object _lock = new object();
        private readonly StringBuilder _tail = new StringBuilder();
        private volatile bool _exited;
        private volatile int _exitCode;
        private string _lastError;

        public string BaseUrl;      // http://127.0.0.1:port/
        public string EntryUrl;     // the same address with ?token=...
        public string NodePath;
        public string BinPath;

        public DshServer(Settings settings) { _s = settings; }

        public bool Exited { get { return _exited; } }
        public int ExitCode { get { return _exitCode; } }
        public string LastError { get { return _lastError; } }

        public string Tail
        {
            get { lock (_lock) return _tail.ToString(); }
        }

        public bool Start()
        {
            string appDir = App.ResolveDir(_s.AppDir, "app");
            string runtimeDir = App.ResolveDir(_s.RuntimeDir, "runtime");
            BinPath = Path.Combine(appDir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
            if (!File.Exists(BinPath))
            {
                _lastError = "NotFound: " + BinPath;
                if (Strings.Current == Lang.Zh)
                    _lastError = "找不到内置的 dsh：" + BinPath + Environment.NewLine
                        + "如果只想连远端服务，请在设置里关掉「启动内置服务」（server=0）并填写服务地址。";
                App.Log("missing dsh entry: " + BinPath);
                return false;
            }

            string bundledNode = Path.Combine(runtimeDir, "node.exe");
            if (File.Exists(bundledNode))
            {
                NodePath = bundledNode;
            }
            else
            {
                // Source checkouts and dev machines often run without the runtime
                // staged next to the exe; fall back to node on PATH.
                NodePath = "node";
                App.Log("bundled node.exe missing under " + runtimeDir + "; falling back to node on PATH");
            }

            string dataHome = App.ResolveDir(_s.Data, "data");
            string workspace = App.ResolveDir(_s.Workspace, "workspace");
            try
            {
                Directory.CreateDirectory(dataHome);
                Directory.CreateDirectory(workspace);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }

            int port = _s.Port;
            if (!App.PortFree(_s.Host, port))
            {
                App.Log("port " + port + " is busy; asking the OS for a free one");
                port = 0;
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = NodePath;
            psi.Arguments = Quote(BinPath) + " web --no-open --host " + _s.Host + " --port " + port.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(_s.ExtraArgs)) psi.Arguments += " " + _s.ExtraArgs.Trim();
            psi.WorkingDirectory = workspace;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            psi.EnvironmentVariables["DSH_HOME"] = dataHome;
            psi.EnvironmentVariables["NO_COLOR"] = "1";
            psi.EnvironmentVariables["FORCE_COLOR"] = "0";
            if (!_s.Telemetry)
            {
                psi.EnvironmentVariables["DSH_TELEMETRY_DISABLED"] = "1";
                psi.EnvironmentVariables["DSH_TELEMETRY_MODE"] = "DISABLED";
            }
            // Let tools the agent runs find the same node, and prefer the bundled
            // runtime over anything globally installed. The runtime folder also
            // carries pnpm.cmd, which `dsh plugin` needs.
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.EnvironmentVariables["PATH"] = runtimeDir + ";" + path;
            psi.EnvironmentVariables["DSH_NODE"] = NodePath;

            App.Log("starting: " + NodePath + " " + psi.Arguments);
            App.Log("cwd=" + psi.WorkingDirectory + " DSH_HOME=" + dataHome);

            try
            {
                _job = JobObject.Create();
                _proc = new Process();
                _proc.StartInfo = psi;
                _proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { OnLine(e.Data, false); };
                _proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { OnLine(e.Data, true); };
                _proc.EnableRaisingEvents = true;
                _proc.Exited += delegate { _exited = true; _exitCode = SafeExitCode(); _ready.Set(); };
                _proc.Start();
                if (_job != IntPtr.Zero) JobObject.Assign(_job, _proc);
                _proc.BeginOutputReadLine();
                _proc.BeginErrorReadLine();
                _ready.WaitOne(_s.StartupSeconds * 1000);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                App.Log("start failed: " + ex);
                return false;
            }

            if (EntryUrl != null) return true;

            if (_exited && _lastError == null)
                _lastError = "dsh web exited before printing an address (exit code " + _exitCode.ToString(CultureInfo.InvariantCulture) + ").";
            else if (_lastError == null)
                _lastError = "timed out after " + _s.StartupSeconds.ToString(CultureInfo.InvariantCulture) + "s waiting for dsh web.";
            return false;
        }

        private int SafeExitCode()
        {
            try { return _proc.ExitCode; }
            catch { return -1; }
        }

        private void OnLine(string line, bool isError)
        {
            if (line == null) return;
            App.Log((isError ? "[err] " : "[out] ") + line);

            lock (_lock)
            {
                _tail.AppendLine(line);
                if (_tail.Length > 8000) _tail.Remove(0, _tail.Length - 8000);
            }

            if (EntryUrl != null) return;
            Match m = Regex.Match(line, "https?://[^\\s\"']+");
            if (!m.Success) return;
            string url = m.Value;
            string baseUrl = StripToken(url);
            lock (_lock)
            {
                if (EntryUrl != null) return;
                EntryUrl = url;
                BaseUrl = baseUrl;
            }
            App.Log("server url: " + baseUrl);
            _ready.Set();
        }

        public static string StripToken(string url)
        {
            int q = url.IndexOf('?');
            if (q < 0) return url;
            return url.Substring(0, q);
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

        public void Stop()
        {
            try
            {
                if (_proc != null && !_proc.HasExited)
                {
                    App.Log("stopping server pid=" + _proc.Id);
                    if (_job != IntPtr.Zero) JobObject.Terminate(_job);
                    if (!_proc.WaitForExit(5000)) _proc.Kill();
                }
            }
            catch (Exception ex) { App.Log("stop failed: " + ex.Message); }
        }

        public void Dispose()
        {
            Stop();
            try { if (_proc != null) _proc.Dispose(); } catch { }
            if (_job != IntPtr.Zero) { try { JobObject.Close(_job); } catch { } _job = IntPtr.Zero; }
            _ready.Close();
        }
    }

    internal static class JobObject
    {
        private const int ExtendedLimitInformation = 9;
        private const uint KillOnJobClose = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimit
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimit
        {
            public BasicLimit BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint infoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public static IntPtr Create()
        {
            try
            {
                IntPtr job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) { App.Log("CreateJobObject failed: " + Marshal.GetLastWin32Error()); return IntPtr.Zero; }

                ExtendedLimit info = new ExtendedLimit();
                info.BasicLimitInformation.LimitFlags = KillOnJobClose;
                int size = Marshal.SizeOf(typeof(ExtendedLimit));
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(info, buffer, false);
                    if (!SetInformationJobObject(job, ExtendedLimitInformation, buffer, (uint)size))
                    {
                        App.Log("SetInformationJobObject failed: " + Marshal.GetLastWin32Error());
                        CloseHandle(job);
                        return IntPtr.Zero;
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
                return job;
            }
            catch (Exception ex)
            {
                App.Log("job object setup failed: " + ex.Message);
                return IntPtr.Zero;
            }
        }

        public static void Assign(IntPtr job, Process proc)
        {
            try
            {
                if (!AssignProcessToJobObject(job, proc.Handle))
                    App.Log("AssignProcessToJobObject failed: " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex) { App.Log("job assign failed: " + ex.Message); }
        }

        public static void Terminate(IntPtr job)
        {
            try { TerminateJobObject(job, 0); }
            catch { }
        }

        public static void Close(IntPtr job)
        {
            try { CloseHandle(job); }
            catch { }
        }
    }

    internal static class Program
    {
        // Two instances writing one DSH_HOME can corrupt session logs, so a second
        // launch focuses the existing window instead of starting another server.
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            App.LogFile = Path.Combine(App.BaseDir, "dsh-desktop.log");
            App.EnsureIni();
            App.S = Settings.Parse(args);
            Strings.Detect(App.ResolveDir(App.S.Data, "data"));
            App.Log("=== start " + DateTime.Now.ToString("s") + " " + string.Join(" ", args) + " lang=" + Strings.L);
            // One line with every effective setting: makes dsh-desktop.ini verifiable
            // from the log and gives support something concrete to read.
            App.Log("config: server=" + (App.S.Server ? "1" : "0")
                + " host=" + App.S.Host
                + " port=" + App.S.Port.ToString(CultureInfo.InvariantCulture)
                + (App.S.Server ? "" : " url=" + App.S.Url)
                + " workspace=" + App.ResolveDir(App.S.Workspace, "workspace")
                + " data=" + App.ResolveDir(App.S.Data, "data")
                + " profile=" + App.ResolveDir(App.S.Profile, "profile")
                + " runtime=" + App.ResolveDir(App.S.RuntimeDir, "runtime")
                + " app=" + App.ResolveDir(App.S.AppDir, "app")
                + " icon=" + App.S.Icon

                + " close=" + App.S.CloseAction
                + " telemetry=" + (App.S.Telemetry ? "1" : "0")
                + " startupseconds=" + App.S.StartupSeconds.ToString(CultureInfo.InvariantCulture)
                + " webview2version=" + App.S.WebView2Version
                + " args=[" + (App.S.ExtraArgs ?? "") + "]");

            // Scriptable update: same code path the settings menu uses, but without
            // a window, so it can be run from a deployment script.
            if (!string.IsNullOrEmpty(App.S.UpdateToVersion))
            {
                bool ok = Maintenance.UpdateTo(App.S.UpdateToVersion, delegate(string line) { App.Log(line); });
                App.Log("update to " + App.S.UpdateToVersion + ": " + (ok ? "ok" : "failed"));
                Environment.Exit(ok ? 0 : 1);
                return;
            }

            // A restart spawned by the settings menu must outwait this process,
            // which still owns the mutex and the server until it exits.
            if (App.S.WaitPid > 0)
            {
                try
                {
                    Process old = Process.GetProcessById(App.S.WaitPid);
                    App.Log("waiting for pid " + App.S.WaitPid + " to exit before taking over");
                    old.WaitForExit(20000);
                }
                catch (ArgumentException) { }
                catch (Exception ex) { App.Log("wait-pid failed: " + ex.Message); }
            }

            string dataDir = App.ResolveDir(App.S.Data, "data");
            string key = Convert.ToBase64String(Encoding.UTF8.GetBytes(dataDir.ToLowerInvariant()))
                .Replace("=", "").Replace("+", "-").Replace("/", "_");
            if (key.Length > 120) key = key.Substring(0, 120);
            bool created;
            _mutex = new Mutex(true, "Local\\dsh-desktop-" + key, out created);
            if (!created && !App.S.SelfTest)
            {
                App.Log("another instance owns " + dataDir + "; focusing it");
                if (!ActivateExisting())
                    MessageBox.Show(
                        Strings.Current == Lang.Zh
                            ? "已经有一个 DSH Desktop 在使用这个数据目录了。\r\n\r\n" + dataDir
                                + "\r\n\r\n请从任务栏或托盘恢复那个窗口；若要同时开两个，请复制整份程序并用不同的 data 目录。"
                            : "Another DSH Desktop is already using this data folder.\r\n\r\n" + dataDir
                                + "\r\n\r\nRestore that window from the taskbar or tray; to run two, copy the folder and give each a different data folder.",
                        "DSH Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                MainForm shell = new MainForm(App.S);
                if (App.S.AboutTest)
                {
                    System.Windows.Forms.Timer opener = new System.Windows.Forms.Timer();
                    opener.Interval = 9000;
                    opener.Tick += delegate { opener.Stop(); shell.ShowAboutForTest(); };
                    shell.Shown += delegate { opener.Start(); };
                }
                Application.Run(shell);
            }
            catch (Exception ex)
            {
                App.Log("unhandled: " + ex);
                Environment.ExitCode = 2;
            }
            finally
            {
                try { if (_mutex != null) _mutex.ReleaseMutex(); } catch { }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc callback, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        private delegate bool EnumProc(IntPtr handle, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int command);

        // The caption carries the page title, so it cannot be looked up verbatim.
        // Our window is the one whose caption ends with the program name.
        private static bool ActivateExisting()
        {
            try
            {
                IntPtr found = IntPtr.Zero;
                EnumWindows(delegate(IntPtr handle, IntPtr param)
                {
                    if (!IsWindowVisible(handle)) return true;
                    StringBuilder caption = new StringBuilder(300);
                    GetWindowText(handle, caption, 300);
                    string text = caption.ToString();
                    if (text == "DSH Desktop" || text.EndsWith(" \u2014 DSH Desktop", StringComparison.Ordinal))
                    {
                        found = handle;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);

                if (found == IntPtr.Zero) return false;
                ShowWindow(found, 9); // SW_RESTORE
                SetForegroundWindow(found);
                return true;
            }
            catch { return false; }
        }
    }
}
