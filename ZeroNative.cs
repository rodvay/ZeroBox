using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ZeroBox
{
    internal static class Loc
    {
        public static string DetectSystemLanguage()
        {
            try
            {
                string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (string.Equals(two, "tr", StringComparison.OrdinalIgnoreCase)) return "tr";
                if (string.Equals(two, "zh", StringComparison.OrdinalIgnoreCase)) return "zh";
            }
            catch { }
            return "en";
        }

        public static string T(string lang, string en, string tr, string zh)
        {
            if (string.Equals(lang, "tr", StringComparison.OrdinalIgnoreCase)) return tr;
            if (string.Equals(lang, "zh", StringComparison.OrdinalIgnoreCase)) return zh;
            return en;
        }

        public static string FontFamily(string lang)
        {
            return string.Equals(lang, "zh", StringComparison.OrdinalIgnoreCase) ? "Microsoft YaHei UI" : "Segoe UI";
        }

        public static CultureInfo Culture(string lang)
        {
            try
            {
                if (string.Equals(lang, "tr", StringComparison.OrdinalIgnoreCase)) return CultureInfo.GetCultureInfo("tr-TR");
                if (string.Equals(lang, "zh", StringComparison.OrdinalIgnoreCase)) return CultureInfo.GetCultureInfo("zh-CN");
                return CultureInfo.GetCultureInfo("en-US");
            }
            catch { return CultureInfo.InvariantCulture; }
        }

        public static string F(string fmt, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, fmt, args);
        }
    }

    internal sealed class ZeroBoxSettings
    {
        public string Language = Loc.DetectSystemLanguage();
        public bool MinimizeToTray = true;
        public bool UseMirror = false;
        public string CustomModulesDir = "";

        public static string GetConfigDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZeroBox");
        }

        public static string GetSettingsPath()
        {
            return Path.Combine(GetConfigDir(), "settings.ini");
        }

        public static bool IsSafeRootedDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string trimmed = path.Trim();
                if (trimmed.Length < 3 || trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
                if (!Path.IsPathRooted(trimmed)) return false;
                string full = Path.GetFullPath(trimmed);
                return !string.IsNullOrEmpty(full) && full.Length >= 3;
            }
            catch { return false; }
        }

        public string ResolveModulesRoot()
        {
            if (IsSafeRootedDir(CustomModulesDir)) return Path.GetFullPath(CustomModulesDir.Trim());
            // Portable mode: if a "ZeroBox_Modules" folder already exists next to ZeroBox.exe, prefer it
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    string portable = Path.Combine(exeDir, "ZeroBox_Modules");
                    if (Directory.Exists(portable)) return portable;
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"ZeroBox\modules");
        }

        public static ZeroBoxSettings Load()
        {
            ZeroBoxSettings s = new ZeroBoxSettings();
            try
            {
                string p = GetSettingsPath();
                if (!File.Exists(p)) return s;
                foreach (string raw in File.ReadAllLines(p, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim();
                    string v = raw.Substring(eq + 1).Trim();
                    if (k == "Language" && (v == "en" || v == "tr" || v == "zh")) s.Language = v;
                    else if (k == "MinimizeToTray") s.MinimizeToTray = (v == "1");
                    else if (k == "UseMirror") s.UseMirror = (v == "1");
                    else if (k == "CustomModulesDir" && IsSafeRootedDir(v)) s.CustomModulesDir = v;
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                string p = GetSettingsPath();
                string dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Language=" + (Language ?? "en"));
                sb.AppendLine("MinimizeToTray=" + (MinimizeToTray ? "1" : "0"));
                sb.AppendLine("UseMirror=" + (UseMirror ? "1" : "0"));
                if (IsSafeRootedDir(CustomModulesDir)) sb.AppendLine("CustomModulesDir=" + CustomModulesDir.Trim());
                File.WriteAllText(p, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    internal static class WinNative
    {
        public const int WM_DPICHANGED = 0x02E0;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_CAPTION_COLOR = 35;
        public const int DWMWA_TEXT_COLOR = 36;
        public const uint MSGFLT_ALLOW = 1;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        public static readonly uint WM_SHOW_EXISTING = RegisterWindowMessage("ZeroBox_ShowExistingWindow_v1");
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        private const string StartupRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "ZeroBox";

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr pChangeFilterStruct);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        // Works across UAC elevation boundaries (reads elevated process image path from non-elevated ZeroBox!)
        public static string TryGetProcessImagePath(int pid)
        {
            IntPtr hProc = IntPtr.Zero;
            try
            {
                hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProc == IntPtr.Zero) return "";
                int cap = 1024;
                StringBuilder sb = new StringBuilder(cap);
                if (QueryFullProcessImageNameW(hProc, 0, sb, ref cap) && cap > 0)
                    return sb.ToString(0, cap);
            }
            catch { }
            finally
            {
                if (hProc != IntPtr.Zero) { try { CloseHandle(hProc); } catch { } }
            }
            return "";
        }

        public static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        public static void ApplyDarkTitleBar(IntPtr hwnd, Color captionColor, Color textColor)
        {
            try
            {
                int en = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref en, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref en, sizeof(int));
                int cap = captionColor.R | (captionColor.G << 8) | (captionColor.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref cap, sizeof(int));
                int txt = textColor.R | (textColor.G << 8) | (textColor.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref txt, sizeof(int));
            }
            catch { }
        }

        public static void AllowWakeMessage(IntPtr hwnd)
        {
            try
            {
                if (hwnd != IntPtr.Zero && WM_SHOW_EXISTING != 0)
                    ChangeWindowMessageFilterEx(hwnd, WM_SHOW_EXISTING, MSGFLT_ALLOW, IntPtr.Zero);
            }
            catch { }
        }

        private static string ExpectedStartupCommand()
        {
            return "\"" + Application.ExecutablePath + "\" --minimized";
        }

        public static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(StartupRunKey, false))
                {
                    if (k == null) return false;
                    string val = k.GetValue(StartupValueName) as string;
                    if (string.IsNullOrEmpty(val)) return false;
                    // Verify entry still points to current ZeroBox.exe path (handles moved EXE cleanly)
                    return val.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        public static bool SetStartupEnabled(bool enable)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(StartupRunKey))
                {
                    if (k == null) return false;
                    if (enable) k.SetValue(StartupValueName, ExpectedStartupCommand());
                    else k.DeleteValue(StartupValueName, false);
                    return true;
                }
            }
            catch { return false; }
        }

        public static void OpenUrlSafe(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        public static void OpenFolderSafe(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                System.Diagnostics.Process.Start(explorer, "\"" + dir + "\"");
            }
            catch { }
        }
    }
}
