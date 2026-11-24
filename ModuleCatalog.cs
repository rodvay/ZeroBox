using System;
using System.Collections.Generic;
using System.Drawing;

namespace ZeroBox
{
    internal enum ModuleState
    {
        NotInstalled,
        Installed,
        UpdateAvailable,
        Downloading,
        Error
    }

    internal sealed class ToolModule
    {
        public string Id;
        public string Name;
        public string RepoOwner = "rodvay";
        public string RepoName;
        public string ExeName;
        public bool IsZipArchive;
        public Color Accent;
        public string IconKind; // "uptime", "sweep", "forge", "pulse", "port", "lumi"

        public string CatEn, CatTr, CatZh;
        public string DescEn, DescTr, DescZh;
        public string ApproxSize;
        public string FallbackTag;

        // Live runtime state (mutated safely via ModuleManager)
        public volatile ModuleState State = ModuleState.NotInstalled;
        public string InstalledVersion = "";
        public string InstalledExePath = "";
        public long InstalledBytes;

        public string LatestVersion = "";
        public string DownloadUrl = "";
        public string ExpectedSha256 = "";
        public long RemoteBytes;

        public volatile int ProgressPct;
        public bool LastInstallShaVerified;
        public string ErrorText = "";

        public string RepoUrl { get { return "https://github.com/" + RepoOwner + "/" + RepoName; } }
        public string ReleasesUrl { get { return RepoUrl + "/releases/latest"; } }

        public string Category(string lang) { return Loc.T(lang, CatEn, CatTr, CatZh); }
        public string Description(string lang) { return Loc.T(lang, DescEn, DescTr, DescZh); }

        public static List<ToolModule> BuildDefaultCatalog()
        {
            List<ToolModule> list = new List<ToolModule>();

            list.Add(new ToolModule
            {
                Id = "pcuptime", Name = "PCUptime", RepoName = "PCUptime", ExeName = "PCUptime.exe",
                IsZipArchive = false, Accent = Color.FromArgb(56, 189, 248), IconKind = "uptime",
                CatEn = "Diagnostics", CatTr = "Tanılama", CatZh = "系统诊断",
                DescEn = "Real uptime, Fast Startup check, BSOD crash & Sleep/Wake event timeline.",
                DescTr = "Gerçek çalışma süresi, Hızlı Başlatma, BSOD çökme & uyku geçmişi.",
                DescZh = "精准开机时长、快速启动检测、蓝屏 BSOD 与睡眠唤醒时间线。",
                ApproxSize = "~86 KB", FallbackTag = "v1.3.3"
            });

            list.Add(new ToolModule
            {
                Id = "safesweep", Name = "SafeSweep", RepoName = "SafeSweep", ExeName = "SafeSweep.exe",
                IsZipArchive = false, Accent = Color.FromArgb(52, 211, 153), IconKind = "sweep",
                CatEn = "Maintenance", CatTr = "Temizlik", CatZh = "系统清理",
                DescEn = "Transparent 2-step system junk, browser cache & Windows Update cleaner.",
                DescTr = "Şeffaf 2 adımlı sistem çöpü, tarayıcı önbelleği ve güncelleme temizleyici.",
                DescZh = "透明两步式系统垃圾、浏览器缓存与 Windows 更新残留安全清理。",
                ApproxSize = "~97 KB", FallbackTag = "v1.1.3"
            });

            list.Add(new ToolModule
            {
                Id = "winforge", Name = "WinForge", RepoName = "WinForge", ExeName = "WinForge.exe",
                IsZipArchive = false, Accent = Color.FromArgb(167, 139, 250), IconKind = "forge",
                CatEn = "System Tweaks", CatTr = "Optimizasyon", CatZh = "系统优化",
                DescEn = "100% reversible Windows tweaks, 60 WinGet apps, Appx debloater & 1-click DNS.",
                DescTr = "%100 geri alınabilir Windows ayarları, 60 WinGet uygulaması, Debloat & DNS.",
                DescZh = "100% 可还原系统优化、60 款 WinGet 装机、预装精简与一键 DNS。",
                ApproxSize = "~277 KB", FallbackTag = "v2.0.5"
            });

            list.Add(new ToolModule
            {
                Id = "corepulse", Name = "CorePulse", RepoName = "CorePulse", ExeName = "CorePulse.exe",
                IsZipArchive = true, Accent = Color.FromArgb(248, 113, 113), IconKind = "pulse",
                CatEn = "Hardware & HUD", CatTr = "Donanım & HUD", CatZh = "硬件与帧率",
                DescEn = "CPU/GPU sensors, custom PWM fan curves, ETW FPS & 32-bit Gaming HUD.",
                DescTr = "CPU/GPU sensörleri, PWM fan eğrisi, ETW FPS ve 32-bit Oyuncu HUD.",
                DescZh = "全硬件温控、自定义 PWM 风扇曲线、零注入 FPS 与 32-bit 置顶 HUD。",
                ApproxSize = "~1.4 MB ZIP", FallbackTag = "v1.2.0"
            });

            list.Add(new ToolModule
            {
                Id = "portwatch", Name = "PortWatch", RepoName = "PortWatch", ExeName = "PortWatch.exe",
                IsZipArchive = false, Accent = Color.FromArgb(45, 212, 191), IconKind = "port",
                CatEn = "Network", CatTr = "Ağ & Duvar", CatZh = "网络与防火墙",
                DescEn = "Live TCP/UDP port killer (30+ dev tags), 0px speed HUD & reversible firewall.",
                DescTr = "Canlı TCP/UDP port kapatma, 0px hız HUD ve tek tık geri alınabilir duvar.",
                DescZh = "实时 TCP/UDP 端口查杀、0px 迷你网速浮窗与一键可逆防火墙拦截。",
                ApproxSize = "~105 KB", FallbackTag = "v1.0.3"
            });

            list.Add(new ToolModule
            {
                Id = "lumibar", Name = "LumiBar", RepoName = "LumiBar", ExeName = "LumiBar.exe",
                IsZipArchive = false, Accent = Color.FromArgb(251, 191, 36), IconKind = "lumi",
                CatEn = "Display & Power", CatTr = "Ekran & Güç", CatZh = "亮度与电源",
                DescEn = "Hardware DDC/CI monitor brightness, Warm Night Tint & Smart Idle sleep.",
                DescTr = "Donanımsal DDC/CI parlaklık, Sıcak Gece Tonu, Uyanık Tut & Akıllı Uyku.",
                DescZh = "原生 DDC/CI 多屏硬件调光、可逆护眼暖色温、防休眠与空闲自动睡眠。",
                ApproxSize = "~86 KB", FallbackTag = "v1.0.3"
            });

            return list;
        }
    }
}
