# Roadmap

The original build order (boot sequence → installer → wizard → summary →
shutdown outro → achievement toasts → Konami code → real boot art) is done -
see [`CHANGELOG.md`](CHANGELOG.md) for the full history of what shipped and
when. This file is the backlog of everything considered *after* that: ideas
worth doing, not yet scheduled, roughly grouped by kind. Nothing here is
committed to a version number until it's actually picked up.

## Ground rule for anything that touches the system

This app already changes a couple of things on the machine (Game Mode,
hardware-accelerated GPU scheduling, the power plan). Every idea below that
does the same follows the same two rules, no exceptions:

1. **Disclosed** - the app shows what it's about to change (and ideally
   what the previous value was) before or as it happens, on screen, in
   plain English. Nothing changes silently in the background.
2. **Reversible** - there's always a way back to how it was, either because
   the app records the old value and can restore it, or because it's just
   flipping a documented, standard Windows setting the user could undo
   themselves in two clicks.

An idea that can't satisfy both isn't in scope for this project, however
useful it might be.

**This rule was written down after the fact - the existing Game Mode/HAGS/
power plan tweaks had already shipped without either half of it.** Fixed
in 0.14.0: a "Apply these tweaks?" consent screen (real current value ->
new value, shown before anything changes) and an auto-generated
`revert-tweaks.cmd` that restores everything to what it was. See
`CHANGELOG.md`'s 0.14.0 entry. Any tweak added from this list starts from
that same pattern - it's the template, not just the rule.

## System tweaks

- ~~Restore point before any tweaks run~~ - **done in 0.15.0**: a System
  Restore point is now always created once app selection is finalized,
  before anything installs or changes - no consent screen needed for this
  one, it's the mandatory baseline. Paired with an optional full
  disk-image backup (before changes or after setup, auto-detected
  destination drive, blocks until done) for the times a restore point
  alone isn't enough. See `CHANGELOG.md`'s 0.15.0 entry.
- ~~Explorer tweaks~~ - **done in 0.17.0**: show file extensions, show
  hidden files, restore the classic (non-condensed) right-click context
  menu. See `CHANGELOG.md`'s 0.17.0 entry.
- ~~Taskbar/Start decluttering~~ - **done in 0.17.0**: hide widgets, hide
  the search box, disable the Copilot button. Purely cosmetic Windows
  settings, all one-click to put back. See `CHANGELOG.md`'s 0.17.0 entry.
- ~~Ultimate Performance power plan~~ - **done in 0.18.0**: offered as an
  alternative to High Performance at the power-plan tweak step, via a
  small "[1] High performance [2] Ultimate Performance" choice right
  before the tweaks screen. See `CHANGELOG.md`'s 0.18.0 entry.
- ~~MMCSS background-task CPU reservation~~ - **done in 0.18.0**: the
  `SystemResponsiveness` value that reserves CPU for background tasks,
  set to 0 so games get full priority. See `CHANGELOG.md`'s 0.18.0 entry.
- ~~Start menu suggestions/ads~~ - **done in 0.18.0**: turns off the
  suggested-apps/tips content Windows injects into Start. See
  `CHANGELOG.md`'s 0.18.0 entry.
- ~~Fast Startup toggle~~ - **done in 0.18.0**: the same effect as
  unchecking "Turn on fast startup" in Control Panel's Power Options. See
  `CHANGELOG.md`'s 0.18.0 entry.
- **Debloat pass, opt-in per item** - trial Office nag, preinstalled OEM
  trialware, unwanted Xbox Game Bar overlay, etc. Never a blanket "remove
  everything" - each one listed and individually skippable. Deliberately
  not bundled in with the tweaks in 0.19.0 - this is a different shape of
  feature (removing pre-installed apps, not changing a setting) and needs
  its own opt-in-per-item list UI and app-detection logic, worth designing
  on its own rather than squeezed into the existing tweaks pattern.
  Concrete starting list of what to target, found while reviewing
  [theantipopau/windows11nontouchgamingoptimizer](https://github.com/theantipopau/windows11nontouchgamingoptimizer)
  (MIT-licensed - fine to reuse the *idea* of which packages, though this
  project's own tweaks are always reimplemented natively in C#, never
  shelled out to someone else's script): Bing News/Weather, Clipchamp,
  Skype, Teams, TikTok, Spotify, Disney+ and similar preinstalled consumer
  apps.
- **Mouse acceleration disable** - the classic "Enhance pointer precision"
  off tweak (`MouseSpeed`/`MouseThreshold1`/`MouseThreshold2` under
  `HKCU\Control Panel\Mouse`, all set to 0). Very standard among FPS
  players, one clean per-user registry change, trivially reversible -
  fits the `ConfirmTweaks` pattern directly.
- **CPU core parking disabled** - a real, documented power-plan setting
  (`ValueMin`/`ValueMax` on the core-parking min-cores-parked GUID,
  `0cc5b647-c1df-4637-891a-dec35c318583`, under the active power scheme)
  that stops Windows idling CPU cores. Same spirit as the existing
  power-plan tweaks, just a `powercfg -setacvalueindex`/
  `-setdcvalueindex` pair instead of a plain registry write.
- **Win32PrioritySeparation** - boosts foreground-app CPU scheduling
  priority (`HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl`,
  value `Win32PrioritySeparation`). Single documented DWORD, same
  disclosed/reversible shape as everything else here.
- **Delivery Optimization disabled** - stops Windows using this PC's
  bandwidth to share Windows Update downloads P2P with other PCs on the
  internet (`HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization`,
  value `DODownloadMode` set to 0). Clean, reversible, no real downside
  for a gaming rig.
- **Disable DiagTrack (Connected User Experiences and Telemetry)** - a
  service toggle rather than a registry flag (still reversible - the
  service's start-type just goes back to what it was), standard in most
  gaming/privacy tweak guides.
- Deliberately **not** taking from that same repo: disabling
  SysMain/Superfetch (helps HDDs, can be neutral-to-negative on an SSD
  with plenty of RAM - not a clean universal win), setting Windows Search
  to manual (a real cost to Start menu search speed, not a freebie),
  raising `MaxUserPort` or disabling 8.3 filenames (marginal value for a
  single gaming PC, more relevant to servers), and its "intelligent" page
  file/memory compression changes (riskier to get wrong than the benefit
  justifies - Microsoft's own defaults are usually fine here).
- ~~Windows Update active hours~~ - **done in 0.19.0**: sets the official
  "active hours" window to 16:00-23:00 (typical evening gaming times),
  rather than disabling updates. See `CHANGELOG.md`'s 0.19.0 entry.
- ~~NVMe TRIM / storage health check~~ - **done in 0.19.0**: read-only
  reporting on the fake BIOS screen next to the disk list - real per-disk
  WMI health status, and a TRIM (delete notify) status line. See
  `CHANGELOG.md`'s 0.19.0 entry.
- ~~Xbox Game Bar / Game DVR toggle~~ - **done in 0.19.0**: some capture
  software conflicts with it; a clear on/off tweak on the `ConfirmTweaks`
  screen. See `CHANGELOG.md`'s 0.19.0 entry.
- ~~Network/QoS tweak for gaming (Nagle's algorithm half)~~ - **done in
  0.19.0**: disables Nagle's algorithm on whichever network interface is
  actually active. See `CHANGELOG.md`'s 0.19.0 entry.
- **DNS switch (Cloudflare/Google)** - the other half of the original
  "Network/QoS tweak" idea, deliberately split off rather than bundled
  into 0.19.0. Needs its own provider-choice screen, and carries more
  risk than a registry flag - a VPN, parental controls, or an
  ISP-specific service can all depend on the DNS servers already in use,
  so this deserves its own careful design pass rather than a quick add.

## Apps to add to the catalog

- **MSI Afterburner / RivaTuner** - OC + on-screen FPS overlay, a big one
  for a gaming rig (no reliable winget package as of writing - see
  `CLAUDE.md`'s Next steps for the caveat).
- **Sunshine** - self-hosted Moonlight streaming host, for streaming to a
  Steam Deck or another room.
- **EarTrumpet** - per-app volume mixer, small and well loved, on winget.
- **ShareX** - screenshot/recording utility, common alongside
  Discord/streaming setups.
- **DS4Windows / Steam Input helper** - for non-Xbox controllers.
- **A clipboard manager** (e.g. Ditto) - small quality-of-life win.
- **WinDirStat / WizTree** - visualize disk usage after a big install run.
- **TMOG (Task Manager OG)** - Dave Plummer's (original 1996 Windows Task
  Manager author) modern task manager/system monitor, from
  [tmog.org](https://tmog.org). Free beta available, paid Pro tier. Held
  back for now because the site doesn't mention a winget package or a
  GitHub repo, and none could be found in `winget-pkgs` either - no stable
  unattended install path yet. Worth re-checking later, since a project
  with this much community goodwill often gets a winget package
  eventually.
- **ASUS Armoury Crate** - RGB/fan/AIO control for ASUS motherboards and
  peripherals. A winget package exists (`Asus.ArmouryCrate`), but it's
  had at least one manifest break already: ASUS hosts the installer
  behind a version-agnostic URL, so when they quietly swap the file
  behind it the hash-pinned winget manifest stops working until someone
  notices and re-pins it (see
  [winget-pkgs#430531](https://github.com/microsoft/winget-pkgs/pull/430531)).
  Same category of problem as Razer Cortex/Hyte Nexus/L-Connect 3 above,
  just via winget instead of a manual link - worth adding once there's
  a manual-download fallback path to fall back on if the winget install
  fails, matching how those three are already handled.
- **"PC Smart Utility"** - the user asked about this by name, but it
  couldn't be confidently identified. There's a Microsoft Store listing
  under this name describing an all-in-one cleanup/monitoring/"PC health
  score" tool, which reads a lot like generic system-optimizer software
  (a category that's frequently low-quality or borderline scareware) and
  its actual publisher couldn't be confirmed as Microsoft or anyone else
  from the store listing alone. Not adding anything under this name
  until it's confirmed exactly what tool is meant - if the user meant a
  specific different product, worth asking for a link next time it comes
  up.

## Retro computing / emulation

- **Windows 98 gaming VM, pre-configured but not pre-installed** - install
  [86Box](https://86box.net) (winget package `86Box.86Box`), the
  cycle-accurate PC emulator the retro-gaming community actually uses for
  this, rather than a generic hypervisor like VirtualBox/VMware - those
  don't emulate the period chipsets, Sound Blaster/AdLib audio, or 3dfx
  Voodoo-era graphics that Windows 98 games actually expect, so games
  either won't install or won't run right. GamingStack would install
  86Box and drop in a pre-built virtual machine config (86Box's own JSON
  format) tuned as a period-accurate late-90s gaming rig - a
  Pentium/AMD-era CPU, a Voodoo3 or similar, an SB16 - so it boots straight
  to a BIOS screen ready for an OS.
  - **The one thing this can't do**: install Windows 98 itself. It isn't
    freeware or redistributable - Microsoft's copyright on it hasn't
    lapsed just because it's unsupported, so GamingStack can't bundle,
    download, or auto-fetch an ISO or product key on your behalf. The
    feature has to stop at "here's the emulator, pre-configured and
    pointed at an empty virtual CD drive" with a short readme on where to
    mount your own legally-owned Windows 98 disc/ISO and enter your own
    key - the same way any legitimate retro-gaming guide handles this.
  - Fits the disclosed/reversible rule cleanly: installing 86Box and a
    config file is no different from installing any other catalog app,
    and removing it is just deleting the VM folder - nothing touches the
    host system's registry or settings at all.
  - Worth scoping as its own small feature when picked up: what hardware
    profile to ship as the default (a Voodoo3 + SB16 covers most late-90s
    3D titles), where the VM files live, and how much of the 86Box
    config screen to explain in the readme versus just linking to
    86Box's own docs.

## Automation / quality of life

- **Generated "build sheet"** - a text or PDF summary of the real hardware
  detected plus everything installed, dropped on the desktop as a record
  that outlives the app closing.
- **Steam library folder pre-creation on the fastest detected drive** -
  check `MediaType` via WMI so games don't default onto a slower secondary
  disk without the user knowing.
- **Auto-arrange/hide desktop icons** after installers finish, so the
  desktop isn't left cluttered with shortcuts.
- **First-boot reminder note** - Windows activation status, a nudge to
  check for manufacturer driver updates (pointing at their site, never
  auto-installing anything itself).

## Fun / personality

- **Closing "build stats" credits screen** - total install time, apps
  installed, a made-up "rig power level" score derived from the real
  CPU/GPU/RAM numbers. Purely cosmetic, no functional effect (learned that
  lesson from the Konami code discussion - a joke payoff stays a joke).
- **Rare alternate boot flavor text** - a small chance of a different joke
  BIOS line each run, so repeat runs feel a little alive instead of static.
- **Hidden dev/debug overlay** - a second secret key combo (distinct from
  the Konami code) that shows real live system stats as a "hacker mode"
  joke.
- **Escalating easter egg for repeat wizard-decliners** - something extra
  if you decline the whole wizard more than once across runs. Would need a
  tiny bit of persisted state (a counter file) to track "more than once
  across runs" - worth scoping carefully before starting, since everything
  else in this app is deliberately stateless between runs.

## Working style reminder

Same as always: one item at a time, confirm the design before building,
test after, keep `CHANGELOG.md`/`README.md`/version numbers in lock-step
with what's actually shipped.
