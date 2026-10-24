# The Island

A lightweight WPF/C# desktop island for Windows 10 2004 (build 19041) or newer and Windows 11.
This upgrades the existing application; internal namespace `RioIsland` and project path
`src/RioIsland/RioIsland.csproj` are intentionally preserved. The executable is **TheIsland.exe**.

## Features

- Black pill with layered glass-like gradients, a subtle static edge glow, and short spring-like expand/collapse motion.
- Windows current media session: previous, play/pause, next, album artwork, elapsed time and progress.
  Unsupported commands are disabled. Media availability depends on the player.
- Local timestamp-synced LRC lyrics, including multiple timestamps, fractional seconds and signed
  `[offset:milliseconds]`. Positive offsets show lyrics earlier. Load a file for the current track;
  changing tracks clears that association to avoid displaying the wrong lyrics. Blank timestamped
  lines clear the display during instrumental sections.
- Minimal eye mascot: random blinks, greeting on open, sleep after 45 seconds with the island collapsed,
  no playback and no running focus timer. This uses island activity, not global input monitoring.
- Existing 25-minute focus timer with pause/reset and tray notification.
- Existing opt-in clipboard capture/restore, in memory only.
- Existing file-drop shelf (filename/count summary), clear action, tray menu, Ctrl+Alt+I hotkey and
  opt-in Start with Windows. Escape collapses the panel.
- Existing CPU and RAM metrics.

## Build

Install the .NET 10 SDK, then run `build-windows.bat` from any directory.
On success, run `build/TheIsland.exe`. The batch file can also be replaced with:

```powershell
dotnet publish src/RioIsland/RioIsland.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o build
```

The published app does not need a separately installed .NET runtime.
Keep the executable in its permanent location before enabling startup.
If an older version has startup enabled, launching the published new executable migrates the existing
opt-in to its current location and removes the legacy startup entry. Startup remains off by default.

GitHub Actions retains the existing `windows-latest` / .NET 10 / self-contained win-x64 flow.
It runs the lyric regression checks, publishes, verifies `dist/TheIsland.exe`, and uploads
**The-Island-Windows-x64**. Runs are triggered by main pushes, pull requests and manual dispatch.
Download the artifact, then extract the ZIP inside it.

## Use

Click the compact island or press Ctrl+Alt+I to expand. Play music in an app that exposes a Windows
media session. Use **Load .lrc** to select a UTF-8 lyric file matching the current recording.
The island follows the player timeline, including pause/resume and seeks reported by that player.
Use the player's own seek control; the island progress bar is a read-only indicator.

Right-click the tray icon to change startup behavior or exit. Clipboard text is read only when
**Capture** is clicked. Dropped files are not copied, opened, uploaded or retained after exit.

## Performance

No new third-party dependencies, browser engine, background network calls, backdrop blur shaders or
permanent rendering callbacks. Metadata/artwork update from Windows events, not a polling loop.
Only one 128-pixel-wide decoded artwork is retained; source thumbnails larger than 4 MB are skipped.
LRC input is limited to 1 MB.

The housekeeping timer runs once per second. CPU/RAM counters update every two seconds while expanded.
The 250 ms position/lyric timer runs only while expanded, playing and a usable timeline exists.
Animations end after a short transition. Windows reduced-motion preferences, remote sessions and
software-rendering tier disable decorative motion. Event handlers and timers are detached at shutdown.

Glass is a lightweight visual treatment, not native Acrylic/Mica desktop blur.
Low-resource implementation choices do not establish a measured CPU/RAM budget.

## Verification

Run the dependency-free parser checks:

```powershell
dotnet run --project tests/TheIsland.Tests/TheIsland.Tests.csproj -c Release
```

The upgrade was locally published on Windows with .NET SDK 10.0.402 as self-contained win-x64.
Fourteen parser checks passed, covering fractions, multiple tags, offsets, malformed input, duplicate
timestamps, instrumental blanks and backward seeks. A separate WPF smoke harness loaded the real
XAML, exercised 20 rapid toggles and rendered the expanded panel.

Before releasing, check on your Windows desktop:

1. Expand/collapse repeatedly, Ctrl+Alt+I, Escape and tray exit. Check 100%, 125% and 150% DPI.
2. With Spotify/browser/local player: artwork, commands, pause/resume, forward/backward seek,
   switching active players, closing the player and no-artwork fallback.
3. Load a matching LRC; verify exact timestamps, offsets, pause and track-change clearing.
4. Check focus completion notification, clipboard capture/restore, file drop/clear and startup opt-in.
5. Observe mascot idle sleep/wake and reduced-motion behavior. Compare Task Manager CPU and memory
   while collapsed, expanded, paused and playing on the intended older PC.

## Limits

Actual third-party player integration, audible playback, startup/login behavior, notifications,
DPI variations and sustained CPU/RAM usage have not been manually verified on target hardware.
Artwork, duration, playback rate and event accuracy depend on what each player exposes.
Missing duration/timeline shows `--:--` and a lyric availability message rather than guessing.
Lyrics are line-synced, not word-by-word karaoke; there is no automatic lyric download.
Track association uses source app, title, artist, album and track number, so indistinguishable metadata
cannot identify separate recordings. The shelf remains a non-persistent filename/count summary.
The existing primary-work-area positioning remains; there is no display selector.
Global hotkey availability depends on conflicts with other apps.
No notification capture from other apps, GPU metrics or installer/signing has been added.

## Source map

- `MainWindow.xaml`: pill, eye shapes, media card and preserved utility controls.
- `MainWindow.xaml.cs`: UI motion, mascot states, lyric binding, timer, metrics, tray and utilities.
- `MediaSessionService.cs`: Windows media events, commands, bounded artwork and timeline state.
- `LrcLyrics.cs`: local timestamp parser and binary-search lookup.
- `NativeMetrics.cs`: existing Win32 CPU/RAM metrics (unchanged).
- `RioIsland.csproj`: existing framework settings, executable/product identity.
- `.github/workflows/windows-build.yml`: Windows build and packaging.
- `tests/TheIsland.Tests`: lightweight parser regression checks.

Upgrade baseline: repository commit `ae63dfacf3d920124125be14b1ba5869a28b1354`.
