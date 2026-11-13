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

The released exe is scanned with [VirusTotal](https://www.virustotal.com/). The result for each
version is recorded here:

| Version | SHA-256 | Result |
|---|---|---|
| 1.6.0 | `4c22bc1601588edd72bb38869a233051c90b7f7ad785f60aad2100912d7176b9` | _scan pending - see below_ |

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
