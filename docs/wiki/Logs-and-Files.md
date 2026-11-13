# Logs and Files

The exe can run from anywhere (Downloads, Desktop, a USB stick). It does not save anything next to itself.
"Documents" below means **your own Windows Documents folder, wherever Windows keeps it** - usually
`C:\Users\<you>\Documents`, or `...\OneDrive\Documents` when OneDrive backs it up. The Summary screen shows
the exact log path, and Start menu > **Setup Logs** opens it.

| What | Where |
|---|---|
| Install log | `Documents\GamingStack\logs\gamingstack_<date>_<time>.log` |
| Backup log | `Documents\GamingStack\logs\backup_<date>_<time>.log` |
| Undo scripts | `Documents\GamingStack\undo\revert-tweaks_<date>_<time>.cmd` |
| Build sheet (plain-text summary) | `Documents\GamingStack\build-sheet_<date>_<time>.txt` |
| Progress, trophies, settings, icon positions | `%APPDATA%\GamingStack\progress.json` |
| Notes app | `%APPDATA%\GamingStack\notes.txt` |
| Downloaded installers | `%TEMP%\GamingStack\downloads\` |

Start menu > **Setup Logs** opens the logs folder.

## What is in a log

A header (app version, computer and user name, Windows version, whether it ran as administrator, setup
type, backup choice, every tweak option, and every item with how it installs), then every step with a time
stamp, the result and how long it took, and a summary at the end. Direct downloads include their size,
SHA-256 and signer.

The log contains your PC name and Windows user name. If you share it (for example in a bug report), remove
those if you prefer.

## Resetting

Delete `progress.json` to reset trophies, scores and settings. Nothing else depends on it.
