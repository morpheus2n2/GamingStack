# GamingStack GUI

## What this is

A fully automated, visually over-the-top Windows 11 gaming setup app. Retro
Win95/98-styled boot sequence, then it installs a full gaming app stack and
applies gaming-related tweaks. The whole point is minimal manual steps plus
maximum nostalgia.

## Versioning

The project uses [Semantic Versioning](https://semver.org/), tracked in
`GamingStackGUI.csproj` (`<Version>`/`<AssemblyVersion>`/`<FileVersion>`)
and mirrored as a `## [x.y.z] - ...` heading at the top of `CHANGELOG.md`.
The project is at **1.6.1**. Rules for bumping (they were written under
`0.y.z` and still apply the same way):
- **Bump the minor version** (`0.6.x` → `0.7.0`) for a change that adds a
  feature - a new wizard behavior, a new catalog category, a new stage.
- **Bump the patch version** (`0.6.1` → `0.6.2`) for a fix, tweak, or pure
  visual polish with no new feature - the boot sky recolor, a warning fix,
  a spacing adjustment.
- Bump both the csproj and the changelog together, in the same change -
  don't let the version number drift from what's actually in the code.

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
- `BootScene.cs` (boot artwork + gradient loading bar), `BiosLogo.cs` (BIOS emblem), `BootTheme.cs` (time-of-day colour schemes), `DesktopIcons.cs`, `PixelCanvas.cs` (shared pixel helpers + 5x7 font) and `RetroAudio.cs` (sound mixer for the typewriter and BIOS beeps: winmm, synthesised, own thread) are separate files by design. `Resources/win95.wav` - embedded as is (`Resources/wallpaper.jpg` too)
  resources, located at runtime by name match
  (`GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(...))`)
  rather than a hardcoded resource path, specifically to avoid the exact
  "embedded resource path mismatch" bug that broke the first version.
- `InstallerEngine.cs` - the actual install stack (winget loop, manual
  installer fallback, bundle installs, tweaks), decoupled from the UI via
  an `OnLog` event so it doesn't know anything about WinForms. Exposes a
  static `Catalog` of `AppEntry` (friendly name + `Category` + winget ID /
  manual URL / bundle of winget IDs, discriminated by `AppKind`) - this is
  the single source of truth for what's installable; `RunAsync` takes the
  selected subset as a parameter rather than deciding for itself, since
  the wizard in `MainForm.cs` owns that decision.
  - `AppKind.Winget` / `AppKind.Manual` are unchanged from before.
  - `AppKind.Bundle` is a catalog entry backed by several winget IDs
    (`BundleWingetIds`) installed back-to-back under one friendly line -
    used for "VC++ Redistributables Pack (all versions)" so the wizard
    doesn't have to show a dozen near-identical VCRedist entries. Each ID
    still gets its own retry loop; per-ID failures are summarized under
    the bundle's name rather than each raising its own `Fail()` (an
    already-newer redist "failing" to install is normal noise, not a real
    problem).
  - `Category` is display-only - it doesn't affect install order or
    logic, only how `DrawWizard` in `MainForm.cs` groups the on-screen
    list under a heading. Add a new category by just giving an `AppEntry`
    a category string that doesn't exist yet.
- `BackupEngine.cs` - the restore point + optional full disk-image backup,
  decoupled from the UI the same way `InstallerEngine` is (its own `OnLog`
  event, its own log file). See "Restore point / backup" below for the
  detail - it's a separate concern from app installs, not a mode of
  `InstallerEngine`.

## Stage flow (`MainForm.cs`)

```
Bios -> Boot -> Desktop -> Terminal (ChooseProfile first); SPACE skips Bios/Boot/Desktop straight to Terminal
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
- **Boot**: original artwork only, drawn procedurally by `BootScene.cs` in a
  16/32-bit style (dithered sky, striped sun, parallax mountains/hills,
  shaded clouds, waving flag with pixel lettering) at low resolution and
  scaled up by a whole number so pixels stay square. No image files - the old
  sprite PNGs are gone.
- **Desktop**: the embedded `Resources/wallpaper.jpg` (the user's own choice; it shows some real brand marks, worth remembering if this is ever released publicly), scaled once at load and lightly sharpened. Three pixel-art setup-type icons (`DesktopIcons.cs`, original art, not copied Windows icons), a Start button with a chip logo, and a taskbar with a live
  clock. Holds 7s.
- **Terminal**: a command-prompt-style window that scales in from the
  center (ease-out cubic), then runs an interactive install wizard
  (`WizardPhase` enum in `MainForm.cs`) before ever touching
  `InstallerEngine`:
  - `ChooseProfile` is the first screen: [1] Gaming/Streaming, [2] Office,
    [3] Developer/Home lab (`SetupProfile`, `SelectProfile`). Each
    `AppEntry` has a `Profiles` flag (default Gaming only) and the wizard uses
    `InstallerEngine.CatalogFor(profile)` (`_catalog` in `MainForm`). Gaming-only
    tweaks are gated in `PreviewTweaks`/`ApplyTweaks` by `options.Profile`;
    Developer adds long paths + Developer Mode, both with preview and undo line.
    Any new tweak must still follow preview -> consent -> apply -> revert.
  - `ConfirmAll` shows the full `InstallerEngine.Catalog` (friendly
    names, grouped under a heading per `Category` - see
    `BuildWizardColumnsIfNeeded`/`DrawWizard`) and asks Y/N to install
    everything.
  - `N` -> `ChooseIndividually` asks whether to pick items one at a time;
    `Y` -> `PerApp` walks the catalog asking Y/N per entry, building
    `_selectedApps` and showing a live checklist (`_perAppDecisions`).
  - `N` to choosing individually -> `ConfirmQuit`; `Y` closes the app,
    `N` -> `EasterEgg` (an original wobbling floppy-disk mascot, 5s) then
    `Farewell`.
  - Once a selection is made (full catalog or a hand-picked subset, even if
    empty), both paths route through `GoToConfirmBackup()` rather than
    straight to `StartInstall()` - see Restore point/backup below for that
    step, which always ends by calling `GoToConfirmTweaks()`. That calls
    the static, read-only `InstallerEngine.PreviewTweaks()` (real current
    values, nothing changed) and shows `ConfirmTweaks`, a dedicated "Apply
    these tweaks?" screen (`DrawConfirmTweaks`). Only that screen's own Y/N
    answer (`_applyTweaks`) decides whether tweaks run - see Gaming tweaks
    disclosure below.
  - `StartInstall()` then runs `InstallerEngine.RunAsync(_selectedApps, _applyTweaks)`
    on a background task and streams its `OnLog` output into a scrolling,
    thread-safe log (`_terminalLines`, guarded by `_terminalLock`).
    `InstallerEngine` also fires a structured `OnItemResult` event per
    selected entry (`Installed`/`Failed`/`Skipped`), collected into
    `_itemResults` (guarded by `_itemResultLock`) - this, not the
    free-text log, is what the summary screen renders from.
    `OnFinished` transitions to `Summary` - or, if `_backupTiming ==
    BackupTiming.After`, to `RunningBackup` first (`RunAfterBackupAsync`
    runs the image backup to completion, then moves on to `Summary`) -
    never straight to `Farewell` either way.
  - `Summary` (`DrawSummary`) reports what actually happened: totals plus
    a per-item breakdown grouped under the same category headings the
    wizard used (reuses `_wizardColumn0`/`_wizardColumn1`). Any key moves
    on to `Farewell` - it's a report, not a Y/N question, so it's handled
    before the `yn` switch in `HandleWizardKey`, not inside it.
  - `Farewell` is a styled ASCII-bordered sign-off screen reached from
    either path (a real install's `Summary`, or the easter egg timing out) -
    it's the same message either way, per the user's explicit request.
  - Y/N keys only do anything while `_stage == Stage.Terminal`, routed
    through `HandleWizardKey` from `OnKeyDown`.

## The desktop shell (1.3.0) - read before touching windows/icons

After the boot screen the app is a small retro "OS", still all on the one canvas (no real
child forms - that rule stands). `MainForm` is now a `partial` class split by concern:
`MainForm.cs` (stages, BIOS, wizard, installer drawing), `MainForm.Shell.cs` (window
manager: z-order, title bar + minimise/maximise/close, drag/resize, taskbar buttons, Start
menu, desktop icons, input routing) and `MainForm.Apps.cs` (the installer window, My
Computer, Trophy Room, Arcade/Snake, Recycle Bin). `RetroWindow` is just data + callbacks;
the shell draws the chrome and calls `DrawClient`/`OnKey`/`OnTick`.
- The installer is the window with Id `installer`; `Stage.Terminal` still means "installer
  has been launched" and the wizard logic is unchanged. `EnterTerminalStage()` is the one
  way in. Icons/Start menu call it; the desktop auto-opens it after a few seconds unless
  another window is open.
- To add an app: add an icon in `DesktopIcons.cs`, an entry in `IconItems()` (and
  `StartItems()` if wanted) and an `OpenXxx()` that calls `OpenWindow(new RetroWindow {...})`.
- `ProgressStore` is the only thing written between runs (`%APPDATA%\GamingStack\progress.json`).
  It must stay app-data only - never put system settings in it.

- **UI scale rule:** all shell/app code works in *virtual* pixels (`VW`/`VH`, never
  `Width`/`Height`) because the shell is drawn through `g.ScaleTransform(UiScale)`; mouse points
  go through `V()`. The wallpaper, boot scene and scanlines are drawn at real pixel size.
- `MainForm.Tools.cs` holds the screensaver, Display Properties, Arcade picker, Notes, Paint,
  Defrag and Command Prompt; `MiniGames.cs` holds Minesweeper/Pong/Breakout (Snake is its own
  file). Settings and scores live in `ProgressStore` (app data only).

- **Fonts rule:** never `new Font("Consolas", ...)` - use `Ui.Font(points, style)` (pixel-sized), or text and
  layout drift apart on scaled displays. UI scale defaults to 1x (`UiScale`); choices live in Display Properties.
- Desktop icons sit on a slot grid (`Slots()`), draggable, saved via `ProgressStore.IconSlots`.

## Logging, consent and downloads (1.6.0) - read before touching the install flow

- Flow in the wizard is now `... -> ConfirmTweaks -> FinalReview -> Installing`. **FinalReview**
  (`DrawFinalReview` in `MainForm.Wizard.cs`) must stay: it needs `GO` + Enter typed, it lists every
  consequence in plain words, and `N` backs out with nothing changed. Don't turn it into a single
  keypress and don't add a way to reach `StartInstall()` that skips it.
- `InstallerEngine.RunAsync` writes a timestamped log to `Documents\GamingStack\logs` (header with
  version/OS/admin/profile/backup/tweak options/every item, then each step with result and
  duration, then a summary). New install steps must call `Log(...)`; failures go through `Fail(...)`.
  Manual downloads are fingerprinted (size, SHA-256, signer) before they are run.
- Undo scripts are per run in `Documents\GamingStack\undo\` (never overwritten - a second run would
  otherwise record already-changed values). Don't move them back to Temp.
- `RequestCancel()` (key `C` while installing) stops after the current item and skips tweaks and
  removals that haven't started.
- `AppKind.Link` entries just open an https `PageUrl` in the default browser (no download, no
  bundled browser); `Manual` entries fall back to their `PageUrl` if the automatic install fails.
  Results use `InstallResultKind.Opened`.
- `MainForm` is split by topic: `MainForm.cs` (state, ctor, stage loop, keys), `.Intro.cs` (BIOS,
  boot, wallpaper), `.Wizard.cs` (flow + every wizard screen), `.Finale.cs` (summary and ending),
  `.Achievements.cs`, `.Shell.cs`, `.Apps.cs`, `.Tools.cs`.
- `tools/gen-catalog.py` regenerates `docs/CATALOG.md` from the catalog - run it when apps change.

## Copyright constraint - important

This project deliberately does not contain any real Microsoft assets:
no actual Windows 95/98 boot GIFs, no Microsoft logos/wordmarks, no BSOD
text copied from real Windows. Everything visual is original artwork
(the chip logo, the flag, the cloud shapes, the BSOD joke copy). If asked
to "find" or "source" real Win9x boot animations/images, don't - explain
why and offer an original alternative instead, same as previously agreed
with the user.

## Gaming tweaks constraint - important

Same weight as the copyright rule above: **no system-level tweak (registry,
power plan, or anything else) may run without an explicit on-screen Y/N
consent screen showing its real current value first, and every tweak must
be reversible.** This wasn't followed when Game Mode/HAGS/the power plan
were first added - they ran automatically at the end of every install with
no disclosure and no way back - and got fixed properly in 0.14.0:
`InstallerEngine.PreviewTweaks()` (static, read-only) feeds a
`ConfirmTweaks` wizard screen (`DrawConfirmTweaks` in `MainForm.cs`) shown
before `StartInstall()`, and `ApplyTweaks()` writes a `revert-tweaks.cmd`
capturing each setting's real previous value *before* changing anything,
regenerated every run. `RunAsync`'s `applyTweaks` parameter is the only
thing that gates `ApplyTweaks()` - never call it unconditionally again, and
never add a new tweak that skips this pattern (preview -> consent screen ->
apply -> revert script), whatever the reason.

## Restore point / backup (`BackupEngine.cs`) - important

A System Restore point is the one exception to "always ask first" above -
the user was explicit that it "should always always happen", so
`GoToConfirmBackup()` runs it unconditionally via
`BackupEngine.CreateRestorePoint()` before any screen asks about anything
else. It tries the direct WMI call (`root\default:SystemRestore`,
`CreateRestorePoint`) first, then falls back to enabling System Restore
for the system drive (`powershell -Command Enable-ComputerRestore`) and
retrying once if the first attempt failed - don't remove that retry, it
exists because the user has personally had restore points silently fail
before, more than once.

The optional full disk-image backup (`wbadmin start backup`) *does* follow
the disclosed/reversible pattern: `ConfirmBackup` asks before/after/skip,
`SelectBackupDrive` shows `BackupEngine.FindEligibleDrives()` (a pure read)
with each drive's free space next to `EstimatedNeededBytes()` rather than
silently filtering options out, and the backup itself blocks
(`PreparingBackup`/`RunningBackup`, both drawn via the shared
`DrawBackupProgress`) rather than racing in the background - a backup that
runs concurrently with installs would capture a half-changed system, not
the clean before/after snapshot the whole feature is for. Both the restore
point's and the backup's real outcome are reported on `Summary`
(`_backup.RestorePointCreated`/`RestorePointError`,
`ImageBackupSucceeded`/`ImageBackupError`/`ImageBackupDestination`) - never
assume either one succeeded just because it was attempted.

## Next steps

The original build order (installer stack, wizard, summary, shutdown
outro, achievement toasts, Konami code, real boot animation art) is
complete as of 0.13.2, and the project reached 1.0.0 (optional DNS switch,
opt-in debloat, `BuildSheet.cs` plain-text build sheet saved to
`Documents\GamingStack`, Credits screen after Summary) - see `CHANGELOG.md`
for the full history. 1.0.0 compiles, but the debloat uninstall, the Store
reinstall IDs in the undo script, the DNS switch, and the Credits/Build
Sheet screens have never been run on a real PC. The DNS switch and debloat
follow the same disclosed + reversible rule as every other tweak.

Everything under consideration beyond that lives in `ROADMAP.md`, grouped
by kind (system tweaks, apps, automation, fun/easter eggs). The one rule
that governs all of it, stated explicitly by the user and non-negotiable:
**any tweak that touches a system setting must be disclosed on screen
before/as it happens, and must be reversible** - either by the app
recording and restoring the previous value, or because it's just a
standard Windows setting the user could flip back themselves. An idea that
can't satisfy both isn't in scope, no matter how useful.

Ongoing maintenance notes:
- MSI Afterburner has no reliable winget package and no verified stable
  direct-download URL was available when the manual-installer list was
  built - add it there if/when a good source is confirmed (it's also
  listed in `ROADMAP.md`'s app list).
- Every winget ID in the catalog was checked directly against the
  winget-pkgs repo as of 0.7.1 (this is what caught Razer Cortex never
  having had a real package ID, and two other IDs that had drifted -
  see the 0.7.1 changelog entry). Vendor-published apps (RGB/peripheral
  control especially) still change IDs more than most, so if one starts
  failing every time, re-check it with `winget search <name>` first -
  the summary screen's per-item failure detail (exit code or error
  message) is the fastest way to tell "wrong ID" apart from "installer
  quirk on this machine".

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

## Fullscreen / taskbar note

The form is positioned explicitly via `Screen.FromPoint(Cursor.Position)`
and `Bounds = screen.Bounds` (not `WindowState.Maximized`, which doesn't
reliably cover the full physical screen on every DPI/multi-monitor setup)
and marked `TopMost = true`. The `TopMost` part matters even with correct
bounds: the real Windows taskbar is itself an always-on-top window, and
without this our own drawn taskbar could render *behind* it. Don't drop
either half of this fix independently - both were needed to actually
resolve the "taskbar renders off/behind screen" bug reported during
testing.

## Running it

Windows-only (WinForms + WMI). `dotnet restore && dotnet run` from this
folder. Press **Esc** anytime to close (it's borderless/maximized, no
title bar to click). Press **Delete** during the Bios screen to trigger
the Bsod easter egg. `dotnet publish -c Release -r win-x64` produces a
single self-contained .exe.
