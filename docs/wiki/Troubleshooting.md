# Troubleshooting

## "winget not found"

Install **App Installer** from the Microsoft Store, then run GamingStack again. Without winget the winget
apps are skipped (the "opens download page" apps still work).

## An app failed to install

1. Look at the reason under the item on the Summary screen.
2. Open the log (Start menu > Setup Logs) and search for the app name - winget's own output is there.
3. Common causes: a package that is already installed or newer ("skipped" is normal), the vendor changed
   the package ID, the download link broke upstream, or the installer needs a restart first.
4. Try that app again later, or install it by hand.

## "The requested operation requires elevation"

You ran the Debug build from a terminal. Use the published exe (it asks for admin) or run your terminal as
administrator.

## The restore point could not be created

Windows limits restore points to one per 24 hours by default, and System Restore may be off. GamingStack
turns it on for the system drive and retries once. The Summary tells you what happened - do not rely on a
restore point that says it failed; consider the disk-image backup instead.

## The screen looks wrong / text too big or small

Start menu > Display Properties > Screen size. 100% is the default; try 125% or 150% on a 4K display.

## There is no sound

Sounds use your normal sound device. Check the Windows volume mixer for GamingStack.

## Docker or WSL will not start

They need virtualisation turned on in your BIOS/UEFI (look for "SVM" on AMD or "VT-x" on Intel) and a
restart after installing.

## Still stuck

Open an [issue](https://github.com/morpheus2n2/GamingStack/issues/new/choose) and attach the log from
`Documents\GamingStack\logs`.
