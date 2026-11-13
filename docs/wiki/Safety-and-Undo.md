# Safety and Undo

## The safety net

1. **A System Restore point is always created first.** GamingStack uses Windows' own call; if that
   fails it turns System Restore on for your system drive and tries once more. The summary says whether it
   really worked.
2. **Optional full disk-image backup** (Windows `wbadmin`) to a drive you choose - before the changes
   or after setup. It runs to completion before moving on so it is a clean snapshot. Each drive's free
   space is shown next to the space needed.

## The Final Check

Before anything is installed or changed, one screen lists what is about to happen: the setup type, how
many apps and how they install, whether Windows settings change, DNS and removals, the safety net, and
where the log goes. To start you must type **G O** and press **Enter**. Press **N** to back out - nothing
has been changed at that point.

## Stopping

While installing, press **C** to stop after the current item. Tweaks and app removals that had not
started are skipped (and the log says so). Nothing already installed is rolled back.

## Undoing tweaks

Every run writes an undo script to `Documents\GamingStack\undo\` (one per run, never overwritten). Before
each change GamingStack records the setting's real previous value in it. To undo, right-click the script
and choose **Run as administrator**. If you ran GamingStack more than once, use the script from the run you
want to undo.

## Undoing installs

Uninstall apps the normal way (Settings > Apps) or with `winget uninstall --id <ID>` - the IDs are in the
log. App removals (debloat) are put back by the undo script via the Microsoft Store.

## If something goes wrong

Use **System Restore** (search "Create a restore point" > System Restore) to go back to the point made at
the start of the run, or restore the disk image if you made one.
