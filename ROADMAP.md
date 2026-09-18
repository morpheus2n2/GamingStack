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

## System tweaks

- **Restore point before any tweaks run** - a "before we touch anything"
  safety net, and a natural fit for the install flow's tone. Also makes
  every tweak below trustworthy by default.
- **Tweak log on the summary screen** - extend the existing summary so it
  lists exactly what was changed *and* what it was before (not just "Game
  Mode: enabled", but "Game Mode: enabled (was: disabled)").
- **Explorer tweaks** - show file extensions, show hidden files, restore
  the classic (non-condensed) right-click context menu.
- **Taskbar/Start decluttering** - hide widgets, hide the search box,
  disable the Copilot button. Purely cosmetic Windows settings, all
  one-click to put back.
- **Debloat pass, opt-in per item** - trial Office nag, preinstalled OEM
  trialware, unwanted Xbox Game Bar overlay, etc. Never a blanket "remove
  everything" - each one listed and individually skippable.
- **Network/QoS tweak for gaming** - e.g. disabling Nagle's algorithm on
  the NIC, or offering a DNS switch (Cloudflare/Google). Show the old
  value, offer a one-click revert.
- **Windows Update active hours** - configure the official "active hours"
  setting around typical gaming times, rather than actually disabling
  updates.
- **NVMe TRIM / storage health check** - read-only reporting, no tweak
  involved, fits next to the existing CrystalDiskInfo install.
- **Xbox Game Bar / Game DVR toggle** - some capture software conflicts
  with it; offer to turn it off, with the on/off state shown clearly.

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
