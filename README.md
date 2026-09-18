![GamingStack](banner.png)

# GamingStack

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![Status](https://img.shields.io/badge/status-WIP-orange)
[![Version](https://img.shields.io/badge/version-0.19.0-brightgreen.svg)](CHANGELOG.md)

A fully automated, over-the-top retro-styled Windows 11 gaming setup app.
Boots through a fake BIOS screen (pulling your *real* motherboard/CPU/RAM/
GPU/disk info), into an original Win95-inspired boot animation, onto a
desktop, then opens a terminal window that installs a full gaming/streaming
app stack and applies gaming tweaks — all in one self-contained app, no
scripts or extra files to lose. Download it, run it, walk away.

The core setup flow (below) is done and stable. Beyond that, this is an
actively growing project — see [`ROADMAP.md`](ROADMAP.md) for everything
being considered next (more tweaks, more apps, more automation, more fun).
Every idea there follows one non-negotiable rule: **anything that changes a
system setting is disclosed on screen before or as it happens, and can be
undone.** Nothing here ever changes something silently or leaves you unable
to put it back.

## Features

- **Fake BIOS POST screen** — genuinely reads your motherboard, CPU, RAM,
  GPU and disks (including each disk's real health status and whether
  TRIM is enabled) and displays them like a real boot screen, including an
  animated memory count-up with tick beeps
- **Original retro boot animation** — gradient sky, a hand-pixelled sun,
  a rolling-hills silhouette, drifting pixel clouds, and a waving flag that
  animates off a pre-baked sprite sheet, all hand-authored pixel art scaled
  up crisp and chunky (no real Windows assets are used, see
  [Credits & copyright](#credits--copyright))
- **Desktop stage** with a taskbar (live clock), desktop icons, and a
  wallpaper
- **Interactive install wizard**, running inside a terminal window that
  animates open: shows the full app list grouped under a heading per
  category, typed out Resident Evil save-point style with a mechanical
  typewriter tick, then asks whether to install everything, pick items one
  by one, or bail out — see [How the install works](#how-the-install-works)
  below
- **Real installer**: installs the selected apps via `winget` with
  retries, silently installs every VC++ Redistributable version as one
  bundled entry, and handles a few apps that need manual download/install
- **A System Restore point, always** — created once app selection is
  finalized, before anything installs or changes, no consent screen
  needed for this one. On top of that, an **optional full disk-image
  backup** (before any changes, or after everything's set up, your call)
  — see [Restore point & backup](#restore-point--backup) below
- **Optional tweaks** — Game Mode, hardware-accelerated GPU scheduling, a
  choice between the High Performance or hidden Ultimate Performance power
  plan, Explorer/taskbar decluttering (file extensions, hidden files, the
  classic right-click menu, hiding the widgets/search/Copilot buttons),
  background-task CPU reservation (MMCSS), Start menu suggestions/ads,
  Fast Startup, Windows Update active hours, Xbox Game Bar/Game DVR, and
  network latency (Nagle's algorithm) — asked about explicitly, never
  applied silently: a
  dedicated screen shows each one's real current value and what it would
  change to, before anything happens. Saying yes writes a
  `revert-tweaks.cmd` first, which restores every setting to exactly what
  it was — see [Tweaks](#tweaks) below
- **Install summary** at the end of a run — totals plus a per-item
  installed/failed/skipped breakdown, grouped by category, with a
  plain-English reason next to anything that failed or was skipped (known
  winget outcomes like "already installed and up to date" are translated
  out of raw exit codes rather than shown as a mystery failure)
- **Full install log** written to disk every run (path shown on the
  summary screen) — every line the terminal window shows, not just failures
- **Fake shutdown outro** — a Windows 9x-style shutdown sequence plays
  after the install summary, ending on the classic "It's now safe to turn
  off your computer" screen, before moving on to the sign-off
- **Achievement toasts** — small pop-ups (First Blood, Completionist,
  Redistributable Rampage, Already Perfect, Not Everything's Meant To Be,
  Speedrunner) fire during a run the moment their real condition is met,
  with a tracker on the summary screen showing which ones you've earned
  across the session
- **Easter eggs** — press <kbd>Delete</kbd> during the BIOS screen for a
  joke blue screen, decline every install option in the wizard for a small
  surprise, or find the hidden classic cheat code for a throwaway joke toast
- Runs as a single self-contained `.exe` — no separate files to install
  alongside it

## How the install works

Once the boot sequence finishes, the terminal window shows the full list
of what's on offer and asks:

1. **Install everything shown above? [Y/N]** — `Y` installs the full list.
   `N` moves to the next question.
2. **Would you like to choose what's installed? [Y/N]** — `Y` walks
   through every item one at a time (`Y` installs it, `N` skips it),
   showing a running checklist as you go. `N` moves to the next question.
3. **Just want to quit? [Y/N]** — `Y` closes the app. `N` leads to a
   small easter egg, then the same sign-off screen you'd see after a
   real install.

Once a real app selection is finalized (everything, or a hand-picked
subset — even an empty one), a System Restore point is created, an
optional full disk-image backup is offered, and then a screen asks about
the tweaks — in that order, before installing anything. See
[Restore point & backup](#restore-point--backup) and
[Tweaks](#tweaks) below.

After a real install run, the summary screen leads into a fake shutdown
sequence before the sign-off screen — press any key to move past the
summary, the rest plays itself out.

## Restore point & backup

Once app selection is finalized, GamingStack always creates a Windows
System Restore point first — no screen to confirm it, it just happens,
since it's the baseline safety net for everything that follows (installs
included, not just the tweaks below). If System Restore happens to be off
for the system drive (the Windows default on most consumer PCs), it's
switched on and the restore point is retried once before giving up.

On top of that, a screen offers a full disk-image backup via `wbadmin`:

```
[1] Before anything is installed or changed (a clean, "virgin machine" backup)
[2] After everything is installed and tweaked (a "gaming ready" backup)
[3] Skip the image backup - just the restore point above
```

Choosing 1 or 2 leads to a drive picker that auto-detects eligible drives
(anything NTFS-formatted that isn't the system drive) and shows each
one's free space next to the rough space the backup needs, so it's never
a guess:

```
[1] E: (Backup Drive) - 412.0 GB free
[2] F: (USB) - 58.0 GB free  (may not be enough room)
```

Whichever timing is chosen, the backup runs to completion before the flow
continues — a backup racing against installs in the background would
capture a half-changed system, not the clean before/after snapshot the
choice is meant to give you. Expect it to take anywhere from a few minutes
to a couple of hours depending on the destination, with a plain "don't
turn off your PC" screen and an elapsed timer while it runs. The summary
screen reports what actually happened to both the restore point and the
backup, including the backup's own log file.

## Tweaks

Right before the tweaks screen, one small question decides what the power
plan tweak targets:

```
[1] High performance - Windows' own built-in plan (recommended)
[2] Ultimate Performance - a hidden Microsoft plan with the last few
    power-saving throttles removed on top of High performance

Choose 1 or 2
```

Ultimate Performance is real and Microsoft-documented, just hidden by
default since it has no benefit on a laptop and a small idle-power cost on
a desktop that's plugged in. Picking it duplicates the hidden template
into a real, switchable plan the first time (reusing that same duplicate
on any later run, rather than creating a new one every time).

Then a dedicated screen lists every tweak GamingStack can apply — the
gaming tweaks (Game Mode, hardware-accelerated GPU scheduling, the power
plan you just chose), a set of Explorer/taskbar decluttering tweaks (file
extensions, hidden files, the classic right-click context menu, and hiding
the taskbar's widgets/search/Copilot buttons), and a handful more (
background-task CPU reservation, Start menu suggestions/ads, Fast
Startup, Windows Update active hours, Xbox Game Bar/Game DVR, and network
latency) — each with its real current value (read live, not assumed) and
what it would become:

```
Game Mode                                   currently: not set (Windows default)  ->  enabled
Hardware-accelerated GPU scheduling         currently: off                        ->  on
Power plan                                  currently: Balanced                   ->  High performance
File extensions                             currently: hidden (Windows default)   ->  shown
Hidden files                                currently: hidden (Windows default)   ->  shown
Right-click context menu                    currently: modern (Windows 11 default) -> classic (full menu, no "Show more options")
Taskbar widgets button                      currently: shown (Windows default)    ->  hidden
Taskbar search box                          currently: shown (Windows default)    ->  hidden
Taskbar Copilot button                      currently: shown (Windows default)    ->  hidden
Background task CPU reservation (MMCSS)     currently: 20% (Windows default)      ->  0% (games get full priority)
Start menu suggestions/ads                  currently: shown (Windows default)    ->  hidden
Fast Startup                                currently: on (Windows default)       ->  off
Windows Update active hours                 currently: 08:00-17:00 (Windows default) -> 16:00-23:00 (typical evening gaming window)
Xbox Game Bar / Game DVR                    currently: enabled (Windows default)  ->  disabled
Network latency (Nagle's algorithm)         currently: not set (Windows default, Nagle's algorithm enabled) -> disabled

Apply these tweaks?   [Y] Yes    [N] No, skip tweaks
```

The Explorer/taskbar tweaks are per-user settings, so they take effect
next time Explorer restarts (typically your next sign-in) rather than
instantly — that's expected, not a failure. The Nagle's algorithm tweak
targets whichever network interface is actually active right now (found
via WMI, since this is a per-adapter setting, not a global one) — if none
can be identified, that row shows "no active network adapter found" and
is skipped rather than guessed at.

`N` skips every tweak — nothing is touched. `Y` applies them, but not
before writing `revert-tweaks.cmd` next to the install log: a plain,
readable script that restores every setting to exactly what it was
before (or removes it entirely, if it wasn't set at all beforehand). Its
full path is shown on this screen and again on the summary screen
afterward, so it's never something you have to go hunting for. Run it as
Administrator any time to undo everything this step changed.

This applies to any future tweak added from `ROADMAP.md`, too — it's a
hard rule for this project, not a one-off: nothing that changes a system
setting happens without being shown on screen first, and nothing happens
that can't be put back.

## What it installs

The wizard groups everything under a heading per category:

- **Game Launchers** — Steam, Epic, GOG, Ubisoft Connect, Battle.net,
  Amazon Games, Discord, Playnite to tie the launchers together
- **Monitoring & Performance** — NVIDIA App, HWiNFO, CPU-Z, Speccy,
  CrystalDiskInfo, Process Lasso, Microsoft PC Manager, Razer Cortex
  (direct download - no winget package exists for it)
- **Streaming & Recording** — Streamlabs Desktop, Medal.tv, Voicemeeter
  Banana
- **General Utilities** — VS Code, PowerShell 7, Microsoft 365 Apps,
  PowerToys, Python 3, VLC, 7-Zip, Vortex Mod Manager
- **Redistributables** — a single "VC++ Redistributables Pack" entry that
  silently installs every VC++ runtime version (2005 through 2015+, x86
  and x64) in one go, since so many games and creator apps quietly expect
  one of these already being present
- **RGB & Peripheral Control** — Corsair iCUE, Razer Synapse, OpenRGB (a
  single open-source app that talks to several vendors' hardware at once,
  handy if you don't want four separate vendor apps running), plus Hyte
  Nexus and L-Connect 3

The full list — and how to change it — is the `Catalog` in
`InstallerEngine.cs`.

Apps with no reliable `winget` package (Razer Cortex, Hyte Nexus,
L-Connect 3) are downloaded and installed directly instead, but sit in
the same pickable list as everything else so you can skip them if you
don't own that hardware.

## Requirements

- Windows 10/11 (this uses WinForms and WMI, so it's Windows-only)
- [`winget`](https://apps.microsoft.com/detail/9nblggh4nns1) (App Installer)
  — comes preinstalled on current Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) if you're
  building it yourself
- Admin rights — the app requests elevation on launch (needed for the
  install/tweak stage, and for the restore point/backup stage)
- `wbadmin` — only needed if you choose the optional full disk-image
  backup; ships with Windows 10/11 by default. Skipping that screen (or
  picking "skip") doesn't need it at all

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

The original build order is complete:

- [x] Retro boot sequence (BIOS, boot animation, desktop, terminal)
- [x] Real installer stack (winget loop, manual installer fallback, tweaks)
- [x] Interactive install wizard (install all / pick individually / quit)
- [x] Installation summary screen
- [x] Fake "shutting down" outro
- [x] Achievement toast notifications
- [x] Konami code easter egg
- [x] Real boot animation assets (hand-pixelled sprites, original artwork)

Everything under consideration beyond that — more system tweaks, more apps,
more automation, more fun — lives in [`ROADMAP.md`](ROADMAP.md), grouped by
kind, along with the disclosed/reversible rule anything system-changing has
to follow before it's in scope.

## Safety note

This app requests admin rights and will install applications and modify
registry settings on your machine. Test it in a VM or a spare machine
before running it on anything you care about, and review the app list in
`InstallerEngine.cs` before running it on your own PC. Every system-level
tweak it applies (today: Game Mode, hardware-accelerated GPU scheduling,
the power plan - including the hidden Ultimate Performance plan, if you
pick it - file extensions/hidden files, the classic right-click context
menu, the taskbar widgets/search/Copilot buttons, MMCSS background-task
CPU reservation, Start menu suggestions/ads, Fast Startup, Windows Update
active hours, Xbox Game Bar/Game DVR, and Nagle's algorithm) is a
standard, documented Windows setting — nothing here is a hidden or one-way
change, and that stays true for anything added from
[`ROADMAP.md`](ROADMAP.md) going forward.

## Credits & copyright

The retro aesthetic here is inspired by classic BIOS boot screens and
Windows 9x, but every visual asset (the logo, the boot animation's sun,
hills, clouds and flag sprites, the icons, the BSOD easter egg text) is
original artwork made for this project — no Microsoft logos, boot
animations, or copyrighted assets are included or redistributed. The
banner image above and the desktop wallpaper are AI-generated.

## License

[MIT](LICENSE) — do what you like with it.
