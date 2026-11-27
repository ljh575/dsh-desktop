// Localisation for the launcher's own UI. DSH stores its interface language in
// the profile patch (the same place its Settings page writes it), so the
// launcher reads that and speaks the language the page is already using.

using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace DshDesktop
{
    // The locales DSH ships: zh, en, ja, es, fr, de, ru, pt. Everything the
    // launcher shows follows the page's language when it is one of these.
    internal sealed class Lang
    {
        public readonly string Code;
        private Lang(string code) { Code = code; }

        public static readonly Lang Zh = new Lang("zh");
        public static readonly Lang En = new Lang("en");
        public static readonly Lang Ja = new Lang("ja");
        public static readonly Lang Es = new Lang("es");
        public static readonly Lang Fr = new Lang("fr");
        public static readonly Lang De = new Lang("de");
        public static readonly Lang Ru = new Lang("ru");
        public static readonly Lang Pt = new Lang("pt");

        private static readonly Lang[] All = new Lang[] { Zh, En, Ja, Es, Fr, De, Ru, Pt };

        public static Lang From(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            string code = tag.Trim().ToLowerInvariant();
            int cut = code.IndexOf('-');
            if (cut < 0) cut = code.IndexOf('_');
            if (cut > 0) code = code.Substring(0, cut);
            foreach (Lang lang in All)
            {
                if (lang.Code == code) return lang;
            }
            return null;
        }
    }

    internal static partial class Strings
    {
        private static bool Extra(string code, string key, out string text)
        {
            return StringsExtra.Lookup(code, key, out text);
        }

        private static Lang _current = Lang.Zh;

        public static Lang Current
        {
            get { return _current; }
        }

        public static string L
        {
            get { return _current.Code; }
        }

        // Preference comes from DSH's own locale setting, which is written to the
        // web profile patch; a missing or unreadable value falls back to the
        // Windows UI language.
        public static void Detect(string dataDir)
        {
            string preference = ReadDshLocale(dataDir);
            Lang chosen = Lang.From(preference);
            if (chosen != null) { _current = chosen; return; }
            chosen = Lang.From(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
            _current = chosen ?? Lang.En;
        }

        private static string ReadDshLocale(string dataDir)
        {
            try
            {
                if (string.IsNullOrEmpty(dataDir)) return null;
                string patch = Path.Combine(dataDir, "profiles", "web", "cordis.patch.yml");
                if (!File.Exists(patch)) return null;
                string text = File.ReadAllText(patch);
                Match block = Regex.Match(text, "id:\\s*locale\\b[\\s\\S]{0,400}?preference:\\s*([A-Za-z_-]+)");
                if (block.Success) return block.Groups[1].Value;
                Match loose = Regex.Match(text, "preference:\\s*(zh|en)\\b", RegexOptions.IgnoreCase);
                if (loose.Success) return loose.Groups[1].Value;
            }
            catch { }
            return null;
        }

        // The page is the authority once it loads: DSH resolves its own locale
        // (profile setting, else the request's Accept-Language), so mirror the
        // html lang attribute whenever it disagrees with the initial guess.
        public static bool SetFromPage(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return false;
            string value = tag.Trim().ToLowerInvariant();
            Lang next = Lang.From(value) ?? _current;
            if (next == _current) return false;
            _current = next;
            return true;
        }

        // One table, two languages: zh first, en second.
        public static string T(string key)
        {
            bool zh = _current == Lang.Zh;
            if (!zh && _current != Lang.En)
            {
                string localized;
                if (Extra(_current.Code, key, out localized)) return localized;
            }
            switch (key)
            {
                case "app.title": return "DSH Desktop";

                case "splash.install": return zh
                ? "正在下载 DeepSeek Harness 运行时…\r\n\r\n首次启动需要联网获取 dsh，请稍候；完成后会自动继续。"
                : "Downloading the DeepSeek Harness runtime…\r\n\r\nThe first launch fetches dsh over the network. It continues on its own once that finishes.";
            case "splash.install.preparing": return zh ? "正在准备…" : "preparing…";
            case "splash.install.progress": return zh ? "{1}MB ({0}%)" : "{1}MB ({0}%)";
            case "splash.ready": return zh
                ? "正在启动 DeepSeek Harness…\r\n\r\n首次启动会初始化工作区与插件，可能需要十几秒。"
                : "Starting DeepSeek Harness…\r\n\r\nThe first launch initialises the workspace and plugins; this can take a while.";
            case "splash.install.failed": return zh
                ? "无法下载 dsh。请检查网络后重试，或从另一台机器拷贝 app\\node_modules 过来。"
                : "Could not download dsh. Check the network and retry, or copy app\\node_modules from another machine.";
            case "splash.install.hint": return zh
                ? "日志里有 pnpm 的完整输出。"
                : "The log holds the full pnpm output.";
            case "tray.show": return zh ? "显示窗口" : "Show window";
            case "tray.quit": return zh ? "退出" : "Quit";
            case "menu.settings": return zh ? "设置…" : "Settings…";
                case "menu.openInBrowser": return zh ? "在浏览器中打开" : "Open in browser";
                case "menu.copyUrl": return zh ? "复制当前地址" : "Copy address";
                case "menu.devtools": return zh ? "开发者工具 (F12)" : "Developer tools (F12)";
                case "menu.openWorkspace": return zh ? "打开工作区" : "Open workspace";
                case "menu.openData": return zh ? "打开数据目录" : "Open data folder";
                case "menu.openIni": return zh ? "打开配置文件" : "Open config file";
                case "menu.update": return zh ? "检查 dsh 更新…" : "Check dsh updates…";
                case "menu.about": return zh ? "关于" : "About";

                case "settings.title": return zh ? "设置" : "Settings";
                case "settings.subtitle": return zh ? "改完点「保存并重启」立即生效" : "Changes apply after Save and restart";
                case "settings.section.start": return zh ? "启动" : "Startup";
                case "settings.section.ui": return zh ? "外观" : "Appearance";
                case "settings.section.paths": return zh ? "目录" : "Folders";
                case "settings.section.advanced": return zh ? "高级" : "Advanced";

                case "settings.server": return zh ? "启动内置 dsh web 服务" : "Start the bundled dsh web server";
                case "settings.server.hint": return zh ? "关掉则仅作客户端，只打开下面的地址" : "Off = client only: it just opens the address below";
                case "settings.url": return zh ? "服务地址" : "Server address";
                case "settings.url.hint": return zh ? "仅客户端模式使用，首次可带 ?token=" : "Client mode only; may carry ?token= the first time";
                            case "settings.host": return zh ? "监听地址" : "Listen address";
                case "settings.host.hint": return zh ? "服务器只绑这个地址" : "The server binds only this address";
                case "settings.port": return zh ? "端口" : "Port";
                case "settings.port.hint": return zh ? "0 = 每次自动挑一个空闲端口" : "0 = pick a free port on every launch";
                                    case "settings.icon": return zh ? "图标" : "Icon";
                case "settings.icon.hint": return zh ? "窗口与托盘图标的样式" : "Window and tray icon style";
                case "settings.zoom": return zh ? "初始缩放" : "Initial zoom";
                case "settings.zoom.hint": return zh ? "0.5 – 2.0" : "0.5 – 2.0";
                case "settings.toolbar": return zh ? "显示地址栏" : "Show address bar";
                case "settings.toolbar.hint": return zh ? "前进 / 后退 / 刷新 / 缩放" : "Back / forward / reload / zoom";
                case "settings.tray": return zh ? "关闭时最小化到托盘" : "Minimise to tray on close";
                case "settings.tray.hint": return zh ? "服务继续运行，双击托盘图标恢复" : "The server keeps running; double-click the tray icon to restore";
                case "settings.telemetry": return zh ? "上报匿名使用统计" : "Send anonymous telemetry";
                case "settings.telemetry.hint": return zh ? "默认关闭，建议保持关闭" : "Off by default; leaving it off is recommended";
                case "settings.workspace": return zh ? "工作区" : "Workspace";
                case "settings.workspace.hint": return zh ? "会话默认从这里开始" : "Sessions start here";
                case "settings.data": return zh ? "数据目录" : "Data folder";
                case "settings.data.hint": return zh ? "配置、密钥、会话、插件（DSH_HOME）" : "Settings, credentials, sessions, plugins (DSH_HOME)";
                case "settings.profile": return zh ? "浏览器数据" : "Browser data";
                case "settings.profile.hint": return zh ? "cookie 与登录态" : "Cookies and login state";
                case "settings.runtime": return zh ? "运行时目录" : "Runtime folder";
                case "settings.runtime.hint": return zh ? "内置 node 与 pnpm" : "Bundled node and pnpm";
                case "settings.app": return zh ? "应用目录" : "App folder";
                case "settings.app.hint": return zh ? "内置 dsh 的安装位置" : "Where the bundled dsh lives";
                case "settings.args": return zh ? "附加参数" : "Extra arguments";
                case "settings.args.hint": return zh ? "追加给 dsh web，通常留空" : "Passed to dsh web; usually empty";
                case "settings.timeout": return zh ? "启动超时" : "Startup timeout";
                case "settings.timeout.hint": return zh ? "等服务的秒数" : "Seconds to wait for the server";
                case "settings.seconds": return zh ? "秒" : "s";

                case "settings.openIni": return zh ? "打开配置文件" : "Open config file";
                case "settings.cancel": return zh ? "取消" : "Cancel";
                case "settings.save": return zh ? "保存" : "Save";
                case "settings.saveRestart": return zh ? "保存并重启" : "Save and restart";
                case "settings.browse": return zh ? "浏览…" : "Browse…";
                case "settings.needUrl": return zh ? "关闭内置服务时必须填写服务地址。" : "A server address is required when the bundled server is off.";
                case "settings.badUrl": return zh ? "服务地址要以 http:// 或 https:// 开头。" : "The address must start with http:// or https://.";
                case "settings.applyAsk": return zh ? "设置已保存。现在重启以生效吗？" : "Settings saved. Restart now to apply them?";
            case "settings.applyNow": return zh ? "立即重启" : "Restart now";
            case "settings.applyLater": return zh ? "稍后" : "Later";
            case "settings.saveFailed": return zh ? "保存失败" : "Could not save";

                case "update.title": return zh ? "dsh 更新" : "dsh updates";
                case "update.current": return zh ? "当前版本" : "Installed";
            case "update.channel": return zh ? "更新通道" : "Channel";
            case "update.force": return zh ? "强制重装（允许同版本或回退）" : "Force (same or older version)";
            case "update.forceSame": return zh ? "已是最新，仍可强制重装" : "already current; force reinstalls it";
            case "update.forceOlder": return zh ? "所选通道更旧，强制后会回退" : "the channel is older; force would downgrade";
                case "update.latest": return zh ? "最新版本" : "Latest";
                case "update.checking": return zh ? "正在检查…" : "Checking…";
                case "update.upToDate": return zh ? "已是最新版本。" : "Already up to date.";
                case "update.found": return zh ? "发现新版本，可以就地更新。" : "A newer version is available and can be installed in place.";
                case "update.now": return zh ? "立即更新" : "Update now";
                case "update.close": return zh ? "关闭" : "Close";
                case "update.running": return zh ? "正在用 pnpm 更新，请稍候…" : "Updating with pnpm, please wait…";
                case "update.done": return zh ? "更新完成，重启后生效。" : "Update finished; restart to apply it.";
                case "update.failed": return zh ? "更新失败。" : "The update failed.";
                case "update.restartAsk": return zh ? "更新完成，现在重启 DSH Desktop 吗？" : "Update finished. Restart DSH Desktop now?";
            case "settings.icon.default": return zh ? "官方图标" : "Official artwork";
            case "settings.icon.auto": return zh ? "跟随系统主题" : "Follow system theme";
            case "settings.icon.light": return zh ? "黑鲸" : "Dark whale";
            case "settings.icon.dark": return zh ? "白鲸" : "White whale";
            case "about.title": return zh ? "关于 DSH Desktop" : "About DSH Desktop";
            case "about.tagline": return zh ? "便携版 DeepSeek Harness 启动器" : "Portable DeepSeek Harness launcher";
            case "about.version": return zh ? "启动器版本" : "Launcher";
            case "about.dsh": return zh ? "dsh 版本" : "dsh";
            case "about.rights": return zh ? "保留所有权利" : "all rights reserved";
            case "about.copy": return zh ? "复制信息" : "Copy info";
            case "about.copied": return zh ? "已复制" : "Copied";
            case "common.yes": return zh ? "重启" : "Restart";
            case "common.no": return zh ? "稍后" : "Later";
            case "update.updated": return zh ? "已更新到" : "Updated to";
                case "update.noNetwork": return zh ? "访问不了 npm registry，检查网络或代理设置。" : "Cannot reach the npm registry; check the network or proxy settings.";
                case "update.hint": return zh
                    ? "更新用 pnpm 就地替换 app 目录里的包，不下载安装程序；失败时窗口内容不动。"
                    : "The update replaces packages inside the app folder with pnpm; no installer is downloaded. Nothing changes if it fails.";
                case "update.notBundled": return zh ? "这个目录没有可更新的 dsh（server=0 的客户端模式）。" : "This folder has no bundled dsh to update (client mode).";

                case "dl.title": return zh ? "准备 WebView2" : "Preparing WebView2";
                case "dl.body": return zh
                    ? "本机没有装 WebView2 运行时，正在下载一份到程序目录。约 250MB，只在下一次需要时再检查。"
                    : "No WebView2 runtime is installed, so one is being downloaded into the program folder. About 250MB, checked again next launch.";
                case "dl.downloading": return zh ? "正在下载" : "Downloading";
                case "dl.extracting": return zh ? "正在解压…" : "Extracting…";
                case "dl.verifying": return zh ? "正在校验…" : "Verifying…";
                case "dl.failed": return zh ? "下载失败" : "Download failed";
                case "dl.cancel": return zh ? "取消" : "Cancel";
                case "dl.retry": return zh ? "重试" : "Retry";
                case "dl.noNetwork": return zh ? "连不上下载源。" : "Cannot reach the download source.";
                case "dl.hint": return zh ? "取消后仍可用「系统浏览器」模式打开。" : "If you cancel, the system-browser interface still works.";

                case "about.mode": return zh ? "模式" : "Mode";
                case "about.mode.server": return zh ? "内置服务" : "Bundled server";
                case "about.mode.client": return zh ? "仅客户端" : "Client only";
                case "about.node": return zh ? "内置 Node" : "Bundled node";
                case "about.address": return zh ? "服务地址" : "Address";
                case "about.webview": return zh ? "WebView2 内核" : "WebView2";
                case "about.data": return zh ? "数据目录" : "Data folder";
                case "about.ini": return zh ? "配置文件" : "Config file";
                case "about.log": return zh ? "日志" : "Log";
                case "about.project": return zh ? "项目" : "Project";
                case "about.notStarted": return zh ? "未启动" : "not started";

                default: return key;
            }
        }
    }
}
