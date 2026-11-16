// Locates a WebView2 runtime for the window: a copy already inside this folder,
// else the one installed on the machine, else a fixed-version copy downloaded
// into this folder. The download keeps the no-install promise -- nothing is
// registered, nothing needs administrator rights, deleting the folder removes it.

using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;
using Microsoft.Web.WebView2.Core;

namespace DshDesktop
{
    internal sealed class RuntimeProgress
    {
        public volatile bool Cancelled;
        public volatile string Stage = "";
        public volatile int Percent;
        public volatile string Detail = "";
        public volatile bool Failed;
        public volatile bool Done;          // set only after Ensure() has returned
        public volatile string Error;
        public volatile string Result;      // folder to use, or null for the system runtime

        public void Report(string stage, int percent, string detail)
        {
            Stage = stage;
            Percent = percent;
            Detail = detail;
        }
    }

    internal static class WebViewRuntime
    {
        // Pinned so a fresh machine gets a runtime this build is known to work
        // with; the ini key lets an operator move it forward deliberately.
        public const string DefaultVersion = "157.0.4341.46";

        private const string NuGetRoot = "https://api.nuget.org/v3-flatcontainer";

        static WebViewRuntime()
        {
            // .NET 4.x still negotiates an old default TLS version on many
            // machines, and nuget.org requires 1.2.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
        }

        public static string LocalDir
        {
            get { return Path.Combine(App.BaseDir, "webview2"); }
        }

        public static bool LocalValid(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            return File.Exists(Path.Combine(dir, "msedgewebview2.exe"))
                && File.Exists(Path.Combine(dir, "msedge.dll"));
        }

        public static bool SystemAvailable()
        {
            // Diagnostic hook: pretends the machine has no runtime so the download
            // path can be exercised on a machine that does have one.
            if (string.Equals(Environment.GetEnvironmentVariable("DSH_DESKTOP_NO_WEBVIEW2"), "1", StringComparison.Ordinal))
            {
                App.Log("DSH_DESKTOP_NO_WEBVIEW2=1: pretending the machine has no WebView2");
                return false;
            }
            try
            {
                string version = CoreWebView2Environment.GetAvailableBrowserVersionString(null);
                return !string.IsNullOrEmpty(version) && version != "?";
            }
            catch { return false; }
        }

        public static string SystemVersion()
        {
            try { return CoreWebView2Environment.GetAvailableBrowserVersionString(null) ?? "?"; }
            catch { return "?"; }
        }

        public static string Version(string folder)
        {
            try { return CoreWebView2Environment.GetAvailableBrowserVersionString(folder) ?? "?"; }
            catch { return "?"; }
        }

        // Returns the folder to pass to WebView2, or null to use the installed
        // runtime. Blocks; call it off the UI thread and poll the progress object.
        public static string Ensure(RuntimeProgress progress, bool forceDownload)
        {
            string local = LocalDir;
            if (forceDownload)
            {
                Download(progress);
                return local;
            }
            if (LocalValid(local))
            {
                progress.Report("local", 100, Version(local));
                return local;
            }
            if (SystemAvailable())
            {
                progress.Report("system", 100, SystemVersion());
                return null;
            }
            Download(progress);
            return local;
        }

        private static void Download(RuntimeProgress progress)
        {
            string version = App.S == null || string.IsNullOrWhiteSpace(App.S.WebView2Version)
                ? DefaultVersion
                : App.S.WebView2Version.Trim();
            string temp = Path.Combine(App.BaseDir, "webview2-download");
            string staging = Path.Combine(App.BaseDir, "webview2-partial");

            try
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Directory.CreateDirectory(temp);
                Directory.CreateDirectory(staging);

                string shell = Path.Combine(temp, "shell.nupkg");
                string core = Path.Combine(temp, "core.nupkg");
                string shellUrl = string.Format(CultureInfo.InvariantCulture,
                    "{0}/webview2.runtime.x64/{1}/webview2.runtime.x64.{1}.nupkg", NuGetRoot, version);
                string coreUrl = string.Format(CultureInfo.InvariantCulture,
                    "{0}/webview2.runtime.x64.core/{1}/webview2.runtime.x64.core.{1}.nupkg", NuGetRoot, version);

                progress.Report("download", 2, "webview2.runtime.x64 " + version);
                Fetch(shellUrl, shell, progress, 2, 38);
                if (progress.Cancelled) return;

                progress.Report("download", 40, "webview2.runtime.x64.core " + version);
                Fetch(coreUrl, core, progress, 40, 92);
                if (progress.Cancelled) return;

                progress.Report("extract", 94, version);
                ExtractPackage(shell, staging, null);
                ExtractPackage(core, staging, new string[] { "msedge.dll" });
                if (progress.Cancelled) return;

                // The shell package ships the real side-by-side assembly manifest;
                // an empty stand-in makes Windows fail with "side-by-side
                // configuration is incorrect", so require the genuine one.
                if (Directory.GetFiles(staging, "*.manifest", SearchOption.TopDirectoryOnly).Length == 0)
                    throw new InvalidOperationException("the package did not contain the runtime manifest");
                if (!LocalValid(staging))
                    throw new InvalidOperationException("downloaded runtime is incomplete");

                progress.Report("install", 98, version);
                string target = LocalDir;
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(staging, target);
                progress.Report("done", 100, Version(target));
            }
            catch (Exception ex)
            {
                progress.Failed = true;
                progress.Error = ex.Message;
                App.Log("webview2 download failed: " + ex);
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }
        }

        private static void ExtractPackage(string package, string destination, string[] only)
        {
            using (ZipArchive archive = ZipFile.OpenRead(package))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (name.EndsWith("/")) continue;
                    int marker = name.IndexOf("contentFiles/any/any/WebView2/", StringComparison.OrdinalIgnoreCase);
                    if (marker < 0) continue;
                    string relative = name.Substring(marker + "contentFiles/any/any/WebView2/".Length);
                    if (relative.Length == 0) continue;
                    if (only != null)
                    {
                        bool keep = false;
                        foreach (string wanted in only)
                        {
                            if (string.Equals(relative, wanted, StringComparison.OrdinalIgnoreCase)) { keep = true; break; }
                        }
                        if (!keep) continue;
                    }
                    string target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
                    string directory = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    entry.ExtractToFile(target, true);
                }
            }
        }

        private static void Fetch(string url, string destination, RuntimeProgress progress, int from, int to)
        {
            if (progress.Cancelled) return;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "DSH Desktop";
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            request.AllowAutoRedirect = true;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream input = response.GetResponseStream())
            using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                long total = response.ContentLength;
                byte[] buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (progress.Cancelled) return;
                    output.Write(buffer, 0, read);
                    copied += read;
                    if (total > 0)
                    {
                        int percent = from + (int)((to - from) * (double)copied / total);
                        progress.Report("download", Math.Min(to, percent),
                            string.Format(CultureInfo.InvariantCulture, "{0:N1} / {1:N1} MB",
                                copied / 1048576.0, total / 1048576.0));
                    }
                }
            }
        }
    }
}
