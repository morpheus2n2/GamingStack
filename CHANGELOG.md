# Changelog

Versioned using [Semantic Versioning](https://semver.org/) (`MAJOR.MINOR.PATCH`).
While the project sits at `0.y.z` (nothing's been publicly released yet),
minor bumps mean a new feature, patch bumps mean a fix/tweak with no new
feature - see "Versioning" in `CLAUDE.md` for the exact rule. The version
lives in `GamingStackGUI.csproj` (`<Version>`) and should be bumped in the
same change that updates this file.

Everything below `0.6.2` predates versioning entirely, so those entries are
backfilled from the original milestone-based changelog and given version
numbers retroactively - there's no real date for them, only relative order.

## [0.19.0] - Active hours, Game DVR, Nagle's algorithm, and read-only TRIM/health reporting

### Added
- Three more items on the `ConfirmTweaks` screen, same disclosed/reversible
  pattern as everything else there:
  - **Windows Update active hours** - `ActiveHoursStart`/`ActiveHoursEnd`
    (`HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings`) set to a
    16:00-23:00 window, rather than disabling updates.
  - **Xbox Game Bar / Game DVR** - `GameDVR_Enabled`
    (`HKCU\System\GameConfigStore`) disabled, for capture software that
    conflicts with it.
  - **Network latency (Nagle's algorithm)** - `TcpAckFrequency` and
    `TCPNoDelay` set on whichever network interface is actually active
    right now (found via WMI - `Tcpip\Parameters\Interfaces\{GUID}` is
    per-adapter, not global). Falls back to a clear "no active network
    adapter found" preview line rather than guessing if none can be
    identified.
- **Read-only storage health + TRIM reporting** on the fake BIOS screen,
  next to the existing disk list - no consent screen, since nothing
  changes:
  - Each disk's line now shows its real WMI `Status` (normally "OK") in
    place of the previously-hardcoded "OK".
  - A new "TRIM (delete notify): enabled/disabled" line, read via
    `fsutil behavior query DisableDeleteNotify` (the standard, documented
    way to check this - there's no WMI class for it).

### Notes
- The DNS-switch half of the original "Network/QoS tweak" roadmap idea
  is deliberately not included here - it needs its own provider-choice
  screen and carries more risk than a registry flag (VPNs, parental
  controls, ISP-specific services can all depend on the existing DNS).
  Left on `ROADMAP.md` as its own future idea rather than bundled in.
- The debloat pass is intentionally not part of this release either - a
  different shape of feature (removing pre-installed apps, not changing a
  setting), worth designing on its own.

## [0.18.0] - MMCSS, Start menu ads, Fast Startup, and an Ultimate Performance choice

### Added
- Three more items on the `ConfirmTweaks` screen, same disclosed/reversible
  pattern as everything else there:
  - **Background task CPU reservation (MMCSS)** - `SystemResponsiveness`
    (`HKLM\...\Multimedia\SystemProfile`) from its 20% default down to 0%,
    so background/low-priority tasks stop reserving CPU away from the
    foreground game. This is Microsoft's own documented MMCSS tuning
    value, not an undocumented hack.
  - **Start menu suggestions/ads** - `SystemPaneSuggestionsEnabled` and
    `SubscribedContent-338388Enabled` (both `HKCU\...\ContentDeliveryManager`)
    off, turning off the suggested-apps/tips content Windows injects into
    Start.
  - **Fast Startup** - `HiberbootEnabled`
    (`HKLM\SYSTEM\...\Session Manager\Power`) off - the same effect as
    unchecking "Turn on fast startup" in Control Panel's Power Options.
- A new **"ChoosePowerPlan"** screen, shown once right before `ConfirmTweaks`:
  `[1] High performance` or `[2] Ultimate Performance`. Ultimate Performance
  is a real, Microsoft-documented hidden power plan
  (template GUID `e9a42b02-d5df-448d-aa00-03f14749eb61`) that isn't
  activatable directly on modern Windows - it has to be duplicated into a
  real, visible plan first via `powercfg -duplicatescheme`, which
  `InstallerEngine` now does (reusing an existing duplicate on a repeat run
  rather than creating a new one every time). The "Power plan" tweak row on
  `ConfirmTweaks` now reflects whichever one was chosen.

### Notes
- The Start menu tweak and MMCSS/Fast Startup changes all apply as
  documented registry values with existing precedent in this project
  (`HKCU` for per-user settings, `HKLM` for machine-wide ones) - same
  revert-script coverage as every tweak before them.

## [0.17.0] - Explorer + taskbar tweaks

### Added
- Six new items on the `ConfirmTweaks` screen, same disclosed/reversible
  pattern as the existing Game Mode/HAGS/power plan tweaks - real current
  value shown, nothing changes without saying yes, everything captured in
  `revert-tweaks.cmd`:
  - **File extensions** - shown instead of hidden in Explorer.
  - **Hidden files** - shown instead of hidden.
  - **Right-click context menu** - restored to the classic (pre-Windows 11)
    full menu instead of needing "Show more options", via the documented
    `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` CLSID override. Reverting
    deletes that key outright rather than restoring a previous value,
    since the key either exists or it doesn't.
  - **Taskbar widgets button** - hidden.
  - **Taskbar search box** - hidden.
  - **Taskbar Copilot button** - hidden.
- All six are per-user (`HKCU`) settings, unlike the existing three tweaks
  which are machine-wide (`HKLM`) - `Registry.CurrentUser` resolves
  correctly even though the app runs elevated, since HKCU is tied to the
  signed-in user's SID, not to the process's elevation token.

### Notes
- The context-menu and taskbar changes take effect after Explorer restarts
  (usually the next sign-in) rather than instantly - noted on the log line
  for the context-menu tweak so it isn't mistaken for a failure if the
  right-click menu doesn't change immediately.

## [0.16.0] - Microsoft PC Manager added to the catalog

### Added
- **Microsoft PC Manager** (`Microsoft.PCManager` on winget) to the
  Monitoring & Performance category - Microsoft's own first-party
  CPU/RAM/GPU monitoring, storage cleanup and startup-app manager.
  Confirmed installable via winget (still labelled Beta upstream, but the
  package itself installs cleanly). Opt-in and skippable like every other
  catalog entry.

### Notes
- Investigated three other apps the user asked about while testing
  0.15.0: TMOG (tmog.org), ASUS Armoury Crate, and "PC Smart Utility".
  None added to the catalog this round - see `ROADMAP.md`'s "Apps to add
  to the catalog" section for why each was held back (no confirmed
  unattended install path for TMOG, a known winget-manifest fragility for
  Armoury Crate, and an unconfirmed/unclear identity for "PC Smart
  Utility").

## [0.15.0] - Restore point + optional full disk-image backup

### Added
- **A System Restore point is now always created** once app selection is
  finalized, before anything installs or changes - no consent screen for
  this one, it's a mandatory safety net (see `ROADMAP.md`'s "always always
  happen" rule). Tries the direct route first; if System Restore is off for
  the drive (the Windows default on most consumer machines), enables it and
  retries once rather than failing silently on the first attempt.
- **A new `BackupEngine` class** handling the restore point and an optional
  full disk-image backup via `wbadmin` (the engine behind the old "Backup
  and Restore (Windows 7)" control panel item - still the only scriptable
  full-image option on Windows 11). Decoupled from `InstallerEngine` the
  same way that's decoupled from `MainForm` - its own class, its own log
  file (`backup_<timestamp>.log`).
- **New wizard screen: "Full disk-image backup?"** — shown right after app
  selection is finalized, before the tweaks question. Three choices: before
  any changes (a "virgin machine" backup), after everything's installed (a
  "gaming ready" backup), or skip it. Choosing before/after leads to a
  drive-picker screen that auto-detects eligible drives (non-system, NTFS)
  and shows each one's free space next to the rough space the backup needs,
  so nothing is a guess.
- The backup step **blocks** until it finishes (with a plain "this can take
  a while, don't turn off your PC" screen and an elapsed timer) rather than
  running in the background - a backup racing against installs/tweaks would
  capture a half-changed system instead of a clean before/after snapshot.
- The summary screen now reports the restore point's real outcome and,
  where relevant, the image backup's outcome, destination, and log path.

## [0.14.0] - Disclosed, reversible gaming tweaks

### Fixed
- **The gaming tweaks (Game Mode, hardware-accelerated GPU scheduling, the
  power plan) were being applied automatically at the end of every install
  run, with no on-screen disclosure and no way back.** This broke the
  disclosed/reversible rule written down in `ROADMAP.md` last night, and
  had been true since these tweaks were first added. Fixed properly:

### Added
- **New wizard step: "Apply these tweaks?"** — shown once app selection is
  finalized, before anything installs. Lists each tweak by name with its
  *real current value* (read live via the registry/`powercfg`, not assumed)
  and what it would change to. `N` skips every tweak entirely; tweaks now
  only ever run after an explicit `Y`.
- **A `revert-tweaks.cmd` script, written before any tweak is applied** —
  captures the exact previous value of each setting (or the right command
  to remove it entirely, if it wasn't set before) and restores them with
  one script run as Administrator. Regenerated every run, and its path is
  shown both on the consent screen and the summary screen afterward.
- The summary screen now reports what actually happened to the tweaks
  question — applied (with the revert script's path) or skipped — instead
  of assuming they always ran.

### Changed
- `InstallerEngine.RunAsync` now takes an explicit `applyTweaks` flag from
  the wizard instead of deciding for itself; `ApplyTweaks()` is only ever
  called when that's `true`.

## [0.13.3] - Roadmap + repo docs refresh

### Added
- **`ROADMAP.md`** — the full backlog of ideas beyond the original build
  order (system tweaks, apps, automation, fun/easter eggs), grouped by
  kind, plus the standing rule that governs all of it: any system-level
  tweak must be disclosed on screen and must be reversible, no exceptions.

### Changed
- README: broadened the intro to reflect this as an ongoing project rather
  than a finished checklist, linked `ROADMAP.md` from both the intro and
  the (now-complete) in-README roadmap checklist, and called out the
  disclosed/reversible rule in the Safety note.
- `CLAUDE.md`: replaced the stale "Next steps" list (most of it had already
  shipped) with a pointer to `ROADMAP.md` and the same disclosed/reversible
  rule, so future work here starts from the current backlog instead of an
  outdated one.

## [0.13.2] - Konami clue grouping fix

### Fixed
- The disguised Konami-code hint line was tacked directly onto the Storage
  Devices list with no heading of its own, so it read as just another disk
  entry. It now gets its own "Input Devices:" heading first, matching the
  Graphics Adapter/Storage Devices pattern used everywhere else on the BIOS
  screen.

## [0.13.1] - Motherboard on the BIOS screen

### Added
- The fake BIOS POST screen now also reads and displays the real motherboard
  (manufacturer + model, via `Win32_BaseBoard`), between the BIOS banner and
  the CPU line - same real-hardware-reading approach as CPU/RAM/GPU/disks.

## [0.13.0] - Pixel-art boot animation assets

### Added
- **Real hand-pixelled boot animation art**, replacing the procedural GDI+
  shapes: an original sun, a rolling-hills silhouette, blocky clouds, and a
  waving flag that now animates off a 16-frame pre-baked sprite sheet
  instead of a live per-pixel sine wave - all drawn with NearestNeighbor
  scaling so they stay crisp and chunky when scaled up fullscreen, the same
  way a real retro sprite animation would look.
- New embedded resources: `cloud.png`, `sun.png`, `hills.png`,
  `flag_sheet.png` - all original artwork made for this project (see
  Credits & copyright in the README), tiny hand-authored low-res PNGs, not
  photos or scanned assets.
- Every sprite draw falls back to the old procedural shapes if its resource
  fails to load for some reason, so the boot animation never breaks outright
  over a missing asset.

### Changed
- Flag narrowed and moved higher up the scene (was sitting low and a bit
  too wide for the frame); the hills silhouette dropped further down toward
  the horizon and made taller, so the layers read as proper background/
  foreground depth instead of everything clustering in the middle.

## [0.12.0] - Konami code easter egg

### Added
- **Konami code easter egg** - enter Up, Up, Down, Down, Left, Right, Left,
  Right, B, A at any point in the app (BIOS screen, boot animation, desktop,
  or the terminal wizard - it's checked on every keypress regardless of
  stage) to trigger a "Cheat Code Accepted" toast. It doesn't do anything
  else - it's a throwaway joke, not a functional unlock.
- The only hint is a disguised line on the fake BIOS POST screen, tucked in
  right after the disk listing as an ordinary-looking peripheral-detection
  entry: `Input Device: Standard 104-Key Keyboard (↑↑↓↓←→←→BA) - OK`.
- Routes through a new `UnlockSecretAchievement`, kept deliberately separate
  from the catalog-bound `UnlockAchievement`/`AllAchievements` so this one
  never appears in, or inflates the count on, the summary screen's
  achievement tracker - it stays a genuine hidden surprise.

## [0.11.2] - Achievement tracker spacing

### Changed
- **More breathing room in the summary screen's achievement tracker** -
  a spacer row now separates each achievement's title+description block
  from the next (they were running straight into each other), and the gap
  between the per-item breakdown above and the achievements heading below
  it is a bit more generous.

## [0.11.1] - Bigger toast, achievement descriptions on the tracker

### Changed
- **Achievement toast roughly doubled in size** - box, fonts, padding and
  the trophy icon are all about 2x the 0.11.0 size, since the first pass
  read as too small to comfortably read. Box width is capped against the
  window's own width so it still fits a small/resized terminal.
- **Summary screen's achievement tracker now shows each one's description**,
  not just its title - wrapped to fit its column, with each column tracking
  its own running row count so a longer description doesn't overlap the
  entry below it (same fix already applied to the per-item breakdown above
  it).

## [0.11.0] - Achievement toast layout fix, summary tracker

### Fixed
- **Achievement toast text was overlapping/getting cut off** - the box was
  a fixed 260x64, which was never enough room for the header line, title,
  and a wrapped subtitle together (worst on longer titles like "Not
  Everything's Meant To Be"). The box is now sized from its actual content
  every time: both the title and subtitle wrap to fit, and the box grows
  to whatever height that content needs instead of clipping it.

### Added
- **Achievement tracker on the Summary screen** - a two-column checklist
  of every achievement that exists (`[x]`/`[ ]`, count in the heading),
  tracked across the whole session rather than reset per run, so repeated
  test runs build up a visible record instead of each one only showing
  what it earned. Achievement titles/subtitles are now defined once in a
  shared `AllAchievements` list instead of being repeated at each unlock
  call site, which the tracker and the toasts both read from.

## [0.10.0] - Achievement toast notifications

### Added
- **Achievement toasts** - small bottom-right pop-ups (original pixel-art
  trophy icon, slide up / hold / fade out) that fire the moment a real
  condition is met during a run, not on a script:
  - **First Blood** - first successful install of the run
  - **Completionist** - chose to install the entire catalog
  - **Redistributable Rampage** - the VC++ Redistributables Pack finishes
    without a genuine failure
  - **Already Perfect** - hit an "already installed and up to date" skip
  - **Not Everything's Meant To Be** - first genuine failure of the run
  - **Speedrunner** - the whole install finishes in under 90 seconds
  Each fires once per run. Toasts overlay whatever screen is currently
  showing (wizard, installer log, summary) and queue one at a time rather
  than stacking, so an achievement never has to wait for a specific
  screen to unlock. This is also the plumbing the upcoming Konami code
  easter egg will hook into.

## [0.9.0] - Fake shutdown outro

### Added
- **New `ShuttingDown` wizard phase**, between Summary and Farewell: a
  Windows 9x-style shutdown sequence played completely straight - a few
  staged status lines ("Saving your settings...", "Closing GamingStack
  Installer...", "GamingStack has finished configuring your PC.",
  "Shutting down...") typed out over ~3 seconds, then the classic "It's
  now safe to turn off your computer." screen (navy background, big
  centered white text) framed inside the app's own fake terminal window -
  a screen within the screen.
- This phase is a beat, not a question: it plays itself out and
  auto-advances to Farewell on its own after a short hold, no keypress
  needed. Dismissing the Summary screen now leads here instead of
  straight to Farewell.

## [0.8.3] - Proper typewriter "clack", not a click

### Changed
- **Typewriter tick sound reworked for real Resident Evil save-point
  character.** The 0.8.2 version was a single decaying tone-plus-noise
  click, which read as a generic beep rather than a typewriter. It's now
  three layered components per hit - a brief broadband strike (the key
  hitting the platen), a short high metallic tick (the type-bar), and a
  low-mid mechanical body resonance that carries the actual "clack" pitch
  and rings out longest - mixed together, which is what makes a real
  typewriter sound mechanical rather than electronic.
- Four pre-rendered variants (slightly jittered pitch/timing) are now
  cycled round-robin through their own `SoundPlayer` instances, instead of
  retriggering one shared player - fast typing was cutting a click's tail
  off to start the next one identically; now consecutive hits can overlap
  and ring naturally, like an actual typewriter being typed on quickly.

## [0.8.2] - Summary screen word-wrap, typewriter tick sound

### Fixed
- **Summary screen text no longer overlaps.** Failure/skip reason text was
  drawn as a single unbroken line with no width limit, so anything longer
  than the column (which happens constantly - these are free-text winget
  error explanations) ran straight across into the other column's text.
  Detail text now word-wraps to fit its own column, and the two-column
  balancing pass accounts for how many wrapped lines each item's reason
  actually takes, so the layout stays readable no matter how long the
  install run's messages are or how many items were attempted.

### Added
- **Typewriter tick sound** during the wizard's app-list reveal - a short
  synthesized mechanical "clack" (no new audio asset needed) plays as the
  text types itself out, replacing the silence that was there before.

## [0.8.1] - Friendly failure reasons for known winget exit codes

### Changed
- **Known winget exit codes now show a plain-English reason** on the summary
  screen instead of a bare `exit code -1978335189`. The three we kept
  hitting during testing are recognized directly:
  `APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE` ("already installed and
  up to date"), `APPINSTALLER_CLI_ERROR_EXEC_UNINSTALL_COMMAND_FAILED`
  ("a broken existing install is blocking this"), and a WinHTTP 404 HRESULT
  ("winget's download link for this is currently broken upstream").
- **"Already up to date" is no longer reported as a failure.** An app or
  VC++ Redistributable member that winget refuses to reinstall because an
  equal-or-newer version is already present now shows as skipped, not
  failed, and stops retrying immediately instead of burning three attempts
  on a result that was never going to change. The VC++ Redistributables
  Pack entry only reports itself as failed when a package genuinely fails
  for a different reason - already-current members no longer drag the
  whole bundle's status down.

## [0.8.0] - Persistent install log

### Added
- **Every install run now writes a full, timestamped log file to disk**
  (`%TEMP%\GamingStack\logs\install_<yyyyMMdd_HHmmss>.log`) - every line
  that used to only ever appear scrolling past in the terminal window
  (attempt numbers, exit codes, error messages) is now captured verbatim
  as it happens, not just the handful of things that hit `failed.txt`.
  One file per run, so a previous run's log is never overwritten.
- The summary screen now shows the full log's path directly, so there's
  no need to go hunting in `%TEMP%` to find it after a run.

## [0.7.2] - Razer Cortex restored as a manual download, Synapse bumped to 4

### Changed
- **Razer Cortex is back** - not on winget (confirmed in 0.7.1), but Razer
  does distribute it as a direct download from their own site's own
  "DOWNLOAD NOW" link. Re-added as a `Manual` catalog entry (same mechanism
  as Hyte Nexus/L-Connect 3) pointed at Razer's stable short link
  (`rzr.to/cortex-download`, which currently 302s to
  `dl.razerzone.com/drivers/GameBooster/RazerCortexInstaller.exe`) rather
  than the resolved CDN URL, so it keeps working if Razer moves the file
- **Razer Synapse** now targets Synapse **4** (`RazerInc.RazerInstaller.Synapse4`)
  instead of 3 - that's the current version per Razer's own site

## [0.7.1] - Wrong winget IDs, summary layout/diagnostics fix

### Fixed
- **Razer Cortex removed from the catalog** - verified there's no winget package
  for it at all; `RazerInc.RazerCortex` was never a real package ID, which is
  why it failed on every run. Razer only ships Cortex bundled inside their
  interactive installer, and no stable direct-download URL exists either, so
  it's out until that changes.
- **Razer Synapse**: `Razer.Synapse.3` → `RazerInc.RazerInstaller.Synapse3`
  (the old ID doesn't exist - Razer's real winget package is namespaced under
  their installer, not a standalone `Razer.*`)
- **OpenRGB**: `CalcProgrammer1.OpenRGB` → `OpenRGB.OpenRGB` (the winget-pkgs
  maintainers renamed this to the application-specific ID and switched its
  installer to an MSI)
- Every remaining winget ID in the catalog checked directly against the
  winget-pkgs repo - the rest were already correct (Corsair iCUE, HWiNFO,
  Speccy, and the launchers/utilities were all fine; if those still fail,
  it's a per-machine issue, not a wrong ID, and the summary screen now shows
  why - see below)
- **Summary screen layout**: was reusing the wizard's full-catalog two-column
  split, which left one column mostly empty and the other overflowing
  whenever only a handful of items were actually selected (the "misaligned"
  look from 0.7.0). Now bin-packs a fresh, balanced pair of columns from only
  what was actually attempted each run (`BuildSummaryColumns`), and matches
  the wizard's own spacing (blank line after each heading, not just before
  the next one)

### Added
- `InstallerEngine` now captures *why* something failed - the winget exit
  code, or the exception/download/extract error for manual installs - as
  `ItemResult.Detail`, shown indented under the failed/skipped item on the
  summary screen. No more digging through the scrolled-past install log or
  `failed.txt` to find out what actually went wrong.

## [0.7.0] - Install summary screen

### Added
- **End-of-run summary**, shown after the real install finishes and before the
  Farewell screen: totals ("X installed, Y failed, Z skipped") plus a full
  per-item breakdown, grouped under the same category headings the wizard
  used to offer them - `[x]` installed, `[!]` failed, `[-]` skipped. Press
  any key to move on to Farewell. Skipped entirely when nothing was even
  attempted (e.g. the whole run was declined).
- `InstallerEngine` now fires a structured `OnItemResult` event per selected
  catalog entry (`Installed`/`Failed`/`Skipped`) alongside its existing
  free-text `OnLog`, so the summary screen doesn't need to parse log lines
  to know what happened - `InstallWingetAppAsync`/`InstallBundleAsync`/
  `HandleManualInstallerAsync` all now report success/failure back to
  `RunAsync` instead of firing and forgetting.

## [0.6.3] - Slower wizard typewriter

### Changed
- Wizard app-list reveal speed cut way down (420 → 60 characters/second) -
  it now reads like a deliberate typewriter, not a fast terminal dump.
  Also doubles as a nod for anyone who clocks the save-point vibe.

## [0.6.2] - Nullable warning fix

### Fixed
- `CS8629` compiler warning (nullable value type may be null) on
  `_termH.Value` in `PaintTerminal` - the null-forgiving operator (`!`)
  was already used for the neighboring `_termX`/`_termY` reads on the
  lines right below it, just missed on this one

## [0.6.1] - Boot sky/flag/progress bar, taskbar clock fix, list spacing

### Changed
- **Boot sky** recolored from a dusky purple gradient to a clear daytime
  blue - reads more like an actual sky (and matches the desktop
  wallpaper's palette) for the clouds to move across
- **Flag wave** slowed down slightly (less frantic than before)
- **Boot progress bar** now spans the full width of the screen (like the
  real Win9x boot bar) and is drawn as discrete gradient blocks
  (cyan → magenta) instead of one flat, static-colored fill
- **Taskbar Start button** padding increased again for a bit more
  breathing room around the text
- **Taskbar clock** rewritten to anchor from the right edge and use a
  fixed per-line height instead of `MeasureString`'s (padded) height -
  the date was creeping past the right and bottom edges of the screen on
  some setups
- **Wizard list**: a category's items now get a blank line *before* the
  next category's heading too, not just after their own heading - so a
  category block is fully boxed in whitespace on both sides

## [0.6.0] - BIOS/boot/taskbar polish + movable terminal + typewriter list

### Added
- **Terminal window is now draggable and resizable**: drag the title bar to
  move it, drag the bottom-right corner grip to resize it (down to a
  480x280 minimum). Position/size persist for the rest of the run once
  set (`_termX/_termY/_termW/_termH` in `MainForm.cs`), instead of being
  recomputed from screen size every frame
- **Typewriter reveal** for the wizard's app list: it now "types" itself
  onto the screen (420 chars/sec, with a blinking cursor at the current
  typing position) the first time the terminal opens, then stays fully
  shown for the rest of the run - the Y/N prompt itself doesn't appear
  until the list has finished typing
- Date added under the clock on the taskbar (`dd/MM/yyyy`, stacked under
  the time like the real Windows taskbar clock)

### Changed
- **BIOS "Press DEL" prompt** moved out of the timed line-by-line reveal:
  it's now pinned to the bottom of the screen and visible from the very
  first frame instead of waiting for the rest of the POST text, and now
  reads "Press DEL to enter BIOS Setup... Go on, I dare you!"
- **Boot animation**: three extra, quicker clouds added (phase-shifted so
  they don't overlap the original three identically) for a busier sky
- **Taskbar Start button** now sizes itself around the actual text width
  and centers "Start" both ways, instead of a fixed 80px button with
  left-anchored text that was never actually centered in it
- **Terminal title bar height** is now measured from the title font's
  actual metrics (+8px padding) instead of a hardcoded 26px, so the
  header text can't end up taller than the bar holding it
- **Wizard list spacing**: the "GamingStack will install the following:"
  heading now sits one line lower under the title bar, and every category
  heading now has a blank line before its items instead of running
  straight into them

## [0.5.0] - Categorized catalog + VC++ redist bundle

### Added
- **Catalog entries now carry a `Category`**, and the wizard's app list
  renders a heading per category instead of one flat two-column list.
  Categories are bin-packed as whole blocks across the two columns
  (never split a category's items across columns) so both columns stay
  roughly the same height regardless of category size
  (`BuildWizardColumnsIfNeeded`/`DrawWizard` in `MainForm.cs`)
- **`AppKind.Bundle`**: a catalog entry backed by several winget IDs
  installed back-to-back under one friendly name. Used for the new
  "VC++ Redistributables Pack (all versions)" entry — installs every
  VC++ runtime from 2005 through 2015+ (x86 and x64) silently, since so
  much of the gaming/streaming stack quietly expects one of these to
  already be present. Per-ID failures (common when a newer version is
  already installed) are summarized under the bundle's name rather than
  each raising its own warning
- **Razer Cortex** added to Monitoring & Performance
- **RGB & Peripheral Control** category: Corsair iCUE and Razer Synapse
  (winget), plus OpenRGB — a single open-source app that talks to
  several vendors' RGB hardware at once — alongside the existing Hyte
  Nexus / L-Connect 3 manual installers

### Changed
- Removed OBS Studio from the catalog — Streamlabs Desktop (which is
  itself OBS-based) already covers that need, and having both listed
  separately was redundant

### Fixed
- Terminal window title ("GamingStack Installer") was drawn 8px further
  left than every line of content below it (the app list, prompts, and
  installer log all use a 16px left margin; the title used 8px) — the
  two-column app list itself was already pixel-aligned row-for-row, this
  was the actual source of the "doesn't quite line up" look. Title now
  uses the same 16px margin as the rest of the window content.

### Known caveats
- Three winget IDs are best-guess and not yet verified on real hardware:
  `RazerInc.RazerCortex`, `Corsair.iCUE.4`, `Razer.Synapse.3`. If any of
  these consistently fail in the install log, the fix is a `winget
  search` and a one-line ID correction in `InstallerEngine.cs`.

## [0.4.0] - Interactive install wizard + taskbar fix

### Added
- **Interactive install wizard** in the terminal stage, replacing the old
  "just runs everything automatically" behavior:
  - Shows the full catalog with friendly names (no raw winget IDs) and
    asks "Install everything shown above? [Y/N]"
  - Answering No offers "Would you like to choose what's installed?
    [Y/N]" — Yes walks through every item one at a time asking Y (install)
    or N (skip), with a live checklist showing decisions made so far
  - Answering No to choosing individually asks "Just want to quit? [Y/N]"
    — Yes closes the app; No leads to a small easter egg (see below)
  - Manual installers (Hyte Nexus, L-Connect 3) are now part of the same
    pickable catalog as the winget apps, so people without that specific
    hardware can skip them instead of always downloading them
  - `InstallerEngine.RunAsync` now takes the selected list as a parameter
    instead of always installing a hardcoded set; a catalog entry now
    carries a friendly display name alongside its winget ID / manual URL
    (`AppEntry`/`AppKind` in `InstallerEngine.cs`)
- **Easter egg**: declining to install anything *and* declining to quit
  brings up a wobbling original floppy-disk mascot with a joke speech
  bubble about indecision, shown for 5 seconds
- **Farewell screen**: a styled ASCII-bordered sign-off message, shown
  both after a real install run finishes and after the easter egg —
  "Thank you for using my Installer stack..."
- winget availability is now checked once per run rather than aborting
  the whole thing before manual installers even got a chance to run

### Fixed
- Taskbar (and the rest of the fake desktop) rendering partly off-screen:
  the form is now positioned using `Screen.FromPoint(Cursor.Position)`
  (more reliable than the window's undefined initial position on
  multi-monitor setups) and marked `TopMost`, since the real Windows
  taskbar is an always-on-top window that can otherwise render above our
  own drawn one even when our bounds are correct

## [0.3.0] - C# GUI rewrite (all-in-one)

Everything now lives in a single WinForms process. No PowerShell, no
external asset files.

### Added
- MIT `LICENSE` file, referenced from the README
- Fake BIOS POST screen that reads real CPU/RAM/GPU/disk info via WMI
- Animated memory count-up with tick beeps and a final confirmation beep
- Original chip-style logo (top-left, BIOS-style branding)
- BSOD easter egg — press Delete during the BIOS screen for a joke blue
  screen, then it carries on into the boot sequence
- Original retro boot animation: gradient sky, drifting pixel clouds, and
  an animated waving flag with a wordmark
- Win95 startup sound (embedded resource) synced to the boot animation
- Desktop stage: taskbar with live clock, desktop icons with original
  glyph art, embedded wallpaper image
- Terminal window stage that animates open, now running the real installer
  (see below) instead of a stub line
- Admin elevation via app manifest (`requireAdministrator`) instead of a
  relaunch trick
- **Real installer stack** (`InstallerEngine.cs`) ported from the earlier
  PowerShell version: winget install loop with retries, manual-installer
  fallback (download → try several silent-install flags → interactive
  fallback) for apps with no winget package, and gaming tweaks (Game Mode,
  hardware-accelerated GPU scheduling, High Performance power plan).
  Installer output streams into the terminal window as it runs
- Expanded the app list with OBS Studio, HWiNFO, Playnite, Voicemeeter
  Banana, VLC, 7-Zip, and CrystalDiskInfo
- Debug-only app manifest (`App.Debug.manifest`, `asInvoker`) so
  `dotnet run`/F5 debugging works without hitting "the requested operation
  requires elevation"; Release builds still request real admin rights
- README banner image and badges; documented what the installer installs
  and the Debug-vs-Release elevation behavior

### Fixed
- Build error (`CS0266`): the terminal cursor's x-position was declared
  `var` (inferring `int`) then had a `float` (`MeasureString(...).Width`)
  added to it via `+=`, which doesn't implicitly convert - it's `float
  cursorX` now
- Removed the `<windowsSettings>` DPI block from both app manifests
  (`WFAC010`) - high-DPI is already set via `Application.SetHighDpiMode`
  in `Program.cs`, and declaring it in both places is what triggered the
  warning
- Switched the csproj's SDK from `Microsoft.NET.Sdk.WindowsDesktop` to
  plain `Microsoft.NET.Sdk` (`NETSDK1137`) - the WindowsDesktop SDK is
  no longer needed on current .NET SDKs when `UseWindowsForms` is set
- BIOS logo enlarged (was too small)
- BSOD text now centers as a block on screen with each line left-justified
  within it, instead of overlapping at fixed coordinates
- BIOS screen hold time increased by 3s
- Boot animation flag enlarged and now centers itself both horizontally
  and vertically (was pinned top-center)
- Cloud drift speed increased ~1.8x
- Fixed the desktop taskbar rendering slightly off-screen by setting
  explicit window bounds from `Screen.Bounds` instead of relying on
  `WindowState.Maximized` on a borderless form, which was sizing
  inconsistently on some displays
- Corrected a wrong winget package ID (`CPUID.CPU-Z.ROG` → `CPUID.CPU-Z`)

### Not yet done
- Real boot animation/wallpaper assets beyond the current placeholders
- Installation log viewer / summary screen

## [0.2.0] - Self-contained PowerShell script

Single `.ps1` file with the win95 startup sound embedded as base64 and the
app list embedded directly in the script, so there were no companion files
to lose. Proved out the "everything in one file" approach before the move
to a full GUI.

### Included
- Fake BIOS POST (real hardware info) and ASCII boot animation, both timed
  against a stopwatch so they hold their duration reliably
- winget install loop with retries
- Manual installer fallback for apps with no winget package (download,
  try several silent-install flag variants, fall back to interactive)
- Gaming registry tweaks (Game Mode, HAGS, High Performance power plan)
- Logging to file, opened automatically at the end

## [0.1.0] - WinForms launcher + separate PowerShell script (abandoned)

The first attempt: a compiled C# launcher that extracted assets and handed
off to a separate `.ps1` installer script. **Abandoned** — worked in the
dev environment but failed completely in a live test, because the launcher
never actually extracted the PowerShell script or the boot images to where
it expected them. This is why later iterations avoid any split between a
GUI process and a separate script/asset files.
