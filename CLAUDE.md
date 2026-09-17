# GamingStack GUI

## What this is

A fully automated, visually over-the-top Windows 11 gaming setup app. Retro
Win95/98-styled boot sequence, then it installs a full gaming app stack and
applies gaming-related tweaks. The whole point is minimal manual steps plus
maximum nostalgia.

## History (read this before "helpfully" restructuring anything)

This started as a C# WinForms launcher that shelled out to a separate
PowerShell script, with images/sound as separate files. It worked in dev but
broke completely in a live test - the launcher never actually extracted the
PS1 script or the boot images to where it expected them, so the handoff
silently failed. Lesson taken: **no process handoffs, no separate asset
files that can go missing on a different machine.**

Two rebuilds happened after that:
1. A single self-contained `.ps1` (embeds win95.wav as base64, embeds the
   app list) - proved the "everything in one file" approach works.
2. This project: the real end goal. All-in-one C#/WinForms, no PowerShell
   shell-out at all. Installer logic (winget calls, tweaks, etc.) will be
   ported directly into C# rather than calling out to a script.

**Do not reintroduce a split between a GUI process and a script/installer
process.** That exact architecture already failed once.

## Current architecture

Single WinForms project (`net8.0-windows`), self-contained single-file
publish. Everything lives in one process:

- `Program.cs` - entry point, standard WinForms bootstrap.
- `MainForm.cs` - the entire app. A borderless, maximized `Form` with a
  hand-rolled state machine (`enum Stage`) driven by a `Timer` + `Stopwatch`,
  rendered via `OnPaint`/GDI+. No separate windows/forms per stage - it's
  all one canvas that changes what it draws based on `_stage`.
- `App.manifest` - requests admin elevation via `requireAdministrator`
  (no relaunch-and-exit trick needed, unlike the old PS1 version).
- `Resources/win95.wav` and `Resources/wallpaper.jpg` - both embedded
  resources, located at runtime by name match
  (`GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(...))`)
  rather than a hardcoded resource path, specifically to avoid the exact
  "embedded resource path mismatch" bug that broke the first version.
- `InstallerEngine.cs` - the actual install stack (winget loop, manual
  installer fallback, tweaks), decoupled from the UI via an `OnLog`
  event so it doesn't know anything about WinForms.

## Stage flow (`MainForm.cs`)

```
Bios -> Boot -> Desktop -> Terminal
  \-> Bsod -> Boot   (if Delete is pressed during Bios)
```

- **Bios**: fake BIOS POST screen. Pulls *real* hardware info via
  `System.Management`/WMI (CPU, RAM, GPU, disks). Lines reveal on an
  explicit millisecond timeline (`_timedLines`), not a flat "one line per
  N ms" - this matters because the RAM line isn't static text, it's an
  animated count-up to the real total with tick beeps. Holds a minimum of
  10s but won't cut off mid-reveal if there are a lot of disks
  (`Math.Max(BiosMinHoldMs, _footerAt + 300)`).
- **Bsod**: easter egg. Pressing Delete during Bios jumps here for 7s
  (joke text), then continues into Boot as if nothing happened. Doesn't
  resume Bios - it skips straight to Boot.
- **Boot**: original artwork only - gradient sky, drifting pixel clouds,
  a waving flag built from animated vertical strips (sine-wave flutter).
  **Do not source or embed real Windows 95/98 boot GIFs/logos here** - see
  Copyright below. Win95 startup sound fires at the 8s mark via a
  stopwatch check (not a fixed `Sleep`), so it survives frame-rate hiccups.
- **Desktop**: placeholder wallpaper (clearly labeled as a placeholder -
  the user will drop in a real image later), two icons with original glyph
  art (drive/folder shapes, not copied Windows icons), taskbar with a live
  clock. Holds 7s.
- **Terminal**: a command-prompt-style window that scales in from the
  center (ease-out cubic), then runs `InstallerEngine.RunAsync()` on a
  background task and streams its `OnLog` output into a scrolling,
  thread-safe log (`_terminalLines`, guarded by `_terminalLock`) drawn
  as the terminal's content.

## Copyright constraint - important

This project deliberately does not contain any real Microsoft assets:
no actual Windows 95/98 boot GIFs, no Microsoft logos/wordmarks, no BSOD
text copied from real Windows. Everything visual is original artwork
(the chip logo, the flag, the cloud shapes, the BSOD joke copy). If asked
to "find" or "source" real Win9x boot animations/images, don't - explain
why and offer an original alternative instead, same as previously agreed
with the user.

## Next steps

The installer stack is built (`InstallerEngine.cs`) - winget loop with
retries, manual-installer fallback chain, and gaming tweaks, all logging
into the terminal window. Still open:
- Real boot animation assets (the flag/clouds/sky are original placeholder
  artwork, described in the Boot stage above)
- Installation log viewer / summary screen at the end of a run
- MSI Afterburner has no reliable winget package and no verified stable
  direct-download URL was available when the manual-installer list was
  built - add it there if/when a good source is confirmed

## Dev workflow note - two manifests

`App.manifest` (`requireAdministrator`) is used for Release builds.
`App.Debug.manifest` (`asInvoker`) is used for Debug builds instead,
picked via a `Configuration` condition in the csproj. This exists because
`dotnet run`/F5 launch the app via `CreateProcess`, which cannot silently
elevate a child process - only `ShellExecute` (double-clicking an exe)
can - so requesting `requireAdministrator` on a debug launch throws "the
requested operation requires elevation" every time. Don't remove the
Debug manifest or point both configurations at the same manifest file;
that reintroduces this exact error for anyone doing `dotnet run`.

## Running it

Windows-only (WinForms + WMI). `dotnet restore && dotnet run` from this
folder. Press **Esc** anytime to close (it's borderless/maximized, no
title bar to click). Press **Delete** during the Bios screen to trigger
the Bsod easter egg. `dotnet publish -c Release -r win-x64` produces a
single self-contained .exe.
