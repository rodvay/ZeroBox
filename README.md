<p align="center">
  <img src="assets/banner.png" alt="ZeroBox Banner" width="100%"/>
</p>

<p align="center">
  <strong>English</strong> | <a href="README.zh-CN.md">简体中文</a>
</p>

<p align="center">
  <a href="https://github.com/rodvay/ZeroBox/releases/latest"><img src="assets/badges/release-en.svg" alt="Release"></a>
  <img src="assets/badges/size-en.svg" alt="Size">
  <img src="assets/badges/platform-en.svg" alt="Platform">
  <img src="assets/badges/deps-en.svg" alt="Zero Dependencies">
  <a href="LICENSE"><img src="assets/badges/license-en.svg" alt="MIT License"></a>
  <a href="https://linux.do"><img src="assets/badges/linuxdo-en.svg" alt="LINUX DO Community"></a>
</p>

---

**ZeroBox** is an ultra-lightweight (**~81 KB**), single-file native Windows 10/11 **modular toolbox & on-demand plugin launcher** for the [**@rodvay**](https://github.com/rodvay) Zero-Bloat Windows Suite.

Instead of downloading and manually updating 6 separate utilities, drop a single **81 KB** `ZeroBox.exe` anywhere on your PC and install, update, launch, or cleanly remove only the native micro-apps you actually want — zero bundled bloat, zero background telemetry, **PE/ZIP header + strict SHA-256 verification**, and **3-tier mirror failover**.

## Screenshot

<p align="center">
  <img src="assets/screenshot-en.png" alt="ZeroBox Modular Toolbox Interface" width="92%"/>
</p>

## Available Official Modules (On-Demand)

| Module | Category | Size | What It Does | Repository |
| :--- | :--- | :---: | :--- | :--- |
| **PCUptime** | System | `~50 KB` | Real uptime, Fast Startup detection, BSOD crash & Sleep/Wake event timeline | [rodvay/PCUptime](https://github.com/rodvay/PCUptime) |
| **SafeSweep** | Maintenance | `~70 KB` | Transparent 2-step system junk, browser cache & Windows Update cleaner | [rodvay/SafeSweep](https://github.com/rodvay/SafeSweep) |
| **WinForge** | System | `~150 KB` | Reversible Windows 10/11 privacy/performance optimizer, AppX debloater & Winget manager | [rodvay/WinForge](https://github.com/rodvay/WinForge) |
| **CorePulse** | Hardware | `~230 KB` | Live CPU/GPU/RAM sensors, custom PWM fan curves & 32-bit Gaming HUD | [rodvay/CorePulse](https://github.com/rodvay/CorePulse) |
| **PortWatch** | Network | `~105 KB` | Live TCP/UDP port killer (30+ dev/proxy tags), `0px` Mini Speed HUD & reversible firewall | [rodvay/PortWatch](https://github.com/rodvay/PortWatch) |
| **LumiBar** | Display | `~86 KB` | Hardware DDC/CI multi-monitor brightness & contrast, Warm Night Tint & Smart Idle sleep | [rodvay/LumiBar](https://github.com/rodvay/LumiBar) |

## Key Features

- **100% On-Demand Modular Architecture:** ZeroBox itself is only **~81 KB** and ships with zero pre-bundled payloads. Each micro-app is downloaded directly from its official GitHub Release only when you click **Install**, and lives cleanly inside `%LOCALAPPDATA%\ZeroBox\modules\<ModuleId>\`.
- **Strict SHA-256 + PE/ZIP Integrity + Non-Destructive Atomic Staging:** Validates PE (`MZ`) / ZIP (`PK`) magic bytes & minimum size (blocking broken proxy HTML error pages), verifies every payload against GitHub's official `SHA-256` release digest, uses non-destructive atomic staging + Zip-Slip protection on archive updates, and detects even elevated running processes via `QueryFullProcessImageNameW`.
- **Parallel Checks + 3-Tier Mirror Failover (`ghfast.top` / `gh-proxy`):** Queries all 6 modules + ZeroBox self-updates concurrently in under 1 second, automatically falls back to `/releases/latest` redirects on API rate limits, and provides a 1-click **Source: GitHub / Mirror** toggle with 3-node automatic failover.
- **1-Click `Update All`, Double-Click Launch, Launch as Admin & Clean Uninstall:** Double-click any installed card to launch immediately, right-click for **Launch as Administrator** or **Open Module Folder**, batch-update with **Update All**, or **Remove** cleanly with zero registry residue.
- **Unified Single System Tray & Keyboard Shortcuts:** Minimize ZeroBox to one lightweight tray icon listing all installed modules (`F5` check updates, `Ctrl+1..6` instant launch, flash-free `--minimized` Windows Startup, screen-clamped Per-Monitor V2 DPI, Single-Instance Mutex, `EN / TR / 简体中文`).

## Download & Run

1. Download **`ZeroBox.exe`** from [GitHub Releases](https://github.com/rodvay/ZeroBox/releases/latest).
2. Double-click to run — **no installer, no admin privileges required** to manage modules.
3. Click **Install** on any module card you want, or right-click an installed card for quick actions.

### Build from Source (Zero External SDK Required)

```bat
build.bat
```

## Türkçe Özet

**ZeroBox**, Windows 10/11 için yalnızca **~81 KB** boyutunda, tek dosya yerel **Modüler Araç Kutusu ve İsteğe Bağlı Eklenti Başlatıcısıdır**:
- [@rodvay](https://github.com/rodvay) serisindeki 6 bağımsız mikro uygulamayı (**PCUptime, SafeSweep, WinForge, CorePulse, PortWatch, LumiBar**) tek bir modern koyu arayüzde toplar; yalnızca ihtiyacınız olan modülleri tek tıkla kurar, günceller veya temizce kaldırır.
- İndirilen her modülü GitHub'ın resmi **SHA-256** özetiyle doğrular, ZIP arşivlerini Zip-Slip korumasıyla açar ve tüm kurulu araçları tek bir **Sistem Tepsisi** menüsünden anında başlatmanızı sağlar.

## Community & Links

- Inspired by community feedback on **[LINUX DO](https://linux.do)** — Thank you to `莫乃 monater` and the entire LINUX DO open-source community for the modular toolbox idea and support!

## License

[MIT License](LICENSE) © 2026 [rodvay](https://github.com/rodvay)
