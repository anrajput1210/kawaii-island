# Kawaii Island

A cute, customizable **Dynamic Island for Windows 11** (macOS version in progress). A small pill docks at the top of your screen and expands with springy micro-animations to show music, mail, notifications and app shortcuts. A mascot reacts to what you do.

<p align="center"><img src="design/mascot/icon.png" width="96" alt="Kiko, the default mascot"></p>

**It never covers your browser tabs.** The island reserves its own strip of screen as a Desktop AppBar, so maximized windows stop below it instead of sliding underneath.

> **Status: early development.** Phases 1–6 (skeleton, animations, drag & snap, AppBar, settings, auto-hide) and Code mode are done. See [Roadmap](#roadmap). Try the design in your browser: open [`design/mockup/index.html`](design/mockup/index.html).

## Features (planned for v0.1)

- **AppBar workspace reservation** (top/bottom/left/right): windows stop below the island. The reservation is released cleanly on exit or crash.
- **Music from any player** via Windows media controls (SMTC): Spotify, Apple Music, browser tabs, VLC… with no login. The accent color comes from the album art.
- **Notifications mirroring** (toasts), **mail** headers over IMAP (unread count and latest 5; bodies are never downloaded), **app shortcuts** (drop a file on the island to pin it).
- **5 mascots** (Kiko, Miso the cat, Bun the bunny, Bolt the robot, Ribbit the frog). They blink, peek out to say hi when the island is hidden, squish when clicked, and get dizzy if you click three times fast.
- Drag to move, edge snapping, auto-hide in fullscreen, dark/light themes, Ctrl+Alt+I hotkey, tray icon.
- **Code mode (Claude Code companion):** live status of your Claude Code sessions (thinking, which tool is running, needs your permission, done), plus context used, 5-hour and weekly plan usage with reset times, and session cost. See below.

## Code mode (Claude Code)

Turn it on from the tray: **Code mode (Claude Code)**. After you confirm, Kawaii Island adds a few entries to `~/.claude/settings.json` (a backup is saved as `settings.json.kawaii-backup`):

- **Hooks** for session, prompt, tool, permission, notification and stop events. Each one is an `async` `curl` call to `http://127.0.0.1:47811/kawaii/hook`, so Claude Code never waits on them. There's no helper program that could go missing, and if the island isn't running the call just fails quietly.
- **A status line**, but only if you don't already have one. It sends usage to the island and shows `🏝 ctx 23% · 5h 41% · wk 12%` in Claude Code. Plan usage (5-hour and weekly) comes from Claude Code's status line data, which is only available on Pro and Max plans.

Turning Code mode off, or uninstalling the app (`KawaiiIsland.exe --uninstall-hooks`), removes exactly those entries and nothing else. Restart open Claude Code sessions after you toggle it, because hooks load when a session starts. The listener only accepts connections from this PC, and session data is kept in memory only.

## Privacy: everything stays on your device

- No accounts, no cloud sync, no telemetry, no analytics.
- Settings: `%LOCALAPPDATA%\KawaiiIsland\config.json` (local, not roaming). macOS: `~/Library/Application Support/KawaiiIsland/`.
- Mail passwords are encrypted with Windows DPAPI (current user, this machine). On macOS they go in the login Keychain with iCloud sync disabled. They are never written to `config.json`.
- The only network traffic is to **your own mail server** if you turn on IMAP.

## Build & run (Windows)

Requirements: Windows 10 2004+ / Windows 11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
cd windows
dotnet build
dotnet test
dotnet run --project src/KawaiiIsland
```

## Repository layout

| Path | What |
|---|---|
| `windows/` | WPF app (`src/KawaiiIsland`) + xUnit tests |
| `macos/` | Native Swift app (coming) |
| `design/` | Interactive mockup, design tokens, mascot source (`mascot/mascot.js`) and generators |
| `installer/` | Windows installer + macOS DMG packaging (coming) |

Mascot art has a single source: `design/mascot/mascot.js`. Regenerate the SVGs and the WPF `Mascots.xaml` with `node design/mascot/gen.mjs`, and the app icon with `node design/mascot/icons.mjs` (uses your installed Chrome in headless mode).

## Roadmap

- [x] 0 · Design mockup, repo
- [x] 1 · Skeleton: borderless topmost pill, tray icon, single instance, config service
- [x] 2 · Animations: expand/collapse spring, hover, mascot moods
- [x] 3 · Drag, snap, position persistence
- [x] 4 · AppBar workspace reservation (+ collapse-after timeout: 4 / 10 / 15 / 30 s / never)
- [x] Code mode: Claude Code companion (activity, context, 5-hour / weekly usage)
- [x] 5 · Settings window and right-click menu (live apply, dark/light/auto theme, accent, mascot picker)
- [x] 6 · Auto-hide in fullscreen and the mascot peek
- [ ] 7 · Music (SMTC)
- [ ] 8 · Notifications mirroring
- [ ] 9 · Mail (mock, then IMAP)
- [ ] 10 · App shortcuts and file drop
- [ ] 11 · Polish, themes, start with Windows, logging
- [ ] Installers: Windows setup `.exe` and macOS `.dmg` (built in GitHub Actions)

## Known limitations

- Mirroring toast notifications needs app identity on Windows (MSIX or a sparse package). Without it, the module shows a short explanation instead.
- Builds aren't code-signed yet, so Windows SmartScreen and macOS Gatekeeper will warn on first launch.

## License

[MIT](LICENSE). The mascots are original characters made for this project.
