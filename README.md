# Steam Native Browser

**Open a real Chromium window from inside Steam's own CEF process.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![WinUI](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK%201.7-0067C0)

Steam's built-in browser is a stripped-down CEF view without tabs or an address bar. Steam Native Browser
locates your Steam installation, enables the CEF debugging port (`127.0.0.1:8080`), and asks Steam's own
browser process to open a **full Chromium window** — tab strip, omnibox, bookmarks, extensions and all.

The UI is built with **WinUI 3 / Windows App SDK 1.7** on **.NET 8**, with Mica, Fluent controls,
light/dark themes, a settings page (accent color) and an about page.

> The application UI is in Simplified Chinese; this README documents it in English.

![Home](docs/winui-home.png)

| About | Settings |
|---|---|
| ![About](docs/winui-about.png) | ![Settings](docs/winui-settings.png) |

## Highlights

- **Automatic Steam discovery** — registry (`HKCU\Software\Valve\Steam`, `HKLM\...\Valve\Steam`), running
  `steam.exe`, common install paths, and a fixed-drive fallback scan.
- **Automatic CEF mode** — probes `127.0.0.1:8080`, verifies the `Valve Steam Client` user agent, and if
  needed restarts Steam with `-cef-enable-debugging -dev`.
- **Two launch modes** — a plugin-free DevTools mode and an optional extension mode, with automatic fallback.
- **Permission-aware** — if the current process cannot write Steam's registry key (low-integrity sandbox or a
  key rewritten by an elevated process), the app requests UAC **for Steam only**.
- **WinUI 3 shell** — Mica backdrop, NavigationView, SelectorBar, InfoBar, rounded cards, Fluent icons.
- **Configurable accent color** — follow Windows by default, or pin one of 8 built-in colors.

## How it works

```
┌──────────────────────────────────────┐
│ steam.exe                            │
│  └─ steamwebhelper.exe  (CEF 126)    │
│       ├─ 127.0.0.1:8080              │  ← /json/version, /json/list
│       └─ the Chromium window we open │
└──────────────────────────────────────┘
                 ▲
                 │ WebSocket / CDP
        SteamNativeBrowser.exe
```

| Mode | Mechanism | Dependency |
|---|---|---|
| **Built-in** (default) | Browser-level CDP `Target.createTarget` → Steam creates a new Chromium window | None |
| **Extension** | Connect to an extension's MV3 service worker and call `chrome.windows.create` | At least one extension background running |

The built-in mode is the important finding: **no extension is required**. CEF exposes a browser-level
WebSocket endpoint in `/json/version`, and `Target.createTarget` makes the Steam process create a full window.

## Download

Grab the latest **Portable** zip from [Releases](../../releases), extract it anywhere and run
`SteamNativeBrowser.exe`. The package is self-contained: it bundles the Windows App SDK runtime and the
.NET runtime, so no installation is needed.

Requirements:

- Windows 10 1809+ or Windows 11 (x64)
- Steam installed and signed in at least once

## Usage

1. Launch `SteamNativeBrowser.exe`.
2. Confirm the detected Steam path (use **Browse…** if it is wrong).
3. Pick a launch mode (built-in is recommended) and optionally a URL.
4. Click **Open Browser Window**.

If Steam is not running in CEF mode, the app handles it — including a UAC prompt when Steam needs elevated
rights to write its registry key.

Validated: Windows 11, Steam CEF `Chrome/126.0.6478.183`, built-in and extension modes both tested.

## Settings

- **Accent color** — follow the system accent (default) or pick a custom color. Applied on restart.
- Stored at `%LOCALAPPDATA%\SteamNativeBrowser\settings.json`.

## Build from source

Requirements: .NET 8 SDK, Windows 10/11 SDK (the NuGet packages are restored automatically).

```powershell
git clone https://github.com/tzhx12/steam-native-browser.git
cd steam-native-browser
dotnet build src\SteamNativeBrowser.WinUI\SteamNativeBrowser.WinUI.csproj -c Release
```

Portable publish:

```powershell
dotnet publish src\SteamNativeBrowser.WinUI\SteamNativeBrowser.WinUI.csproj `
  -c Release -r win-x64 --self-contained true -o dist-winui
```

WinUI 3 needs the Windows App SDK 1.7 packages plus `Microsoft.Windows.SDK.BuildTools.MSIX` when Visual
Studio's MSIX tooling is not installed. The repository includes `tools/fetch_nuget.py` (stdlib-only, respects
`HTTPS_PROXY`) to pre-fetch packages into a local feed for offline builds; see `NuGet.config` and
`build-winui.ps1`.

## Repository layout

```
src/
  SteamNativeBrowser.WinUI/   WinUI 3 app (main)
    Pages/                    Home, About, Settings
    Services/                 settings, accent color
  SteamNativeBrowser/         legacy WPF app
    Services/                 shared UI-agnostic services (Steam, CEF, CDP)
dist/                         legacy single-file WPF build
docs/                         screenshots
tools/                        icon pipeline, offline NuGet fetcher
```

## Permissions & security notes

- `-cef-enable-debugging` opens an **unauthenticated** DevTools port on `127.0.0.1:8080`. Any local process
  can connect while it is open. Start Steam normally when you do not need this tool.
- The app modifies Steam's launch arguments and may restart Steam. Make sure nothing important is pending.
- Windows may show a publisher warning for the unsigned executable; choose **Run**.
- The window belongs to Steam's CEF. It shares Steam's web cache and extensions and closes with Steam. It is
  **not** your installed Chrome profile.

## License

[MIT](LICENSE) © 2026 SteamNativeLauncher contributors

UI built with Microsoft WinUI 3 / Windows App SDK (MIT). Window creation uses the Chrome DevTools Protocol.
App icon generated for this project.
