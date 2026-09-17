![GamingStack](banner.png)

# GamingStack

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![Status](https://img.shields.io/badge/status-WIP-orange)

A fully automated, over-the-top retro-styled Windows 11 gaming setup app.
Boots through a fake BIOS screen (pulling your *real* CPU/RAM/GPU/disk info),
into an original Win95-inspired boot animation, onto a desktop, then opens a
terminal window that installs a full gaming/streaming app stack and applies
gaming tweaks — all in one self-contained app, no scripts or extra files to
lose. Download it, run it, walk away.

## Features

- **Fake BIOS POST screen** — genuinely reads your CPU, RAM, GPU and disks
  and displays them like a real boot screen, including an animated memory
  count-up with tick beeps
- **Original retro boot animation** — gradient sky, drifting pixel clouds,
  and an animated waving flag (all original artwork — no real Windows
  assets are used, see [Credits & copyright](#credits--copyright))
- **Desktop stage** with a taskbar (live clock), desktop icons, and a
  wallpaper
- **Real installer**, running inside a terminal window that animates open:
  installs a full app stack via `winget` with retries, handles a couple of
  apps that need manual download/install, and applies a few gaming-related
  tweaks (Game Mode, hardware-accelerated GPU scheduling, High Performance
  power plan)
- **Easter egg** — press <kbd>Delete</kbd> during the BIOS screen
- Runs as a single self-contained `.exe` — no separate files to install
  alongside it

## What it installs

Game launchers (Steam, Epic, GOG, Ubisoft Connect, Battle.net, Amazon
Games, Playnite to tie them all together), streaming/recording tools (OBS
Studio, Streamlabs, Medal, Voicemeeter Banana), monitoring/utility apps
(HWiNFO, CPU-Z, Speccy, CrystalDiskInfo, Process Lasso), and general
utilities (VS Code, PowerShell, Office, PowerToys, Python, VLC, 7-Zip).
The full list — and how to change it — is in `InstallerEngine.cs`.

A couple of apps with no reliable `winget` package (Hyte Nexus, L-Connect 3)
are downloaded and installed directly instead.

## Requirements

- Windows 10/11 (this uses WinForms and WMI, so it's Windows-only)
- [`winget`](https://apps.microsoft.com/detail/9nblggh4nns1) (App Installer)
  — comes preinstalled on current Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) if you're
  building it yourself
- Admin rights — the app requests elevation on launch (needed for the
  install/tweak stage)

## Getting started

```bash
git clone <this-repo-url>
cd <cloned-folder>
dotnet restore
dotnet run
```

Press **Esc** at any point to close the window while testing, or
**Delete** during the BIOS screen for the easter egg.

> **Debugging note:** `dotnet run`/F5 use a Debug-only manifest that does
> *not* request admin rights (`App.Debug.manifest`) — this is intentional.
> Requesting elevation from a debug launch throws "the requested operation
> requires elevation", because `dotnet run` starts the app via
> `CreateProcess`, which can't silently elevate a child process the way
> double-clicking an .exe can. Registry tweaks will just no-op/fail
> gracefully in Debug as a result. To test the real elevated behavior,
> either run a Release publish (below) or launch VS Code itself as
> Administrator before running.

### Building a standalone .exe

```bash
dotnet publish -c Release -r win-x64
```

The output lands in `bin\Release\net8.0-windows\win-x64\publish\` as a
single self-contained executable, using the real `App.manifest`
(`requireAdministrator`) — double-clicking it triggers the UAC prompt as
expected.

## Roadmap

- [x] Retro boot sequence (BIOS, boot animation, desktop, terminal)
- [x] Real installer stack (winget loop, manual installer fallback, tweaks)
- [ ] Real boot animation assets (currently original placeholder artwork)
- [ ] Installation log viewer / summary screen

## Safety note

This app requests admin rights and will install applications and modify
registry settings on your machine. Test it in a VM or a spare machine
before running it on anything you care about, and review the app list in
`InstallerEngine.cs` before running it on your own PC.

## Credits & copyright

The retro aesthetic here is inspired by classic BIOS boot screens and
Windows 9x, but every visual asset (the logo, the flag animation, the
clouds, the icons, the BSOD easter egg text) is original artwork made for
this project — no Microsoft logos, boot animations, or copyrighted assets
are included or redistributed. The banner image above and the desktop
wallpaper are AI-generated.

## License

[MIT](LICENSE) — do what you like with it.
