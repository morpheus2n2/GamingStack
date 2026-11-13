# FAQ

**Is it safe?** It is open source, has no telemetry, always makes a System Restore point first, shows
every change before it happens and saves an undo script and a full log. See
[Safety and Undo](Safety-and-Undo) and [Security and Scans](Security-and-Scans).

**Why does Windows warn me when I run it?** The exe is new and not code-signed (a certificate costs
money), so SmartScreen does not recognise it. Choose *More info > Run anyway* if you are happy with it, or
build it from source.

**Why does it need administrator rights?** To install apps silently and to change the settings you approve.
The Debug build used for development runs without admin, but installs and tweaks then fail.

**Will it install things I did not pick?** No. You approve the list, then confirm again on the Final Check.

**Can I stop it halfway?** Yes - press **C** while installing. It stops after the current item and skips
tweaks that had not started.

**An app failed or was skipped - why?** The summary shows the reason under each item, and the full log in
`Documents\GamingStack\logs` has winget's own output. See [Troubleshooting](Troubleshooting).

**What are the "opens download page" apps?** Vendor tools with no winget package (for example some
motherboard and GPU suites). GamingStack opens the vendor's own page in your default browser and you
install from there.

**Does it send my data anywhere?** No. The only network use is winget, the vendor downloads shown in the
log, and links opened in your own browser.

**Can I undo what it did?** Tweaks: run the undo script from `Documents\GamingStack\undo` as administrator.
Everything: use the System Restore point (or disk image) from the start of the run.

**How do I add my own apps?** Open an issue or a pull request - see
[CONTRIBUTING](https://github.com/morpheus2n2/GamingStack/blob/master/CONTRIBUTING.md).

**The text is too small / too big.** Start menu > Display Properties > Screen size.

**Where do trophies and scores live?** `%APPDATA%\GamingStack\progress.json`. Delete it to reset.
