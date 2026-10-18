# Steam 原生浏览器

**从 Steam 自己的 CEF 进程内部，弹出一个完整的 Chromium 窗口。**

**简体中文** · [English](README.en.md)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![WinUI](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK%201.7-0067C0)

Steam 内置浏览器是一个没有地址栏、没有多标签的简化 CEF 视图。**Steam 原生浏览器**会自动定位 Steam，
开启 CEF 调试端口（`127.0.0.1:8080`），然后让 Steam 自己的浏览器进程打开一个**完整的 Chromium 窗口**——
标签栏、地址栏、书签、扩展按钮一应俱全。

界面使用 **WinUI 3 / Windows App SDK 1.7** + **.NET 8** 构建，支持 Mica、Fluent 控件、明暗主题，
并带有设置页（强调色）与关于页。

![主页](docs/winui-home.png)

| 关于 | 设置 |
|---|---|
| ![关于](docs/winui-about.png) | ![设置](docs/winui-settings.png) |

## 亮点

- **自动定位 Steam**：注册表（`HKCU\Software\Valve\Steam`、`HKLM\...\Valve\Steam`）、运行中的
  `steam.exe`、常见安装目录，最后兜底扫描固定磁盘。
- **自动开启 CEF 调试**：探测 `127.0.0.1:8080` 并校验 `Valve Steam Client` UA；没开就使用
  `-cef-enable-debugging -dev` 启动 Steam（必要时先请求退出）。
- **两种调出方式**：无需插件的 DevTools 方式，或可选的扩展方式，失败自动回退。
- **权限自适应**：进程无法写 Steam 注册表时（低权限/沙箱，或注册表键被高权限改写过），
  自动请求 UAC **只提权 Steam**。
- **WinUI 3 界面**：Mica 背景、NavigationView、SelectorBar、InfoBar、圆角卡片、Fluent 图标。
- **可配置强调色**：默认跟随 Windows，也可固定为 8 种内置颜色之一。

## 工作原理

```
┌──────────────────────────────────────┐
│ steam.exe                            │
│  └─ steamwebhelper.exe  (CEF 126)    │
│       ├─ 127.0.0.1:8080              │  ← /json/version, /json/list
│       └─ 我们打开的 Chromium 窗口     │
└──────────────────────────────────────┘
                 ▲
                 │ WebSocket / CDP
        SteamNativeBrowser.exe
```

| 方式 | 机制 | 依赖 |
|---|---|---|
| **内置方式**（默认） | 浏览器级 CDP `Target.createTarget` → 由 Steam 新建 Chromium 窗口 | 无 |
| **插件方式** | 连接扩展的 MV3 Service Worker，调用 `chrome.windows.create` | 至少一个扩展后台在运行 |

内置方式是最关键的发现：**完全不需要任何插件**。CEF 的 `/json/version` 暴露了浏览器级 WebSocket 地址，
`Target.createTarget` 就能让 Steam 进程创建一个完整窗口。

## 下载

从 [Releases](https://github.com/tzhx12/steam-native-browser/releases/latest) 下载最新 **Portable** 压缩包，
解压后直接运行 `SteamNativeBrowser.exe`。该包**自包含** Windows App SDK 运行时与 .NET 运行时，无需安装。

系统要求：

- Windows 10 1809+ 或 Windows 11（x64）
- Steam 已安装并至少登录过一次

## 使用

1. 启动 `SteamNativeBrowser.exe`。
2. 确认自动检测到的 Steam 路径（不对就点「浏览…」）。
3. 选择调出方式（推荐内置方式），按需填写要打开的地址。
4. 点「一键调出浏览器窗口」。

如果 Steam 没开 CEF，程序会自动处理；需要时会在启动 Steam 时请求 UAC。

已验证：Windows 11，Steam CEF `Chrome/126.0.6478.183`，内置方式与插件方式均通过。

## 设置

- **强调色**：默认跟随系统，也可选择 8 种内置颜色；点「应用并重启」后全局生效。
- 设置保存在 `%LOCALAPPDATA%\SteamNativeBrowser\settings.json`。

## 从源码构建

需要 .NET 8 SDK 与 Windows 10/11 SDK（NuGet 包会自动还原）。

```powershell
git clone https://github.com/tzhx12/steam-native-browser.git
cd steam-native-browser
dotnet build src\SteamNativeBrowser.WinUI\SteamNativeBrowser.WinUI.csproj -c Release
```

便携版发布：

```powershell
dotnet publish src\SteamNativeBrowser.WinUI\SteamNativeBrowser.WinUI.csproj `
  -c Release -r win-x64 --self-contained true -o dist-winui
```

WinUI 3 需要 Windows App SDK 1.7 相关包；未安装 Visual Studio 的 MSIX 工具时还需要
`Microsoft.Windows.SDK.BuildTools.MSIX`。本仓库提供 `tools/fetch_nuget.py`（仅标准库、支持 `HTTPS_PROXY`）
用于离线预取 NuGet 包，参见 `NuGet.config` 与 `build-winui.ps1`。

## 目录结构

```
src/
  SteamNativeBrowser.WinUI/   WinUI 3 主程序
    Pages/                    主页、关于、设置
    Services/                 设置、强调色
  SteamNativeBrowser/         旧版 WPF
    Services/                 与 UI 无关的共享服务（Steam、CEF、CDP）
dist/                         旧版 WPF 单文件
docs/                         截图
tools/                        图标管线、离线 NuGet 下载器
```

## 权限与安全提示

- `-cef-enable-debugging` 会在 `127.0.0.1:8080` 开放**无鉴权**的调试端口，本机任意程序都能连接；
  不需要时应以正常方式启动 Steam。
- 程序会修改 Steam 启动参数并可能重启 Steam，请确认没有未保存的操作。
- 可执行文件未签名，Windows 可能提示“无法验证发布者”，选择「运行」即可。
- 调出的窗口属于 Steam 的 CEF，共享 Steam 的网页缓存与扩展，会随 Steam 退出而关闭；
  它**不是**你系统里安装的 Chrome 配置。

## 许可证

[MIT](LICENSE) © 2026 SteamNativeLauncher contributors

界面基于 Microsoft WinUI 3 / Windows App SDK（MIT）；窗口创建使用 Chrome DevTools Protocol；应用图标为项目定制生成。
