# CrossGOverlay

> English documentation

[🇻🇳 Tiếng Việt](docs/README.vi.md)

> A lightweight Windows desktop crosshair overlay with transparent window rendering and no game-process injection.

[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D6?logo=windows&logoColor=white)](#5-installation)
[![Runtime: .NET 8](https://img.shields.io/badge/Runtime-.NET%208.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Architecture: MVVM](https://img.shields.io/badge/Architecture-MVVM%20%2B%20WPF-brightgreen)](#4-overall-architecture)
[![Safety: External Window](https://img.shields.io/badge/Design-External%20Window-informational)](#anti-cheat-compatibility)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## 1. Title & Description

**CrossGOverlay** is a Windows crosshair overlay built with **.NET 8** and **WPF**. It renders over borderless windowed games and provides customizable presets, game profiles, and optional weapon-key mappings.

Designed as an open, clean-room alternative to proprietary utilities like Crosshair X, CrossGOverlay couples low-overhead OS-level hooks with real-time profile switching, extensive procedural reticle customization, and full crosshair share-code portability between mainstream titles (such as Counter-Strike 2 and Valorant).

---

## 2. Introduction

Competitive gaming demands visual consistency. However, native in-game crosshairs often suffer from dynamic spread jitter, low visibility against shifting background palettes, or restrictive engine customization limits. Hardware monitors offering built-in overlay crosshairs provide poor alignment, rigid shapes, and clunky on-screen display (OSD) button controls.

**CrossGOverlay** uses a borderless transparent overlay window with `WS_EX_TRANSPARENT`, `WS_EX_LAYERED`, and `WS_EX_NOACTIVATE` to avoid taking focus or mouse clicks from the game. It normally runs as an unprivileged user process (`asInvoker`), does not inject code into games, and does not read game memory. Anti-cheat compatibility depends on each game's rules and cannot be guaranteed.

---

## 3. Key Features

- **External Window Design**: No DLL injection, kernel driver, synthetic input, or game-memory reads/writes. Optional weapon-key mappings use a temporary low-level keyboard observer while a mapped game is foreground.
- **Bi-directional Share-Code Engine**:
  - **Counter-Strike 2**: Full base64-encoded procedural bitpack parsing (`CSGO-xxxxx-xxxxx-...`).
  - **Valorant**: Native 1:1 format serialization and deserialization (`0;P;c;...`).
  - **Validation & Sanity Previews**: Immediate visual verification dialog preventing malformed shares.
- **On-Demand Rendering**: Small WPF layered overlay surface; static crosshairs redraw on changes, while animated content follows the configured frame-rate limit.
- **Per-Monitor V2 DPI Awareness**: Seamless dynamic scaling across multi-monitor setups with disparate scale factors (e.g., 100% vs. 150% scaling) and hot-plug display recovery.
- **Automatic Game Profile Matching**: Dynamically switches reticle presets when specified game processes or window titles enter the foreground via low-overhead `SetWinEventHook` notifications.
- **Passive Global Hotkeys & Raw Input**: Global shortcuts using `RegisterHotKey` and auxiliary mouse buttons (Mouse 3, 4, 5) mapped via `WM_INPUT` (`RIDEV_INPUTSINK`).
- **Engineered Dark Design Language**: Handcrafted UI built on the MongoDB LeafyGreen aesthetic (`#001E2B` deep slate, `#00ED64` neon accents) with hot-swappable English and Vietnamese localization.

---

## 4. Overall Architecture

CrossGOverlay implements a decoupled **Model-View-ViewModel (MVVM)** pattern combined with an asynchronous event-driven Win32 interaction tier.

### System Topology & Interaction Flow

```mermaid
flowchart TD
    subgraph OS_Layer [Windows OS / Win32 Subsystem]
        User32[User32.dll / Shcore.dll]
        DisplayMgr[Display Monitors & DPI Context]
        GameWindow[Target Game Window - Borderless]
    end

    subgraph Service_Tier [Core Infrastructure Tier]
        WinEventHook[WinEvent Hook\nSetWinEventHook]
        RawInputSink[Raw Input Listener\nWM_INPUT Sink]
        HotkeyMgr[Hotkey Dispatcher\nRegisterHotKey]
        ProfileMatcher[GameProfileMatcher]
        SettingsRepo[Preset & Config Repository]
    end

    subgraph ViewModel_Tier [Application ViewModel Tier]
        AppVM[App / Main ViewModel]
        OverlayVM[Overlay ViewModel]
        SettingsVM[Settings & Editor ViewModel]
    end

    subgraph View_Tier [WPF UI]
        SettingsView[Settings Dashboard Window]
        OverlayWindow[Transparent Layered Window\nWS_EX_TRANSPARENT]
        ReticleCanvas[WPF Reticle Visual]
    end

    %% OS Event Dispatching
    User32 -->|Foreground Window Change| WinEventHook
    User32 -->|Mouse 3/4/5 Raw Input| RawInputSink
    User32 -->|Global Shortcuts| HotkeyMgr
    DisplayMgr -->|WM_DPICHANGED| OverlayWindow

    %% Services routing
    WinEventHook -->|HWND / Process ID| ProfileMatcher
    ProfileMatcher -->|Active Profile| SettingsRepo
    SettingsRepo -->|Load Preset| AppVM
    RawInputSink -->|Action Trigger| AppVM
    HotkeyMgr -->|Toggle / Cycle Command| AppVM

    %% ViewModels to Views
    AppVM --> OverlayVM
    AppVM --> SettingsVM
    SettingsVM <==> SettingsView
    OverlayVM -->|Push Vector / Render State| OverlayWindow
    OverlayWindow --> ReticleCanvas
    ReticleCanvas -.->|Renders Floating Above| GameWindow
```

### Architectural Directives

#### Anti-Cheat Compatibility
CrossGOverlay uses only standard, documented Windows User APIs:
- Process introspection is limited strictly to `GetWindowThreadProcessId` and `QueryFullProcessImageNameW` to match window profile names.
- The overlay is marked with `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE`, which instructs the Desktop Window Manager (DWM) to route all mouse and pointer clicks directly through the window to the underlying application.
- The normal global shortcuts use `RegisterHotKey` and mouse Raw Input. When weapon-key presets are configured, a `WH_KEYBOARD_LL` observer is active only while the matched game is foreground; it never suppresses the game's keys. No `WH_MOUSE_LL` hook is used.
- The optional `anticheat_audit.log` records selected window-API transitions. It is diagnostic output, not proof of approval by Vanguard, EAC, BattlEye, or any other anti-cheat.

#### Single-Instance Mutex
Process deduplication is managed via an operating-system-level `System.Threading.Mutex`. Attempting to launch a secondary instance signals the primary instance's system tray handler and brings the active Settings window to the foreground.

---

## 5. Installation

### System Requirements

| Component | Minimum Requirement | Recommended |
|---|---|---|
| **OS** | Windows 10 (Version 1607+) x64 | Windows 11 x64 (Latest Build) |
| **Execution** | None (Self-contained release) | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Building** | [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | Visual Studio 2022 (v17.8+) |
| **Execution Level** | Standard User (`asInvoker`) | Standard User (`asInvoker`) |

### Option A: Pre-Compiled Release (Recommended)

1. Navigate to the project's **[Releases](../../releases)** tab.
2. Download the latest self-contained archive: `CrossGOverlay-<version>-win-x64.zip`.
3. Extract the `.zip` archive to your directory of choice (e.g., `C:\Tools\CrossGOverlay`).
4. Execute `CrossGOverlay.exe`.

> **Note:** Official builds are bundled as self-contained ReadyToRun binaries. The host machine does not need .NET 8 pre-installed.

### Option B: Building from Source

Clone the repository recursively:
```bash
git clone https://github.com/your-username/CrossGOverlay.git
cd CrossGOverlay
```

Restore dependencies and build the solution:
```bash
dotnet restore Crosshair.sln
dotnet build Crosshair.sln -c Release
```

---

## 6. Running the Project

### Running via .NET CLI
```bash
# Debug mode with console logging
dotnet run --project src/CrosshairOverlay/CrosshairOverlay.csproj -c Debug

# Optimized release binary execution
dotnet run --project src/CrosshairOverlay/CrosshairOverlay.csproj -c Release
```

### Automated Release Packaging Pipeline
To run the automated packaging pipeline that executes clean builds, embedded dependency generation, ReadyToRun optimization, and zip packaging:

```powershell
# Standard package generation
.\build\publish.ps1

# Package with digital code-signing applied
.\build\publish.ps1 -PfxPath "C:\certs\code_signing.pfx" -PfxPassword (Read-Host -AsSecureString)
```
The distribution directory and standalone archive will be placed in `artifacts\CrossGOverlay-<version>-win-x64.zip`.

### Default Keybindings

| Key Combination | Action | Description |
|---|---|---|
| `Alt + X` | **Toggle Overlay** | Shows or hides the reticle surface globally. |
| `Alt + ]` | **Next Reticle** | Cycles forward through your local preset library. |
| `Alt + [` | **Previous Reticle** | Cycles backward through your local preset library. |
| `Alt + C` | **Open Settings** | Displays the reticle configuration workspace. |
| `Mouse 3 / 4 / 5` | **User Assignable** | Auxiliary mouse buttons supported via Raw Input. |

---

## 7. Environment & Storage Configuration

CrossGOverlay operates on an isolated file-system configuration model rooted in standard Windows system paths. It does not write to the Windows Registry for reticle settings or user preferences.

### System File Paths

```
%APPDATA%\CrosshairOverlay\
├── settings.json              <-- Core config, hotkeys, game-process profiles
└── presets\                   <-- User and community reticle presets
    ├── default.json
    ├── dot.json
    └── competitive_cross.json

%LOCALAPPDATA%\CrosshairOverlay\
└── logs\                      <-- Rolling application logs (auto-pruned after 7 days)
```

### Configuration Schema: `settings.json` Example
```json
{
  "General": {
    "Language": "en-US",
    "StartWithWindows": false,
    "HideInExclusiveFullscreenWarning": true
  },
  "Hotkeys": {
    "ToggleOverlay": "Alt + X",
    "NextPreset": "Alt + OemCloseBrackets",
    "PreviousPreset": "Alt + OemOpenBrackets",
    "OpenSettings": "Alt + C"
  },
  "GameProfiles": [
    {
      "ProfileName": "Counter-Strike 2",
      "ProcessName": "cs2.exe",
      "WindowTitle": "Counter-Strike 2",
      "PresetId": "competitive_cross",
      "MatchByTitleOnly": false
    }
  ]
}
```

---

## 8. Folder Structure

```
CrossGOverlay/
├── .github/                      # CI/CD workflows, issue templates
├── build/                        # Automation & deployment scripts
│   ├── publish.ps1               # Automated release packaging pipeline
│   └── sign.ps1                  # Authenticode code-signing utility
├── src/
│   └── CrosshairOverlay/         # Primary application project (WPF / .NET 8)
│       ├── Common/               # Global enumerations, helper utilities
│       ├── Interop/              # Native Win32 P/Invoke APIs (User32, Shcore)
│       ├── Models/               # Reticle models, configurations, share codes
│       ├── Native/               # Raw input sinks, WinEvent hook abstractions
│       ├── Resources/            # LeafyGreen XAML styles, icon vectors
│       │   ├── Localization/     # Strings.resx, Strings.vi.resx
│       │   └── Theme.xaml        # Core styling specifications
│       ├── Services/             # Profile matchers, preset persistence, hotkeys
│       ├── ViewModels/           # MVVM ViewModels coordinating state
│       ├── Views/                # XAML Windows (Overlay, Settings Dashboard)
│       ├── App.xaml              # Application composition root & DI
│       └── CrosshairOverlay.csproj
├── tests/
│   └── CrosshairOverlay.Tests/   # Unit & functional validation suites (xUnit)
│       ├── Cs2ShareCodeTests.cs
│       ├── ValorantCodeTests.cs
│       ├── GameProfileMatcherTests.cs
│       └── PresetRepositoryTests.cs
├── ARCHITECTURE.md               # Detailed technical design specifications
├── Crosshair.sln                 # Visual Studio solution file
├── LICENSE                       # MIT licensing declaration
└── README.md                     # Project documentation root
```

---

## 9. License

CrossGOverlay is licensed under the open-source **[MIT License](LICENSE)**.

```
Copyright (c) 2026 CrossGOverlay Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
```

---

## Acknowledgements

Special thanks to **[HuuwxLoiwf (HuuLoii)](https://github.com/HuuwxLoiwf)** for architectural collaboration, game-profile testing, and core contributions to the project ecosystem.
