<p align="center">
  <img src="assets/banner-zh.png" alt="ZeroBox 横幅" width="100%"/>
</p>

<p align="center">
  <a href="README.md">English</a> | <strong>简体中文</strong>
</p>

<p align="center">
  <a href="https://github.com/rodvay/ZeroBox/releases/latest"><img src="https://img.shields.io/github/v/release/rodvay/ZeroBox?style=flat-square&color=38bdf8" alt="最新版本"></a>
  <img src="https://img.shields.io/badge/体积-~81%20KB-34d399?style=flat-square" alt="体积">
  <img src="https://img.shields.io/badge/系统-Windows%2010%20%2F%2011-0f172a?style=flat-square" alt="平台">
  <img src="https://img.shields.io/badge/依赖-零依赖单文件-a78bfa?style=flat-square" alt="零依赖">
  <a href="LICENSE"><img src="https://img.shields.io/badge/协议-MIT-fbbf24?style=flat-square" alt="MIT 协议"></a>
  <a href="https://linux.do"><img src="https://img.shields.io/badge/开源社区-LINUX%20DO-1C1C1E?style=flat-square&logo=linux&logoColor=38BDF8" alt="LINUX DO 社区"></a>
</p>

---

**ZeroBox** 是一款仅 **~81 KB** 的原生单文件 Windows 10/11 **模块化工具箱与按需插件启动器**，专为 [**@rodvay**](https://github.com/rodvay) 零臃肿开源工具全家桶打造！

无需再一个个手动下载、分散存放和逐个检查更新：只需一个 **81 KB** 的 `ZeroBox.exe`，按需一键安装、更新、启动或彻底卸载你真正需要的原生小工具 —— **绝不预装捆绑任何多余组件，零后台遥测，每次下载严格执行 PE/ZIP 文件头 + SHA-256 校验**。

## 界面预览

<p align="center">
  <img src="assets/screenshot-zh.png" alt="ZeroBox 模块化工具箱界面" width="92%"/>
</p>

## 内置官方模块目录（按需下载）

| 模块 | 分类 | 体积 | 核心功能 | 开源仓库 |
| :--- | :--- | :---: | :--- | :--- |
| **PCUptime** | 系统诊断 | `~50 KB` | 真实运行时间、快速启动识别、蓝屏崩溃与休眠唤醒事件线 | [rodvay/PCUptime](https://github.com/rodvay/PCUptime) |
| **SafeSweep** | 清理维护 | `~70 KB` | 透明两步预览式系统垃圾、浏览器缓存与 Windows 更新清理 | [rodvay/SafeSweep](https://github.com/rodvay/SafeSweep) |
| **WinForge** | 系统优化 | `~150 KB` | 可逆 Windows 10/11 隐私/性能优化、AppX 精简与 Winget 管理 | [rodvay/WinForge](https://github.com/rodvay/WinForge) |
| **CorePulse** | 硬件监控 | `~230 KB` | CPU/GPU/内存实时传感器、PWM 风扇曲线与 32-bit 游戏顶栏 HUD | [rodvay/CorePulse](https://github.com/rodvay/CorePulse) |
| **PortWatch** | 网络工具 | `~105 KB` | 实时 TCP/UDP 端口查杀（30+ 开发/代理标签）、顶栏网速与可逆防火墙 | [rodvay/PortWatch](https://github.com/rodvay/PortWatch) |
| **LumiBar** | 显示电源 | `~86 KB` | DDC/CI 多显示器硬件亮度/对比度、护眼暖色温、防休眠与智能空闲关机 | [rodvay/LumiBar](https://github.com/rodvay/LumiBar) |

## 核心特性

- **真正按需加载的模块化架构：** ZeroBox 本体仅 **~81 KB**，不捆绑任何工具本体；点击「安装」才直接从官方 GitHub Releases 拉取对应模块，并整洁存放在 `%LOCALAPPDATA%\ZeroBox\modules\<模块名>\`。
- **严格 SHA-256 + PE/ZIP 文件头校验 + 原子解压：** 每次下载自动校验文件头与最小体积（杜绝代理节点返回 HTML 错误页污染程序），并与 GitHub 官方 `SHA-256` 摘要严格比对；ZIP 模块（如 CorePulse）采用原子暂存区 + Zip-Slip 路径穿越防护（解压失败绝不破坏已有版本），并通过 `QueryFullProcessImageNameW` 跨提权精准检测进程占用。
- **并发检测 + 三级镜像容灾加速 (`ghfast.top` / `gh-proxy`)：** 1 秒内并发查询全部 6 个模块与 ZeroBox 本体更新；遇到 API 限流自动无缝切换至 `/releases/latest` 重定向解析，顶栏一键 **GitHub / 加速镜像** 切换并支持三级节点自动容灾。
- **一键「全部更新」、双击秒开、管理员启动与无痕卸载：** 双击已装卡片即可直接启动，右键可「以管理员身份启动」或直达程序目录，支持一键批量更新或彻底无痕卸载。
- **统一系统托盘 + 快捷键 + 高分屏防溢出：** 最小化后只占 **1 个轻量系统托盘图标**，右键菜单列出所有已装模块一键秒开；内置 `F5` 刷新、`Ctrl+1..6` 快捷启动、无闪烁托盘开机自启、Per-Monitor V2 高分屏适配、单实例互斥与 **简体中文 / English / Türkçe** 自动语言切换。

## 下载与使用

1. 前往 [GitHub Releases](https://github.com/rodvay/ZeroBox/releases/latest) 下载 **`ZeroBox.exe`**。
2. 绿色免安装、无需管理员权限，双击即可运行。
3. 点击所需模块卡片的 **「安装」** 按钮按需拉取，安装后即可直接启动或从托盘秒开。

### 本地零依赖编译

```bat
build.bat
```

## 社区与友情链接

- 本项目灵感源自 **[LINUX DO](https://linux.do)** 社区的开源讨论与建议 —— 特别感谢 `莫乃 monater` 以及各位佬友提出“模块化插件工具箱”的绝妙点子与一直以来的支持！

## 开源协议

[MIT License](LICENSE) © 2026 [rodvay](https://github.com/rodvay)
