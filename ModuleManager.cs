using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        // Ordered failover mirror list (ghfast.top preserves 302 /releases/latest redirects + fast CDN!)
        public static readonly string[] MirrorPrefixes = new string[]
        {
            "https://ghfast.top/",
            "https://gh-proxy.org/",
            "https://gh-proxy.com/"
        };

        public const string MirrorPrefix = "https://ghfast.top/";
        private const int MinValidBinaryBytes = 8192;
        private const int ErrorCancelledWin32 = 1223;

        private readonly string _modulesRoot;
        private readonly List<ToolModule> _modules;
        private readonly bool _demoMode;
        private volatile bool _cancelRequested;

        public string ModulesRoot { get { return _modulesRoot; } }
        public IList<ToolModule> Modules { get { return _modules; } }

        static ModuleManager()
        {
            try
            {
                // Enforce TLS 1.2 + TLS 1.3 and allow parallel GitHub/mirror checks
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.DefaultConnectionLimit = 12;
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

        public void RequestCancelAll()
        {
            _cancelRequested = true;
        }

        private void ApplyDemoStates()
        {
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

        public string GetOpenFolderPath(ToolModule m)
        {
            if (m != null && !string.IsNullOrEmpty(m.InstalledExePath) && File.Exists(m.InstalledExePath))
            {
                string d = Path.GetDirectoryName(m.InstalledExePath);
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d;
            }
            return GetModuleDir(m);
        }

        public void ScanLocalInstallations()
        {
            if (_demoMode) return;
            for (int i = 0; i < _modules.Count; i++)
                ScanSingleModule(_modules[i]);
        }

        public void ScanSingleModule(ToolModule m)
        {
            if (_demoMode || m == null) return;
            if (m.State == ModuleState.Downloading) return;

            string dir = GetModuleDir(m);
            SweepStaleTempArtifacts(dir);

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

        private static void SweepStaleTempArtifacts(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            try
            {
                foreach (string f in Directory.GetFiles(dir, ".download_*"))
                {
                    try { File.Delete(f); } catch { }
                }
                foreach (string d in Directory.GetDirectories(dir, ".stage_*"))
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }
            catch { }
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
                    string fn = Path.GetFileName(f);
                    if (fn != null && fn.StartsWith(".download_", StringComparison.OrdinalIgnoreCase)) continue;
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
            return WrapMirrorUrlWithPrefix(url, useMirror ? MirrorPrefix : "");
        }

        public static string WrapMirrorUrlWithPrefix(string url, string prefix)
        {
            if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(url)) return url ?? "";
            if (url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                return prefix + url;
            return url;
        }

        public static bool ValidateDownloadedBinary(string filePath, bool isZip, out string reason)
        {
            reason = "";
            try
            {
                if (!File.Exists(filePath)) { reason = "Downloaded file missing."; return false; }
                FileInfo fi = new FileInfo(filePath);
                if (fi.Length < MinValidBinaryBytes)
                {
                    reason = Loc.F("File too small ({0} B) — possible proxy error page.", fi.Length);
                    return false;
                }
                byte[] hdr = new byte[4];
                using (FileStream fs = File.OpenRead(filePath))
                {
                    if (fs.Read(hdr, 0, 4) < 4) { reason = "Incomplete header."; return false; }
                }
                if (isZip)
                {
                    // ZIP local file header starts with PK\x03\x04
                    if (hdr[0] != 0x50 || hdr[1] != 0x4B)
                    {
                        reason = "Invalid ZIP header (mirror returned non-ZIP content).";
                        return false;
                    }
                }
                else
                {
                    // Windows PE executable starts with 'MZ' (0x4D 0x5A)
                    if (hdr[0] != 0x4D || hdr[1] != 0x5A)
                    {
                        reason = "Invalid EXE header (mirror returned non-PE content).";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        public bool FetchLatestMetadata(ToolModule m, bool useMirror, out string errorMsg)
        {
            bool apiHint = false;
            return FetchLatestMetadata(m, useMirror, ref apiHint, out errorMsg);
        }

        // Queries GitHub Releases API (for official SHA-256 digest & exact size),
        // with instant rate-limit/timeout failover to multi-mirror /releases/latest redirect parser.
        public bool FetchLatestMetadata(ToolModule m, bool useMirror, ref bool skipApiHint, out string errorMsg)
        {
            errorMsg = "";
            if (m == null) return false;
            if (_demoMode) return true;

            if (!skipApiHint)
            {
                string apiUrl = "https://api.github.com/repos/" + m.RepoOwner + "/" + m.RepoName + "/releases/latest";
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(apiUrl);
                    req.UserAgent = "ZeroBox/1.0.1 (https://github.com/rodvay/ZeroBox)";
                    req.Accept = "application/vnd.github+json";
                    req.Timeout = useMirror ? 5000 : 9000;
                    req.ReadWriteTimeout = 9000;
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
                    HttpWebResponse hr = wex.Response as HttpWebResponse;
                    int sc = hr != null ? (int)hr.StatusCode : 0;
                    // On 403/429 rate-limit or network timeout, hint batch siblings to skip api.github.com
                    if (sc == 403 || sc == 429 || wex.Status == WebExceptionStatus.Timeout || wex.Status == WebExceptionStatus.ConnectFailure)
                        skipApiHint = true;
                    errorMsg = wex.Message;
                }
                catch (Exception ex)
                {
                    errorMsg = ex.Message;
                }
            }

            if (TryFetchViaRedirectFallback(m, useMirror))
            {
                RefreshModuleStateAfterMeta(m);
                errorMsg = "";
                return true;
            }
            return false;
        }

        // Checks whether a newer ZeroBox launcher itself is available on GitHub
        public string CheckSelfLatestTag(bool useMirror)
        {
            if (_demoMode) return "";
            ToolModule selfMod = new ToolModule
            {
                RepoOwner = "rodvay",
                RepoName = "ZeroBox",
                ExeName = "ZeroBox.exe",
                IsZipArchive = false
            };
            string err;
            bool hint = false;
            if (FetchLatestMetadata(selfMod, useMirror, ref hint, out err))
                return selfMod.LatestVersion ?? "";
            return "";
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
                            {
                                string hex = dig.Substring(7).Trim().ToLowerInvariant();
                                if (hex.Length == 64) bestDigest = hex;
                            }
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

        internal static bool TryExtractTagFromRedirect(string locationOrUri, out string tag)
        {
            tag = "";
            if (string.IsNullOrEmpty(locationOrUri)) return false;
            int idx = locationOrUri.LastIndexOf("/tag/", StringComparison.OrdinalIgnoreCase);
            if (idx <= 0) return false;
            string raw = locationOrUri.Substring(idx + 5);
            int q = raw.IndexOfAny(new char[] { '?', '#', '/' });
            if (q > 0) raw = raw.Substring(0, q);
            raw = raw.Trim();
            if (string.IsNullOrEmpty(raw)) return false;
            tag = raw;
            return true;
        }

        private static bool TryFetchViaRedirectFallback(ToolModule m, bool useMirror)
        {
            string baseUrl = "https://github.com/" + m.RepoOwner + "/" + m.RepoName + "/releases/latest";
            List<string> candidates = new List<string>();
            if (useMirror)
            {
                for (int i = 0; i < MirrorPrefixes.Length; i++)
                    candidates.Add(WrapMirrorUrlWithPrefix(baseUrl, MirrorPrefixes[i]));
                candidates.Add(baseUrl);
            }
            else
            {
                candidates.Add(baseUrl);
            }

            for (int c = 0; c < candidates.Count; c++)
            {
                string tag;
                if (ProbeRedirectForTag(candidates[c], out tag))
                {
                    string assetName = m.IsZipArchive ? (m.RepoName + "-" + tag + "-Portable.zip") : m.ExeName;
                    m.LatestVersion = tag;
                    m.DownloadUrl = "https://github.com/" + m.RepoOwner + "/" + m.RepoName + "/releases/download/" + tag + "/" + assetName;
                    m.ExpectedSha256 = "";
                    return true;
                }
            }
            return false;
        }

        private static bool ProbeRedirectForTag(string probeUrl, out string tag)
        {
            tag = "";
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(probeUrl);
                req.UserAgent = "ZeroBox/1.0.1";
                req.AllowAutoRedirect = false;
                req.Timeout = 7000;
                req.ReadWriteTimeout = 7000;
                req.Proxy = WebRequest.DefaultWebProxy;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    string loc = resp.Headers["Location"];
                    if (TryExtractTagFromRedirect(loc, out tag)) return true;
                    if (resp.ResponseUri != null && TryExtractTagFromRedirect(resp.ResponseUri.AbsoluteUri, out tag)) return true;
                }
            }
            catch (WebException wex)
            {
                try
                {
                    HttpWebResponse hr = wex.Response as HttpWebResponse;
                    if (hr != null)
                    {
                        string loc = hr.Headers["Location"];
                        if (TryExtractTagFromRedirect(loc, out tag)) return true;
                    }
                }
                catch { }
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
            else if (m.State != ModuleState.Error)
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
                        // QueryFullProcessImageNameW works across UAC elevation boundaries!
                        string path = WinNative.TryGetProcessImagePath(procs[i].Id);
                        if (string.IsNullOrEmpty(path))
                        {
                            try { path = procs[i].MainModule.FileName; } catch { }
                        }
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
                m.LastInstallShaVerified = true;
                m.ErrorText = "";
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
                bool hint = false;
                if (!FetchLatestMetadata(m, useMirror, ref hint, out metaErr) || string.IsNullOrEmpty(m.DownloadUrl))
                {
                    errorMsg = string.IsNullOrEmpty(metaErr) ? "Unable to resolve latest release URL." : metaErr;
                    return false;
                }
            }

            string modDir = GetModuleDir(m);
            if (!Directory.Exists(modDir)) Directory.CreateDirectory(modDir);

            List<string> dlCandidates = new List<string>();
            if (useMirror)
            {
                for (int i = 0; i < MirrorPrefixes.Length; i++)
                    dlCandidates.Add(WrapMirrorUrlWithPrefix(m.DownloadUrl, MirrorPrefixes[i]));
                dlCandidates.Add(m.DownloadUrl); // Last-resort direct fallback
            }
            else
            {
                dlCandidates.Add(m.DownloadUrl);
            }

            string tmpFile = Path.Combine(modDir, ".download_" + Guid.NewGuid().ToString("N") + (m.IsZipArchive ? ".zip" : ".exe"));
            string stageDir = Path.Combine(modDir, ".stage_" + Guid.NewGuid().ToString("N"));
            try
            {
                bool downloadedOk = false;
                string lastErr = "";

                for (int c = 0; c < dlCandidates.Count; c++)
                {
                    if (_cancelRequested) { errorMsg = "CANCELED"; return false; }
                    try
                    {
                        if (File.Exists(tmpFile)) File.Delete(tmpFile);
                        DownloadFileStream(dlCandidates[c], tmpFile, m.RemoteBytes, onProgress);

                        // Mandatory PE ('MZ') / ZIP ('PK') & size validation prevents broken mirror HTML corruption!
                        string valErr;
                        if (!ValidateDownloadedBinary(tmpFile, m.IsZipArchive, out valErr))
                        {
                            lastErr = valErr;
                            continue;
                        }

                        // Strict cryptographic SHA-256 verification whenever GitHub API provided official digest
                        if (!string.IsNullOrEmpty(m.ExpectedSha256))
                        {
                            string actualHash = ComputeFileSha256(tmpFile);
                            if (!string.Equals(actualHash, m.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                            {
                                lastErr = "SHA-256 verification mismatch! Download aborted for safety.";
                                continue;
                            }
                        }

                        downloadedOk = true;
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        errorMsg = "CANCELED";
                        return false;
                    }
                    catch (Exception ex)
                    {
                        lastErr = ex.Message;
                    }
                }

                if (!downloadedOk)
                {
                    errorMsg = string.IsNullOrEmpty(lastErr) ? "Download failed." : lastErr;
                    return false;
                }

                if (onProgress != null) onProgress(95);

                if (m.IsZipArchive)
                {
                    // Non-destructive atomic staging: extract & verify ExeName BEFORE deleting existing installation!
                    Directory.CreateDirectory(stageDir);
                    ExtractZipSafely(tmpFile, stageDir, m.RepoName);
                    string stagedExe = FindExecutableInDir(stageDir, m.ExeName);
                    if (string.IsNullOrEmpty(stagedExe) || !File.Exists(stagedExe))
                    {
                        errorMsg = "Archive did not contain expected executable: " + m.ExeName;
                        return false;
                    }

                    // Now swap cleanly into modDir
                    CleanDirExcept(modDir, tmpFile, stageDir);
                    MoveDirectoryContents(stageDir, modDir);
                    try { Directory.Delete(stageDir, true); } catch { }
                    try { File.Delete(tmpFile); } catch { }
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
                m.LastInstallShaVerified = !string.IsNullOrEmpty(m.ExpectedSha256);
                m.ErrorText = "";
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
                try { if (Directory.Exists(stageDir)) Directory.Delete(stageDir, true); } catch { }
            }
        }

        private void DownloadFileStream(string dlUrl, string destFile, long fallbackTotal, Action<int> onProgress)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(dlUrl);
            req.UserAgent = "ZeroBox/1.0.1 (https://github.com/rodvay/ZeroBox)";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 25000;
            req.Proxy = WebRequest.DefaultWebProxy;

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream src = resp.GetResponseStream())
            using (FileStream dst = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                long total = resp.ContentLength > 0 ? resp.ContentLength : fallbackTotal;
                byte[] buf = new byte[16384];
                long readSum = 0;
                int lastPct = -1;
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    if (_cancelRequested) throw new OperationCanceledException();
                    dst.Write(buf, 0, n);
                    readSum += n;
                    if (total > 0 && onProgress != null)
                    {
                        int pct = Math.Max(1, Math.Min(92, (int)((readSum * 92L) / total)));
                        if (pct != lastPct) { lastPct = pct; onProgress(pct); }
                    }
                }
            }
        }

        public static void ExtractZipSafely(string zipPath, string targetDir)
        {
            ExtractZipSafely(zipPath, targetDir, "");
        }

        // Zip-Slip protected archive extractor (optionally flattens a single redundant top-level folder like CorePulse/)
        public static void ExtractZipSafely(string zipPath, string targetDir, string flattenTopFolder)
        {
            string baseFull = Path.GetFullPath(targetDir);
            if (!baseFull.EndsWith(Path.DirectorySeparatorChar.ToString()))
                baseFull += Path.DirectorySeparatorChar;

            using (ZipArchive za = ZipFile.OpenRead(zipPath))
            {
                // Detect if EVERY file entry starts with "<flattenTopFolder>/" or "<flattenTopFolder>\"
                bool canFlatten = !string.IsNullOrEmpty(flattenTopFolder);
                if (canFlatten)
                {
                    int fileCount = 0;
                    foreach (ZipArchiveEntry e in za.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;
                        fileCount++;
                        string norm = e.FullName.Replace('\\', '/');
                        if (!norm.StartsWith(flattenTopFolder + "/", StringComparison.OrdinalIgnoreCase))
                        {
                            canFlatten = false;
                            break;
                        }
                    }
                    if (fileCount == 0) canFlatten = false;
                }

                foreach (ZipArchiveEntry entry in za.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
                    string rel = entry.FullName;
                    if (canFlatten)
                    {
                        string norm = rel.Replace('\\', '/');
                        rel = norm.Substring(flattenTopFolder.Length + 1);
                    }
                    if (string.IsNullOrEmpty(rel)) continue;

                    string destPath = Path.GetFullPath(Path.Combine(targetDir, rel));
                    if (!destPath.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Blocked unsafe zip entry path: " + entry.FullName);

                    string parent = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                        Directory.CreateDirectory(parent);
                    entry.ExtractToFile(destPath, true);
                }
            }
        }

        private static void MoveDirectoryContents(string srcDir, string dstDir)
        {
            foreach (string f in Directory.GetFiles(srcDir))
            {
                string dest = Path.Combine(dstDir, Path.GetFileName(f));
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(f, dest);
            }
            foreach (string d in Directory.GetDirectories(srcDir))
            {
                string destSub = Path.Combine(dstDir, Path.GetFileName(d));
                if (!Directory.Exists(destSub)) Directory.CreateDirectory(destSub);
                MoveDirectoryContents(d, destSub);
            }
        }

        private static void CleanDirExcept(string dir, string keepFile, string keepSubDir)
        {
            string keepFileFull = string.IsNullOrEmpty(keepFile) ? "" : Path.GetFullPath(keepFile);
            string keepDirFull = string.IsNullOrEmpty(keepSubDir) ? "" : Path.GetFullPath(keepSubDir);
            foreach (string f in Directory.GetFiles(dir))
            {
                if (!string.IsNullOrEmpty(keepFileFull) && string.Equals(Path.GetFullPath(f), keepFileFull, StringComparison.OrdinalIgnoreCase))
                    continue;
                try { File.Delete(f); } catch { }
            }
            foreach (string d in Directory.GetDirectories(dir))
            {
                if (!string.IsNullOrEmpty(keepDirFull) && string.Equals(Path.GetFullPath(d), keepDirFull, StringComparison.OrdinalIgnoreCase))
                    continue;
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
                m.ErrorText = "";
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
                m.ErrorText = "";
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
                ScanSingleModule(m);
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
            catch (Win32Exception wex)
            {
                if (wex.NativeErrorCode == ErrorCancelledWin32)
                {
                    errorMsg = "UAC_CANCELED";
                    return false;
                }
                errorMsg = wex.Message;
                return false;
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
