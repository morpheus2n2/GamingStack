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
