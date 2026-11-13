# Security

GamingStack runs as administrator, installs software and (only when you say yes) changes Windows
settings, so it is worth being careful. This page says what it does, how you can check it, and how
to report a problem.

## What it does on your PC

- **Installs apps** with Windows' own package manager (`winget`), or opens the vendor's page in your
  browser. The list of exactly which packages is in [docs/CATALOG.md](docs/CATALOG.md).
- **Downloads a few installers directly** from vendor websites (listed in the catalog). Each download
  is logged with its size, SHA-256 and the signer's name before it is run.
- **Changes Windows settings only after you approve each one**, and writes an undo script.
- **Creates a System Restore point first**, every time.
- **Does not** collect or send any data, has no telemetry, no accounts and no auto-update. The only
  network traffic is winget, the vendor downloads in the log, and links opened in your own browser.

It is open source: everything above can be read in the code. Start with `InstallerEngine.cs`.

## Verify the download

Each [release](https://github.com/morpheus2n2/GamingStack/releases) lists the SHA-256 of
`GamingStackGUI.exe`. Check yours:

```powershell
Get-FileHash .\GamingStackGUI.exe -Algorithm SHA256
```

Or build it yourself from source (see the [README](README.md#building-from-source)) and compare behaviour.
The .NET build is not byte-for-byte reproducible, so the hashes of your own build will differ.

## Antivirus scan results

Results for each release are recorded here, with exactly what was and was not checked.

| Version | SHA-256 of the released `GamingStackGUI.exe` |
|---|---|
| 1.6.1 | `31e8f6f032873ca5ccc84f95d5ec9da112253af488e14bc563aae20c45be9627` |
| 1.6.0 | `e0f4672a58a966177c3c24fcda4ce949374b440097b29b0cee35cbba14decd7f` |

**1.6.1 release exe (scanned 2026-11-13, by the release workflow):** VirusTotal
[full file scan](https://www.virustotal.com/gui/file/31e8f6f032873ca5ccc84f95d5ec9da112253af488e14bc563aae20c45be9627)
of the real 69 MB `GamingStackGUI.exe`: **2 of 70 engines flagged it** - Bkav Pro
(`W32.Malware.AEA4C5FF`) and Zillya (`Trojan.Agent.Win32.4695414`). All 68 others, including Microsoft
Defender, Kaspersky, ESET, Bitdefender, Avast, AVG, Malwarebytes, CrowdStrike, Sophos and ClamAV, reported
nothing. Both detections are generic/heuristic labels from smaller engines, not named malware families, and
are the typical false-positive pattern for an unsigned single-file .NET program that asks for administrator
rights (see below). They are listed here rather than hidden; the source code is all in this repository so
anyone can check what the program does.

**1.6.0 (checked 2026-11-13):**

- **Download link scan - done, clean.** VirusTotal checked the release download address with its URL/
  reputation engines: **0 of 93 flagged it**
  ([report](https://www.virustotal.com/gui/url/3d877f4e40e2ec56a9c76a778cae62c29692be3fa7eaf0393745b123107b2086)).
  This checks the link and its host, *not* the contents of the file.
- **Program code scan - done, clean.** The project's own compiled code (`GamingStackGUI.dll`, 149 KB,
  SHA-256 `11e51507ce5618fe0281c96a2ca1b681572e9155b13f30a390512d4cae474b83`, from a local build) was
  uploaded to VirusTotal: **0 of 68 engines flagged it**
  ([report](https://www.virustotal.com/gui/file/11e51507ce5618fe0281c96a2ca1b681572e9155b13f30a390512d4cae474b83)).
  This is the app's own code, *not* the 72 MB single-file release exe (which also bundles the .NET
  runtime), and it is from a local build, so its hash is not the one in the table above.
- **Release exe scan - automated from 1.6.1.** The release workflow scans every tagged release with
  VirusTotal (the `VT_API_KEY` repository secret is set). Results for each version are listed above.
  The 1.6.0 exe itself was not scanned.

**Please read this honestly:** a self-contained single-file .NET program that asks for administrator
rights, installs software and changes settings can look "suspicious" to some antivirus engines even
when it is clean (this is called a false positive). If a scan shows a few detections from lesser-known
engines, that is why - the mainstream engines matter most, and the source code is here to read. The
exe is **not code-signed** yet (a certificate costs money), which is also why SmartScreen warns.

## Reporting a problem

- **A security problem** (something that could harm a user): please use GitHub's private
  [security advisory](https://github.com/morpheus2n2/GamingStack/security/advisories/new) rather than a
  public issue.
- **A bug or something that went wrong on your PC:** open an
  [issue](https://github.com/morpheus2n2/GamingStack/issues/new/choose) and attach the log from
  `Documents\GamingStack\logs` (it contains your PC name and Windows user name - remove them if you
  prefer).
