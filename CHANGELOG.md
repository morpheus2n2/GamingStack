# Changelog

Nothing's been tagged as a release yet, so this is grouped by development
milestone rather than version numbers for now — worth switching to proper
[semantic versioning](https://semver.org/) once you start cutting releases.

## Current build — interactive install wizard + taskbar fix

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

## Previous build — C# GUI rewrite (all-in-one)

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

## Previous iteration — self-contained PowerShell script

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

## Original iteration — WinForms launcher + separate PowerShell script (deprecated)

The first attempt: a compiled C# launcher that extracted assets and handed
off to a separate `.ps1` installer script. **Abandoned** — worked in the
dev environment but failed completely in a live test, because the launcher
never actually extracted the PowerShell script or the boot images to where
it expected them. This is why later iterations avoid any split between a
GUI process and a separate script/asset files.
