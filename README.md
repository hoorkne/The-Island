# RIO Island · Windows Dynamic Island (MVP v0.1)

A custom, lightweight-leaning, always-on-top Dynamic Island-inspired desktop UI for Windows 10 (2004+) and Windows 11. Original WPF/C# implementation: **not a copy of the Nusa application**. Futuristic dark navy, cyan and violet styling.

## Features implemented in source

| Feature | v0.1 status |
|---|---|
| Expand/collapse top-center island | Implemented (280 ms easing) |
| Dark futuristic overlay | Implemented (gradient, rounded shell) |
| Windows current media track / artist | Implemented via Windows Media Control (depends on player support) |
| Media previous, play/pause, next | Implemented (depends on player support) |
| CPU/RAM percentage | Implemented with built-in Win32 APIs |
| Focus timer 25 minutes | Implemented with pause/reset and tray balloon |
| Timed lyrics (.lrc) | Implemented for manually loaded local LRC files; depends on player sharing timeline |
| Clipboard capture / restore | Implemented, **only on button click**, in memory |
| File drop shelf | Implemented for filename summary; not a persistent file manager |
| System tray menu + exit | Implemented |
| Ctrl+Alt+I hotkey | Implemented, if shortcut is available |
| Launch with Windows | Implemented via current-user Run registry setting, opt-in |
| Capture other apps' Windows notifications | **Not yet implemented** (needs permissions, privacy UI, and testing) |
| Automatic synced-lyrics fetching | **Not implemented** (source/licensing considerations) |
| Multi-monitor selector, DPI refinement | **Not yet implemented** (primary screen only) |
| GPU metrics, themes and music artwork | Planned |

### Prerequisites

- Windows 10 2004 (build 19041) or newer; Windows 11 recommended.
- To **compile:** .NET 10 SDK, Visual Studio 2022/2026 with desktop .NET workload, or CI via GitHub Actions.
- Published self-contained `RioIsland.exe` does **not** require separately installing the .NET runtime, but may extract native libraries on first run.

## Fastest route: compile on your Windows PC

1. Install [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (SDK, not just runtime).
2. Extract the `RIO-Island-Source.zip` archive.
3. Double-click `build-windows.bat` (or open CMD in the extracted folder and run it).
4. If build succeeds, launch `build\RioIsland.exe`.
5. Click the mini island to expand; `Ctrl+Alt+I` toggles it. Right-click the tray icon for **Start with Windows** or **Exit**.

**Note:** This source bundle was prepared without a Windows/.NET build environment, so compilation and native integration have **not been tested**. If you see a compiler error, keep the entire output and use it to troubleshoot; do not treat this as a verified working executable yet.

## Build in GitHub, without SDK on your own PC

1. Create a new GitHub repository (`RIO-Island`).
2. Upload **all files inside the extracted project folder**, including the hidden `.github/workflows/windows-build.yml` path.
3. In the repository, open **Actions** → **Build RIO Island for Windows** → **Run workflow**. Ensure Actions are enabled.
4. If the Windows build succeeds, download artifact **RIO-Island-Windows-x64** and extract the ZIP, then launch `RioIsland.exe`.

This is a build workflow, **not an installer**. A signed installer can be added later. Windows SmartScreen may warn on unsigned or newly built apps; don't bypass warnings for executables you cannot verify.

### Project map

- `src/RioIsland/MainWindow.xaml` — shell UI, expansion panel, colors
- `src/RioIsland/MainWindow.xaml.cs` — animations, media sessions, timer, tray, drag/drop
- `src/RioIsland/NativeMetrics.cs` — CPU/RAM via Win32 P/Invoke
- `src/RioIsland/LrcLyrics.cs` — local timestamped LRC parsing
- `.github/workflows/windows-build.yml` — self-contained Windows x64 CI build

### Performance design

- No Chromium, no Electron, no embedded browser.
- System counters update once per second, media track metadata ~every 3 seconds.
- UI is completely hidden when collapsed except the small pill-shaped island.
- Actual RAM/CPU footprint and animation FPS still require measurements on Windows hardware.

### Security / privacy

- Clipboard is **never monitored in the background**; text is captured only on explicit click and kept in process memory.
- The app does not upload files or clipboard content and has no network communications in its own code.
- `Start with Windows` modifies only `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run` and is off by default.
- LRC lyrics are read from a file the user selects; no lyrics are downloaded.

### References

Design inspiration only (no source code copied): [Nusa](https://rama2555.github.io/nusa/) and [Windows Dynamic Island](https://github.com/HeyyCzer/windows-dynamic-island). [Microsoft Windows.Media.Control API](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager.requestasync).

### Next milestones

1. Compile and smoke-test on Windows 10/11; adjust any build errors or DPI quirks.
2. Album artwork and playback progress; better file shelf; display selection.
3. Consent-based Windows notification access, not intrusive global hooks.
4. Configurable theme/settings, volume overlay, GPU monitor; optional lyrics provider under acceptable terms.
