# Roadmap

What has shipped is in [`CHANGELOG.md`](CHANGELOG.md). This file is the backlog: ideas worth doing,
not yet scheduled. Nothing here is promised for a version until it is picked up.

## The one rule

**Anything that changes a system setting must be shown on screen first (with the real current
value), need an explicit yes, and be reversible** - either because the app records the old value
and can restore it, or because it is a standard Windows setting the user could flip back in two
clicks. An idea that can't do both is out of scope, however useful.

## Done so far (headline view)

Three setup types (Gaming / Office / Developer) with ~170 catalog entries; fake BIOS with real
hardware; 16/32-bit boot screens that follow the time of day; restore point + optional disk-image
backup; disclosed, reversible tweaks with per-run undo scripts; optional DNS switch and app
removal; a final-check screen; full run logs; a Stop button; download-page links for apps without a
winget package; a retro desktop with windows, Start menu, draggable icons, My Computer, Trophy
Room, four arcade games, Notes, Paint, a Defrag toy, a Command Prompt with secrets, a screensaver
and Display Properties; saved progress and trophies.

## Still to verify on a real PC

These are written and build cleanly but have had little or no real-world testing:

- The debloat removals (`winget uninstall` on preinstalled Store apps) and the Store reinstall IDs
  in the undo script.
- The DNS switch and its undo.
- Installs of the newest catalog entries (each ID was checked with `winget show`, but not every
  package has been installed end to end).
- The final-check screen layout on small screens, and the three-column app list at 1080p.

## System tweaks (ideas)

- Disable SysMain/Superfetch (helps spinning disks, can hurt SSDs - would need a drive check first).
- Windows Search indexing options (a real cost to Start-menu search - would need a clear warning).
- A notification / focus-assist profile for gaming sessions.
- Per-app GPU preference (High performance) for installed games.
- Pause Windows Update for N days (a documented setting) instead of changing active hours.

## Apps and sources (ideas)

- Winget IDs are checked when they are added, but vendor IDs drift. A "catalog health" script that
  runs `winget show` over every ID and reports misses would catch that early.
- More "opens download page" entries for vendor tools with no winget package (peripheral suites,
  motherboard utilities).
- A user-editable extra list (a plain text file of winget IDs) so people can add their own apps
  without rebuilding.
- Google Gemini and Perplexity are left out until they have an official, maintained package.

## Automation (ideas)

- A "dry run" mode that goes through every screen and writes the log without installing anything.
- Export / import your selection as a small file, to repeat a setup on another PC.
- Code signing for the exe (removes the SmartScreen warning; needs a certificate).
- An unattended mode driven by a saved selection (still with the restore point and the recorded
  consent).

## Fun (ideas)

- More arcade games and trophies; a high-score board.
- More Command Prompt commands and secrets; more screensavers.
- Calculator, clock, a notepad with tabs, and a wallpaper picker.
- More boot themes (seasonal) and a sound pack.
- A Windows 98 VM helper: the 86Box emulator installs; Windows 98 itself and ROMs stay
  bring-your-own.
- An in-app "download only" mini-browser was considered for vendor pages and rejected in favour of
  the default browser (safer, nothing extra to maintain). Worth revisiting only if a real need
  appears.
