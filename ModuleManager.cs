using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ZeroBox
{
    internal sealed class ModuleManager
    {
        public const string MirrorPrefix = "https://gh-proxy.org/";

        private readonly string _modulesRoot;
        private readonly List<ToolModule> _modules;
        private readonly bool _demoMode;

        public string ModulesRoot { get { return _modulesRoot; } }
        public IList<ToolModule> Modules { get { return _modules; } }

        static ModuleManager()
        {
            try
            {
                // Enforce TLS 1.2 + TLS 1.3 on Windows 10/11 .NET 4.x
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }
        }

        public ModuleManager(string modulesRoot, bool demoMode)
        {
            _modulesRoot = modulesRoot;
            _demoMode = demoMode;
            _modules = ToolModule.BuildDefaultCatalog();

            if (demoMode) ApplyDemoStates();
            else ScanLocalInstallations();
        }

        private void ApplyDemoStates()
        {
            // Realistic showcase state: 4 installed (1 with update!), 2 available
            SetDemoMod(0, ModuleState.Installed, "v1.3.3", "v1.3.3", 87552);
            SetDemoMod(1, ModuleState.NotInstalled, "", "v1.1.3", 0);
            SetDemoMod(2, ModuleState.UpdateAvailable, "v2.0.4", "v2.0.5", 283648);
            SetDemoMod(3, ModuleState.Installed, "v1.2.0", "v1.2.0", 1464118);
            SetDemoMod(4, ModuleState.Installed, "v1.0.3", "v1.0.3", 107520);
            SetDemoMod(5, ModuleState.NotInstalled, "", "v1.0.3", 0);
        }

        private void SetDemoMod(int idx, ModuleState st, string instVer, string latVer, long bytes)
        {
            ToolModule m = _modules[idx];
            m.State = st;
            m.InstalledVersion = instVer;
            m.LatestVersion = latVer;
            m.InstalledBytes = bytes;
            if (bytes > 0) m.InstalledExePath = Path.Combine(GetModuleDir(m), m.ExeName);
        }

        public string GetModuleDir(ToolModule m)
        {
            return Path.Combine(_modulesRoot, m.RepoName);
        }

        public void ScanLocalInstallations()
        {
            if (_demoMode) return;
            for (int i = 0; i < _modules.Count; i++)
            {
                ToolModule m = _modules[i];
                if (m.State == ModuleState.Downloading) continue;

                string dir = GetModuleDir(m);
                string exe = FindExecutableInDir(dir, m.ExeName);
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    m.InstalledExePath = exe;
                    m.InstalledVersion = ReadInstalledVersionFile(dir);
                    m.InstalledBytes = ComputeDirSize(dir);
                    m.State = IsUpdateNewer(m.InstalledVersion, m.LatestVersion)
                        ? ModuleState.UpdateAvailable
                        : ModuleState.Installed;
                }
                else
                {
                    m.InstalledExePath = "";
                    m.InstalledVersion = "";
                    m.InstalledBytes = 0;
                    m.State = ModuleState.NotInstalled;
                }
            }
        }

        public static string FindExecutableInDir(string dir, string exeName)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return "";
            string direct = Path.Combine(dir, exeName);
            if (File.Exists(direct)) return direct;
            try
            {
                string[] sub = Directory.GetFiles(dir, exeName, SearchOption.AllDirectories);
                if (sub != null && sub.Length > 0) return sub[0];
            }
            catch { }
            return "";
        }

        private static string ReadInstalledVersionFile(string dir)
        {
            try
            {
                string vf = Path.Combine(dir, ".version");
                if (File.Exists(vf)) return File.ReadAllText(vf, Encoding.UTF8).Trim();
            }
            catch { }
            return "installed";
        }

        private static void WriteInstalledVersionFile(string dir, string version)
        {
            try { File.WriteAllText(Path.Combine(dir, ".version"), (version ?? "").Trim(), Encoding.UTF8); }
            catch { }
        }

        private static long ComputeDirSize(string dir)
        {
            long sum = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { sum += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return sum;
        }

        public static bool IsUpdateNewer(string installed, string latest)
        {
            if (string.IsNullOrEmpty(latest)) return false;
            if (string.IsNullOrEmpty(installed) || installed == "installed") return false;
            Version vi, vl;
            if (TryParseTagVersion(installed, out vi) && TryParseTagVersion(latest, out vl))
                return vl > vi;
            return !string.Equals(installed.Trim(), latest.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryParseTagVersion(string tag, out Version ver)
        {
            ver = null;
            if (string.IsNullOrEmpty(tag)) return false;
            string clean = tag.Trim();
            if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(1);
            int dash = clean.IndexOf('-');
            if (dash > 0) clean = clean.Substring(0, dash);
            return Version.TryParse(clean, out ver);
        }

        public static string WrapMirrorUrl(string url, bool useMirror)
        {
            if (!useMirror || string.IsNullOrEmpty(url)) return url ?? "";
            if (url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                return MirrorPrefix + url;
            return url;
        }

        // Queries GitHub Releases API for latest version, asset URL & SHA-256 digest;
        // gracefully falls back to non-rate-limited github.com /releases/latest redirect if API returns 403/429!
        public bool FetchLatestMetadata(ToolModule m, bool useMirror, out string errorMsg)
        {
            errorMsg = "";
            if (m == null) return false;
            if (_demoMode) return true;

            string apiUrl = "https://api.github.com/repos/" + m.RepoOwner + "/" + m.RepoName + "/releases/latest";
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(apiUrl);
                req.UserAgent = "ZeroBox/1.0 (https://github.com/rodvay/ZeroBox)";
                req.Accept = "application/vnd.github+json";
                req.Timeout = 10000;
                req.ReadWriteTimeout = 10000;
                req.Proxy = WebRequest.DefaultWebProxy;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream st = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(st, Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    if (ParseGitHubReleaseJson(json, m))
                    {
                        RefreshModuleStateAfterMeta(m);
                        return true;
                    }
                }
            }
            catch (WebException wex)
            {
                // If API rate-limited (403/429) or unreachable, try non-rate-limited redirect fallback
                if (TryFetchViaRedirectFallback(m, useMirror))
                {
                    RefreshModuleStateAfterMeta(m);
                    return true;
                }
                errorMsg = wex.Message;
                return false;
            }
            catch (Exception ex)
            {
                if (TryFetchViaRedirectFallback(m, useMirror))
                {
                    RefreshModuleStateAfterMeta(m);
                    return true;
                }
                errorMsg = ex.Message;
                return false;
            }
            return false;
        }

        internal static bool ParseGitHubReleaseJson(string json, ToolModule m)
        {
            if (string.IsNullOrEmpty(json) || m == null) return false;
            try
            {
                JavaScriptSerializer jss = new JavaScriptSerializer();
                Dictionary<string, object> root = jss.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return false;

                string tag = root.ContainsKey("tag_name") ? Convert.ToString(root["tag_name"]) : "";
                if (string.IsNullOrEmpty(tag)) return false;

                object[] assets = root.ContainsKey("assets") ? root["assets"] as object[] : null;
                string bestUrl = "";
                string bestDigest = "";
                long bestSize = 0;

                if (assets != null)
                {
                    for (int i = 0; i < assets.Length; i++)
                    {
                        Dictionary<string, object> a = assets[i] as Dictionary<string, object>;
                        if (a == null) continue;
                        string name = a.ContainsKey("name") ? Convert.ToString(a["name"]) : "";
                        string dl = a.ContainsKey("browser_download_url") ? Convert.ToString(a["browser_download_url"]) : "";
                        string dig = a.ContainsKey("digest") ? Convert.ToString(a["digest"]) : "";
                        long sz = 0;
                        if (a.ContainsKey("size") && a["size"] != null)
                            long.TryParse(Convert.ToString(a["size"]), out sz);

                        bool match = m.IsZipArchive
                            ? name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                            : string.Equals(name, m.ExeName, StringComparison.OrdinalIgnoreCase);

                        if (match && !string.IsNullOrEmpty(dl))
                        {
                            bestUrl = dl;
                            bestSize = sz;
                            if (!string.IsNullOrEmpty(dig) && dig.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                                bestDigest = dig.Substring(7).Trim().ToLowerInvariant();
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(bestUrl)) return false;
                m.LatestVersion = tag.Trim();
                m.DownloadUrl = bestUrl;
                m.ExpectedSha256 = bestDigest;
                m.RemoteBytes = bestSize;
                return true;
            }
            catch { return false; }
        }

        private static bool TryFetchViaRedirectFallback(ToolModule m, bool useMirror)
        {
            try
            {
                string url = WrapMirrorUrl("https://github.com/" + m.RepoOwner + "/" + m.RepoName + "/releases/latest", useMirror);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "ZeroBox/1.0";
                req.AllowAutoRedirect = false;
                req.Timeout = 10000;
                req.Proxy = WebRequest.DefaultWebProxy;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    string loc = resp.Headers["Location"];
                    if (!string.IsNullOrEmpty(loc))
                    {
                        int idx = loc.LastIndexOf("/tag/", StringComparison.OrdinalIgnoreCase);
                        if (idx > 0)
                        {
                            string tag = loc.Substring(idx + 5).Trim('/');
                            if (!string.IsNullOrEmpty(tag))
                            {
                                string assetName = m.IsZipArchive ? (m.RepoName + "-" + tag + "-Portable.zip") : m.ExeName;
                                m.LatestVersion = tag;
                                m.DownloadUrl = "https://github.com/" + m.RepoOwner + "/" + m.RepoName + "/releases/download/" + tag + "/" + assetName;
                                m.ExpectedSha256 = "";
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private void RefreshModuleStateAfterMeta(ToolModule m)
        {
            if (m.State == ModuleState.Downloading) return;
            if (!string.IsNullOrEmpty(m.InstalledExePath) && File.Exists(m.InstalledExePath))
            {
                m.State = IsUpdateNewer(m.InstalledVersion, m.LatestVersion)
                    ? ModuleState.UpdateAvailable
                    : ModuleState.Installed;
            }
            else
            {
                m.State = ModuleState.NotInstalled;
            }
        }

        public static bool IsModuleRunning(ToolModule m)
        {
            if (m == null || string.IsNullOrEmpty(m.InstalledExePath)) return false;
            string procName = Path.GetFileNameWithoutExtension(m.ExeName);
            Process[] procs = null;
            try
            {
                procs = Process.GetProcessesByName(procName);
                for (int i = 0; i < procs.Length; i++)
                {
                    try
                    {
                        // Any process with matching name could lock the binary if path matches
                        string path = "";
                        try { path = procs[i].MainModule.FileName; } catch { }
                        if (string.IsNullOrEmpty(path) || string.Equals(path, m.InstalledExePath, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    finally { procs[i].Dispose(); }
                }
            }
            catch { }
            return false;
        }

        public bool DownloadAndInstallModule(ToolModule m, bool useMirror, Action<int> onProgress, out string errorMsg)
        {
            errorMsg = "";
            if (m == null) { errorMsg = "Invalid module."; return false; }

            if (_demoMode)
            {
                if (onProgress != null) onProgress(50);
                m.InstalledVersion = string.IsNullOrEmpty(m.LatestVersion) ? m.FallbackTag : m.LatestVersion;
                m.LatestVersion = m.InstalledVersion;
                m.InstalledExePath = Path.Combine(GetModuleDir(m), m.ExeName);
                m.InstalledBytes = m.IsZipArchive ? 1464118 : 102400;
                m.State = ModuleState.Installed;
                if (onProgress != null) onProgress(100);
                return true;
            }

            if (IsModuleRunning(m))
            {
                errorMsg = "RUNNING";
                return false;
            }

            if (string.IsNullOrEmpty(m.DownloadUrl))
            {
                string metaErr;
                if (!FetchLatestMetadata(m, useMirror, out metaErr) || string.IsNullOrEmpty(m.DownloadUrl))
                {
                    errorMsg = string.IsNullOrEmpty(metaErr) ? "Unable to resolve latest release URL." : metaErr;
                    return false;
                }
            }

            string modDir = GetModuleDir(m);
            if (!Directory.Exists(modDir)) Directory.CreateDirectory(modDir);

            string tmpFile = Path.Combine(modDir, ".download_" + Guid.NewGuid().ToString("N") + (m.IsZipArchive ? ".zip" : ".exe"));
            try
            {
                string dlUrl = WrapMirrorUrl(m.DownloadUrl, useMirror);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(dlUrl);
                req.UserAgent = "ZeroBox/1.0 (https://github.com/rodvay/ZeroBox)";
                req.Timeout = 20000;
                req.ReadWriteTimeout = 30000;
                req.Proxy = WebRequest.DefaultWebProxy;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream src = resp.GetResponseStream())
                using (FileStream dst = new FileStream(tmpFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    long total = resp.ContentLength > 0 ? resp.ContentLength : m.RemoteBytes;
                    byte[] buf = new byte[16384];
                    long readSum = 0;
                    int lastPct = -1;
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        readSum += n;
                        if (total > 0 && onProgress != null)
                        {
                            int pct = Math.Max(1, Math.Min(92, (int)((readSum * 92L) / total)));
                            if (pct != lastPct) { lastPct = pct; onProgress(pct); }
                        }
                    }
                }

                // Cryptographic SHA-256 verification when GitHub API supplied official digest
                if (!string.IsNullOrEmpty(m.ExpectedSha256))
                {
                    string actualHash = ComputeFileSha256(tmpFile);
                    if (!string.Equals(actualHash, m.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        errorMsg = "SHA-256 verification mismatch! Download aborted for safety.";
                        return false;
                    }
                }
                if (onProgress != null) onProgress(96);

                if (m.IsZipArchive)
                {
                    // Clean old files (except tmpFile) then safe Zip-Slip-guarded extract
                    CleanDirExcept(modDir, tmpFile);
                    ExtractZipSafely(tmpFile, modDir);
                    File.Delete(tmpFile);
                }
                else
                {
                    string targetExe = Path.Combine(modDir, m.ExeName);
                    if (File.Exists(targetExe)) File.Delete(targetExe);
                    File.Move(tmpFile, targetExe);
                }

                WriteInstalledVersionFile(modDir, string.IsNullOrEmpty(m.LatestVersion) ? m.FallbackTag : m.LatestVersion);
                m.InstalledExePath = FindExecutableInDir(modDir, m.ExeName);
                m.InstalledVersion = ReadInstalledVersionFile(modDir);
                m.InstalledBytes = ComputeDirSize(modDir);
                m.State = ModuleState.Installed;
                if (onProgress != null) onProgress(100);
                return !string.IsNullOrEmpty(m.InstalledExePath) && File.Exists(m.InstalledExePath);
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
            }
        }

        // Zip-Slip protected archive extractor (never writes outside targetDir)
        public static void ExtractZipSafely(string zipPath, string targetDir)
        {
            string baseFull = Path.GetFullPath(targetDir);
            if (!baseFull.EndsWith(Path.DirectorySeparatorChar.ToString()))
                baseFull += Path.DirectorySeparatorChar;

            using (ZipArchive za = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in za.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
                    string destPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
                    if (!destPath.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Blocked unsafe zip entry path: " + entry.FullName);

                    string parent = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                        Directory.CreateDirectory(parent);
                    entry.ExtractToFile(destPath, true);
                }
            }
        }

        private static void CleanDirExcept(string dir, string keepFile)
        {
            string keepFull = Path.GetFullPath(keepFile);
            foreach (string f in Directory.GetFiles(dir))
            {
                if (string.Equals(Path.GetFullPath(f), keepFull, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(f); } catch { }
            }
            foreach (string d in Directory.GetDirectories(dir))
            {
                try { Directory.Delete(d, true); } catch { }
            }
        }

        public static string ComputeFileSha256(string filePath)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(64);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public bool RemoveModule(ToolModule m, out string errorMsg)
        {
            errorMsg = "";
            if (m == null) return false;
            if (_demoMode)
            {
                m.InstalledExePath = "";
                m.InstalledVersion = "";
                m.InstalledBytes = 0;
                m.State = ModuleState.NotInstalled;
                return true;
            }
            if (IsModuleRunning(m))
            {
                errorMsg = "RUNNING";
                return false;
            }
            try
            {
                string dir = GetModuleDir(m);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                m.InstalledExePath = "";
                m.InstalledVersion = "";
                m.InstalledBytes = 0;
                m.State = ModuleState.NotInstalled;
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                return false;
            }
        }

        public bool LaunchModule(ToolModule m, bool asAdmin, out string errorMsg)
        {
            errorMsg = "";
            if (m == null || string.IsNullOrEmpty(m.InstalledExePath))
            {
                errorMsg = "Module not installed.";
                return false;
            }
            if (_demoMode) return true;
            if (!File.Exists(m.InstalledExePath))
            {
                errorMsg = "Executable missing on disk.";
                ScanLocalInstallations();
                return false;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(m.InstalledExePath);
                psi.WorkingDirectory = Path.GetDirectoryName(m.InstalledExePath);
                psi.UseShellExecute = true;
                if (asAdmin) psi.Verb = "runas";
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                return false;
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 KB";
            double kb = bytes / 1024.0;
            if (kb < 1024.0) return Loc.F("{0:0.0} KB", kb);
            double mb = kb / 1024.0;
            return Loc.F("{0:0.00} MB", mb);
        }
    }
}
