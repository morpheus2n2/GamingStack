# Security and Scans

The short version is in [SECURITY.md](https://github.com/morpheus2n2/GamingStack/blob/master/SECURITY.md).
This page explains it in more detail.

## What it does

- Installs apps through winget or opens vendor pages in your browser.
- Downloads a few installers directly (each is logged with size, SHA-256 and signer before it runs).
- Changes settings only after you approve each one, with an undo script.
- Always makes a System Restore point first.
- No telemetry, no accounts, no auto-update, no data sent anywhere.

## Verify a download

```powershell
Get-FileHash .\GamingStackGUI.exe -Algorithm SHA256
```

Compare with the hash on the release page and in SECURITY.md.

## Antivirus scan results

Releases are scanned with [VirusTotal](https://www.virustotal.com/); results are recorded in
[SECURITY.md](https://github.com/morpheus2n2/GamingStack/blob/master/SECURITY.md#antivirus-scan-results).

Why a clean program can still trigger a scanner: it is a self-contained single-file .NET exe that asks for
administrator rights, runs installers silently and edits registry settings. Those behaviours look similar to
malware, so a few lesser-known engines may flag it (a false positive). The program is unsigned for now.
Everything it does is visible in the source code.

## Reporting a problem

Security problems: use a private
[security advisory](https://github.com/morpheus2n2/GamingStack/security/advisories/new).
Anything else: an [issue](https://github.com/morpheus2n2/GamingStack/issues/new/choose).
