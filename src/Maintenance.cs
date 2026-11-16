// Maintenance of the bundled dsh installation: version queries, in-place updates
// through pnpm, and re-applying the same payload pruning the build script does so
// an updated folder stays as small as a freshly built one.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DshDesktop
{
    internal static class Maintenance
    {
        private const string RegistryUrl = "https://registry.npmjs.org/@deepseek-ai/dsh";

        static Maintenance()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
        }

        public static string AppDir
        {
            get { return App.ResolveDir(App.S != null ? App.S.AppDir : "app", "app"); }
        }

        public static string RuntimeDir
        {
            get { return App.ResolveDir(App.S != null ? App.S.RuntimeDir : "runtime", "runtime"); }
        }

        public static string ManifestPath
        {
            get { return Path.Combine(AppDir, "node_modules", "@deepseek-ai", "dsh", "package.json"); }
        }

        public static string InstalledVersion()
        {
            try
            {
                string text = File.ReadAllText(ManifestPath);
                Match m = Regex.Match(text, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) return m.Groups[1].Value;
            }
            catch { }
            return "?";
        }

        // Highest version published under any dist-tag. The npm "latest" tag can
        // sit behind "next" during a preview series (0.2.2-rc.1 vs -rc.3), so
        // following only "latest" would silently downgrade a build.
        public static string LatestVersion()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(RegistryUrl);
                request.UserAgent = "DSH Desktop";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.Accept = "application/json";
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    Match tags = Regex.Match(body, "\"dist-tags\"\\s*:\\s*\\{([^}]*)\\}");
                    if (!tags.Success) return null;
                    string best = null;
                    foreach (Match tag in Regex.Matches(tags.Groups[1].Value, "\"([^\"]+)\"\\s*:\\s*\"([^\"]+)\""))
                    {
                        string version = tag.Groups[2].Value;
                        if (best == null || Compare(version, best) > 0) best = version;
                    }
                    return best;
                }
            }
            catch (Exception ex) { App.Log("latest version lookup failed: " + ex.Message); }
            return null;
        }

        // One named dist-tag, e.g. "latest" or "next".
        public static string TagVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(RegistryUrl);
                request.UserAgent = "DSH Desktop";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.Accept = "application/json";
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    Match tags = Regex.Match(body, "\"dist-tags\"\\s*:\\s*\\{([^}]*)\\}");
                    if (!tags.Success) return null;
                    Match hit = Regex.Match(tags.Groups[1].Value,
                        "\"" + Regex.Escape(tag.Trim()) + "\"\\s*:\\s*\"([^\"]+)\"");
                    if (hit.Success) return hit.Groups[1].Value;
                }
            }
            catch (Exception ex) { App.Log("dist-tag lookup failed: " + ex.Message); }
            return null;
        }

        // The version npm marks as `latest`. Updates deliberately look at every tag so
        // a preview can be offered, but a fresh install takes the published default.
        public static string LatestTagVersion()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(RegistryUrl);
                request.UserAgent = "DSH Desktop";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.Accept = "application/json";
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    Match tags = Regex.Match(body, "\"dist-tags\"\\s*:\\s*\\{([^}]*)\\}");
                    if (!tags.Success) return null;
                    Match latest = Regex.Match(tags.Groups[1].Value, "\"latest\"\\s*:\\s*\"([^\"]+)\"");
                    if (latest.Success) return latest.Groups[1].Value;
                }
            }
            catch (Exception ex) { App.Log("latest tag lookup failed: " + ex.Message); }
            return null;
        }

        // "0.2.2-rc.3" > "0.2.2-rc.1" > "0.2.2" is false; a release outranks its
        // own pre-releases, and numbers compare numerically.
        public static int Compare(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.IsNullOrEmpty(a)) return -1;
            if (string.IsNullOrEmpty(b)) return 1;

            string coreA = a, preA = "";
            string coreB = b, preB = "";
            int dashA = a.IndexOf('-');
            if (dashA >= 0) { coreA = a.Substring(0, dashA); preA = a.Substring(dashA + 1); }
            int dashB = b.IndexOf('-');
            if (dashB >= 0) { coreB = b.Substring(0, dashB); preB = b.Substring(dashB + 1); }

            string[] partsA = coreA.Split('.');
            string[] partsB = coreB.Split('.');
            int length = Math.Max(partsA.Length, partsB.Length);
            for (int i = 0; i < length; i++)
            {
                int va = i < partsA.Length ? ParseInt(partsA[i]) : 0;
                int vb = i < partsB.Length ? ParseInt(partsB[i]) : 0;
                if (va != vb) return va < vb ? -1 : 1;
            }

            if (preA.Length == 0 && preB.Length == 0) return 0;
            if (preA.Length == 0) return 1;
            if (preB.Length == 0) return -1;

            string[] pa = preA.Split('.');
            string[] pb = preB.Split('.');
            int plen = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < plen; i++)
            {
                string sa = i < pa.Length ? pa[i] : "";
                string sb = i < pb.Length ? pb[i] : "";
                int na, nb;
                bool numA = int.TryParse(sa, out na);
                bool numB = int.TryParse(sb, out nb);
                if (numA && numB)
                {
                    if (na != nb) return na < nb ? -1 : 1;
                }
                else
                {
                    int cmp = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp < 0 ? -1 : 1;
                }
            }
            return 0;
        }

        private static int ParseInt(string value)
        {
            int result;
            return int.TryParse(value, out result) ? result : 0;
        }

        // The portable package ships without dsh: the first launch fetches it, and
        // an update reuses the same code path.
        public static bool HasApp()
        {
            return File.Exists(Path.Combine(AppDir, "node_modules", "@deepseek-ai", "dsh", "package.json"));
        }

        // Removals a fresh install should apply too, so a first-run tree ends up
        // exactly as small as a built one.
        private const string PruneList = @"# Removed when dsh is installed; the launcher's
# ""update dsh"" re-applies this list after a pnpm update. Only payload that cannot
# run on this platform is listed: the Windows x64 engines stay, the WASM builds and
# the macOS/Linux/ARM/ia32 binaries go.
@deepseek-ai/libreoffice-kit-wasm
@deepseek-ai/libreoffice-kit-darwin-arm64
@deepseek-ai/libreoffice-kit-darwin-x64
@deepseek-ai/libreoffice-kit-win32-arm64
sherpa-onnx-win-ia32
sherpa-onnx-darwin-arm64
sherpa-onnx-darwin-x64
sherpa-onnx-linux-arm64
sherpa-onnx-linux-x64
node-pty/prebuilds/darwin-arm64
node-pty/prebuilds/darwin-x64
node-pty/prebuilds/linux-arm64
node-pty/prebuilds/linux-x64
node-pty/prebuilds/win32-arm64
@vscode/ripgrep-darwin-arm64
@vscode/ripgrep-darwin-x64
@vscode/ripgrep-linux-arm
@vscode/ripgrep-linux-arm64
@vscode/ripgrep-linux-ia32
@vscode/ripgrep-linux-ppc64
@vscode/ripgrep-linux-riscv64
@vscode/ripgrep-linux-s390x
@vscode/ripgrep-linux-x64
@vscode/ripgrep-win32-arm64
@vscode/ripgrep-win32-ia32
@deepseek-ai/node-addon-system-darwin-arm64
@deepseek-ai/node-addon-system-darwin-x64
@deepseek-ai/node-addon-system-linux-arm64
@deepseek-ai/node-addon-system-linux-x64
node-addon-require-builtin-darwin-arm64
node-addon-require-builtin-darwin-x64
node-addon-require-builtin-linux-arm64-gnu
node-addon-require-builtin-linux-arm64-musl
node-addon-require-builtin-linux-x64-gnu
node-addon-require-builtin-linux-x64-musl
node-addon-require-builtin-win32-arm64-msvc
node-addon-require-builtin-win32-ia32-msvc
@koromix/koffi-darwin-arm64
@koromix/koffi-darwin-x64
@koromix/koffi-linux-arm64
@koromix/koffi-linux-ia32
@koromix/koffi-linux-loong64
@koromix/koffi-linux-riscv64
@koromix/koffi-linux-x64
@koromix/koffi-openbsd-ia32
@koromix/koffi-openbsd-x64
@koromix/koffi-freebsd-arm64
@koromix/koffi-freebsd-ia32
@koromix/koffi-freebsd-x64
@koromix/koffi-win32-arm64
@koromix/koffi-win32-ia32
@img/sharp-darwin-arm64
@img/sharp-darwin-x64
@img/sharp-linux-arm
@img/sharp-linux-arm64
@img/sharp-linux-ppc64
@img/sharp-linux-riscv64
@img/sharp-linux-s390x
@img/sharp-linux-x64
@img/sharp-linuxmusl-arm64
@img/sharp-linuxmusl-x64
@img/sharp-win32-arm64
@img/sharp-win32-ia32
@img/sharp-freebsd-wasm32
@img/sharp-wasm32
@img/sharp-webcontainers-wasm32";

        private static void EnsurePruneList()
        {
            try
            {
                Directory.CreateDirectory(AppDir);
                string path = Path.Combine(AppDir, ".dsh-desktop-prune.txt");
                if (!File.Exists(path)) File.WriteAllText(path, PruneList, new UTF8Encoding(false));
            }
            catch (Exception ex) { App.Log("could not write the prune list: " + ex.Message); }
        }

        // Called before the server starts. A packaged folder has no dsh at all, so
        // the first launch installs the newest published version here.
        public static bool EnsureApp(Action<string> log)
        {
            if (HasApp()) return true;
            EnsurePruneList();
            // npm's `latest` tag: the published default, not the newest preview.
            string version = LatestTagVersion();
            if (string.IsNullOrEmpty(version)) version = LatestVersion();
            if (string.IsNullOrEmpty(version)) version = App.DefaultDshVersion;
            if (log != null) log("dsh is not installed yet; fetching " + version + " (npm latest)");
            return UpdateTo(version, log);
        }

        // in only after it verifies. A plain `pnpm add` in place cannot work here:
        // the tree was installed with whatever store the build machine used, and
        // pnpm refuses to link from a different one -- which is every machine this
        // folder is copied to.
        public static bool UpdateTo(string version, Action<string> log)
        {
            string node = Path.Combine(RuntimeDir, "node.exe");
            if (!File.Exists(node)) node = "node";
            string pnpm = Path.Combine(RuntimeDir, "pnpm", "bin", "pnpm.cjs");
            if (!File.Exists(pnpm))
            {
                if (log != null) log("pnpm is missing under " + Path.Combine(RuntimeDir, "pnpm"));
                return false;
            }

            string dataDir = App.ResolveDir(App.S.Data, "data");
            string staging = Path.Combine(dataDir, "update-staging");
            string retired = Path.Combine(dataDir, "update-old");
            string modules = Path.Combine(AppDir, "node_modules");

            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                if (Directory.Exists(retired)) Directory.Delete(retired, true);
                Directory.CreateDirectory(staging);

                string manifest = "{\"name\":\"dsh-desktop-runtime\",\"private\":true,\"dependencies\":{\"@deepseek-ai/dsh\":\""
                    + version + "\"}}";
                File.WriteAllText(Path.Combine(staging, "package.json"), manifest, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(staging, "pnpm-workspace.yaml"),
                    "packages:\n  - .\nnodeLinker: hoisted\nautoInstallPeers: true\n", new UTF8Encoding(false));

                if (log != null) log("installing @deepseek-ai/dsh@" + version + " into a staging folder");
                if (!RunPnpm(node, pnpm, staging, new string[] { "install", "--prod", "--ignore-scripts" }, log))
                    return false;

                string staged = Path.Combine(staging, "node_modules", "@deepseek-ai", "dsh", "package.json");
                if (!File.Exists(staged))
                {
                    if (log != null) log("staging install did not produce dsh");
                    return false;
                }
                string installed = "?";
                Match m = Regex.Match(File.ReadAllText(staged), "\"version\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) installed = m.Groups[1].Value;
                if (log != null) log("staged version " + installed);
                if (!string.Equals(installed, version, StringComparison.OrdinalIgnoreCase))
                {
                    if (log != null) log("staged version does not match " + version);
                    return false;
                }

                if (log != null) log("swapping node_modules");
                // The running server keeps files open inside the tree, so a swap
                // while it lives fails with "access denied". Retry briefly in case
                // something (a scanner, a lingering child) lets go late.
                Exception last = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        SwapModules(modules, retired, Path.Combine(staging, "node_modules"));
                        last = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        if (log != null) log("swap attempt " + (attempt + 1) + " failed: " + ex.Message);
                        System.Threading.Thread.Sleep(1200);
                    }
                }
                if (last != null) throw last;

                foreach (string file in new string[] { "package.json", "pnpm-workspace.yaml", "pnpm-lock.yaml" })
                {
                    string source = Path.Combine(staging, file);
                    if (File.Exists(source)) File.Copy(source, Path.Combine(AppDir, file), true);
                }
            }
            catch (Exception ex)
            {
                App.Log("update failed: " + ex);
                if (log != null) log(ex.Message);
                return false;
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }

            Prune(log);

            if (log != null) log("installed " + InstalledVersion());
            return true;
        }

        // Swap with a rollback: the old tree is only discarded once the new one is
        // in place, and a failed move never leaves the app without node_modules.
        private static void SwapModules(string modules, string retired, string staged)
        {
            bool movedOld = false;
            if (Directory.Exists(retired)) Directory.Delete(retired, true);
            if (Directory.Exists(modules))
            {
                Directory.Move(modules, retired);
                movedOld = true;
            }
            try
            {
                Directory.Move(staged, modules);
            }
            catch
            {
                if (movedOld && !Directory.Exists(modules))
                {
                    try { Directory.Move(retired, modules); } catch { }
                }
                throw;
            }
            try { if (Directory.Exists(retired)) Directory.Delete(retired, true); } catch { }
        }

        private static bool RunPnpm(string node, string pnpm, string workingDirectory, string[] arguments, Action<string> log)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            StringBuilder quoted = new StringBuilder();
            quoted.Append('"').Append(pnpm).Append('"');
            foreach (string argument in arguments) quoted.Append(' ').Append(argument);
            psi.FileName = node;
            psi.Arguments = quoted.ToString();
            psi.WorkingDirectory = workingDirectory;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.EnvironmentVariables["PATH"] = RuntimeDir + ";" + path;
            psi.EnvironmentVariables["CI"] = "1";

            App.Log("update: " + psi.FileName + " " + psi.Arguments + " (cwd " + workingDirectory + ")");
            if (log != null) log("$ pnpm " + string.Join(" ", arguments));

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = psi;
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null && log != null) log(e.Data);
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null && log != null) log(e.Data);
                    };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(900000))
                    {
                        try { process.Kill(); } catch { }
                        if (log != null) log("timed out after 15 minutes");
                        return false;
                    }
                    if (process.ExitCode != 0)
                    {
                        if (log != null) log("exit code " + process.ExitCode.ToString(CultureInfo.InvariantCulture));
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                App.Log("pnpm failed: " + ex);
                if (log != null) log(ex.Message);
                return false;
            }
        }

        // The build script records exactly what it removed in app\.dsh-desktop-prune.txt,
        // so an in-place update trims the same payload instead of drifting.
        public static void Prune(Action<string> log)
        {
            string list = Path.Combine(AppDir, ".dsh-desktop-prune.txt");
            if (!File.Exists(list)) return;
            string modules = Path.Combine(AppDir, "node_modules");
            long saved = 0;
            int removed = 0;
            try
            {
                foreach (string raw in File.ReadAllLines(list))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    string target = Path.Combine(modules, line.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(target) && !Directory.Exists(target)) continue;
                    try
                    {
                        long size = 0;
                        if (Directory.Exists(target))
                        {
                            foreach (string file in Directory.GetFiles(target, "*", SearchOption.AllDirectories))
                            {
                                try { size += new FileInfo(file).Length; } catch { }
                            }
                            Directory.Delete(target, true);
                        }
                        else
                        {
                            size = new FileInfo(target).Length;
                            File.Delete(target);
                        }
                        saved += size;
                        removed++;
                    }
                    catch { }
                }
            }
            catch (Exception ex) { App.Log("prune failed: " + ex.Message); }

            // Source maps are only read by browser devtools, and debug symbols only by
            // a native stack walker: together 121MB that normal use never touches.
            try
            {
                foreach (string file in Directory.GetFiles(modules, "*.map", SearchOption.AllDirectories))
                {
                    try
                    {
                        long size = new FileInfo(file).Length;
                        File.Delete(file);
                        saved += size;
                    }
                    catch { }
                }
                foreach (string file in Directory.GetFiles(modules, "*.pdb", SearchOption.AllDirectories))
                {
                    try
                    {
                        long size = new FileInfo(file).Length;
                        File.Delete(file);
                        saved += size;
                    }
                    catch { }
                }
            }
            catch (Exception ex) { App.Log("map sweep failed: " + ex.Message); }

            // Package-manager bookkeeping would pin a store path this machine lacks.
            try
            {
                foreach (string relative in new string[]
                {
                    ".bin", ".pnpm", ".modules.yaml", ".pnpm-workspace-state-v1.json"
                })
                {
                    string target = Path.Combine(modules, relative);
                    try
                    {
                        if (Directory.Exists(target)) Directory.Delete(target, true);
                        else if (File.Exists(target)) File.Delete(target);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { App.Log("cleanup sweep failed: " + ex.Message); }

            if (removed > 0 || saved > 0)
            {
                string message = string.Format(CultureInfo.InvariantCulture,
                    "pruned {0} paths this platform cannot run, {1:N0} MB", removed, saved / 1048576.0);
                if (log != null) log(message);
                else App.Log(message);
            }
        }
    }
}
