using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GamingStackGUI
{
    public enum AppKind { Winget, Manual, Bundle }

    /// <summary>
    /// One installable thing, with a human-friendly name for display in the wizard
    /// (the winget ID / manual URL are what actually gets run, kept separate so the
    /// UI never has to show raw package IDs to the user). <see cref="Category"/> is
    /// purely for grouping the wizard's on-screen list under a heading - it plays no
    /// part in install order or logic.
    /// </summary>
    public class AppEntry
    {
        public required string FriendlyName { get; init; }
        public required string Category { get; init; }
        public required AppKind Kind { get; init; }
        public string? WingetId { get; init; }
        public string? ManualFileName { get; init; }
        public string? ManualUrl { get; init; }
        // Bundle kind only: a handful of winget IDs installed back-to-back under one
        // catalog entry (e.g. every VC++ Redistributable version), so the wizard shows
        // a single friendly line instead of a dozen near-identical ones.
        public string[]? BundleWingetIds { get; init; }
    }

    /// <summary>
    /// What happened to one catalog entry, once. Fired via
    /// <see cref="InstallerEngine.OnItemResult"/> alongside the free-text log so the
    /// UI can build a structured end-of-run summary (grouped by category, like the
    /// wizard's own list) instead of parsing human-readable log lines.
    /// </summary>
    public enum InstallResultKind { Installed, Failed, Skipped }

    public readonly record struct ItemResult(AppEntry Entry, InstallResultKind Kind, string? Detail = null);

    /// <summary>
    /// The actual install stack, ported directly from the earlier PowerShell version -
    /// same behavior (retries, manual-installer fallback chain, tweaks), just written
    /// as C# so it runs in-process instead of shelling out to a script.
    /// Fires <see cref="OnLog"/> for every line so the UI can print it into the
    /// terminal window without this class knowing anything about WinForms, and takes
    /// the list of what to actually install as a parameter to <see cref="RunAsync"/> -
    /// the wizard in MainForm decides that, this class just does the work.
    /// </summary>
    public class InstallerEngine
    {
        public event Action<string>? OnLog;
        public event Action? OnFinished;
        // Fired once per selected catalog entry, after that entry's own install
        // attempt(s) finish - the summary screen builds off these, not the log text.
        public event Action<ItemResult>? OnItemResult;

        private static readonly HttpClient Http = new();

        // The full catalog the wizard offers, grouped by Category purely for display -
        // the wizard renders one heading per category, in the order categories first
        // appear here. Edit here to add/remove/rename what's on offer - friendly names
        // are what the user sees, everything else is what actually gets run.
        //
        // Every winget ID below has been checked against the winget-pkgs repo directly
        // (not just "seemed right") as of 2026-09. Vendor-published apps (RGB/peripheral
        // control especially) change package IDs more than most, so if one starts
        // failing every time, that's the first thing to re-check with `winget search`.
        public static readonly IReadOnlyList<AppEntry> Catalog = new List<AppEntry>
        {
            // Launchers / core
            new() { FriendlyName = "Discord", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "Discord.Discord" },
            new() { FriendlyName = "Steam", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "Valve.Steam" },
            new() { FriendlyName = "Ubisoft Connect", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "Ubisoft.Connect" },
            new() { FriendlyName = "Epic Games Launcher", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "EpicGames.EpicGamesLauncher" },
            new() { FriendlyName = "GOG Galaxy", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "GOG.Galaxy" },
            new() { FriendlyName = "Amazon Games", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "Amazon.Games" },
            new() { FriendlyName = "Battle.net", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "Blizzard.BattleNet" },
            new() { FriendlyName = "Playnite (unifies the above)", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "Playnite.Playnite" },

            // GPU / drivers / monitoring / performance
            new() { FriendlyName = "NVIDIA App", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "Nvidia.App" },
            new() { FriendlyName = "HWiNFO", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "REALiX.HWiNFO" },
            new() { FriendlyName = "CPU-Z", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "CPUID.CPU-Z" },
            new() { FriendlyName = "Speccy", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "Piriform.Speccy" },
            new() { FriendlyName = "CrystalDiskInfo", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "CrystalDewWorld.CrystalDiskInfo" },
            new() { FriendlyName = "Process Lasso", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "BitSum.ProcessLasso" },
            // No winget package exists for Cortex - Razer only distributes it as a
            // direct download, so it's a Manual entry like Hyte Nexus/L-Connect 3
            // below rather than a (nonexistent) winget ID. Uses Razer's own stable
            // "DOWNLOAD NOW" short link (rzr.to/cortex-download, currently 302s to
            // dl.razerzone.com/drivers/GameBooster/RazerCortexInstaller.exe) rather
            // than the resolved CDN URL, since HttpClient follows redirects and the
            // short link should keep working even if Razer moves the file.
            new() { FriendlyName = "Razer Cortex", Category = "Monitoring & Performance", Kind = AppKind.Manual,
                    ManualFileName = "RazerCortexInstaller.exe",
                    ManualUrl = "https://rzr.to/cortex-download" },

            // Streaming / recording / audio
            new() { FriendlyName = "Streamlabs Desktop", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "Streamlabs.StreamlabsOBS" },
            new() { FriendlyName = "Medal.tv", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "Medal.Medal" },
            new() { FriendlyName = "Voicemeeter Banana", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "VB-Audio.Voicemeeter.Banana" },

            // General utility
            new() { FriendlyName = "VS Code", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "Microsoft.VisualStudioCode" },
            new() { FriendlyName = "PowerShell 7", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "Microsoft.PowerShell" },
            new() { FriendlyName = "Microsoft 365 Apps", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "Microsoft.Office" },
            new() { FriendlyName = "PowerToys", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "Microsoft.PowerToys" },
            new() { FriendlyName = "Python 3", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "Python.Python.3" },
            new() { FriendlyName = "VLC Media Player", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "VideoLAN.VLC" },
            new() { FriendlyName = "7-Zip", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "7zip.7zip" },
            new() { FriendlyName = "Vortex Mod Manager", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "NexusMods.Vortex" },

            // Redistributables - the one entry everyone should just say yes to. A single
            // catalog line that silently installs every VC++ runtime version games and
            // creator apps commonly expect, so people stop hitting "missing MSVCP140.dll"
            // errors. See InstallBundleAsync for how a Bundle entry actually installs.
            new() { FriendlyName = "VC++ Redistributables Pack (all versions)", Category = "Redistributables", Kind = AppKind.Bundle,
                    BundleWingetIds = new[]
                    {
                        "Microsoft.VCRedist.2005.x86", "Microsoft.VCRedist.2005.x64",
                        "Microsoft.VCRedist.2008.x86", "Microsoft.VCRedist.2008.x64",
                        "Microsoft.VCRedist.2010.x86", "Microsoft.VCRedist.2010.x64",
                        "Microsoft.VCRedist.2012.x86", "Microsoft.VCRedist.2012.x64",
                        "Microsoft.VCRedist.2013.x86", "Microsoft.VCRedist.2013.x64",
                        "Microsoft.VCRedist.2015+.x86", "Microsoft.VCRedist.2015+.x64"
                    } },

            // RGB / peripheral ecosystems - manufacturer control software for lighting,
            // fan curves, etc. Nobody owns all of these, so they're skippable like the
            // manual installers below. OpenRGB is the odd one out: a single open-source
            // app that talks to hardware from several vendors at once, for anyone who'd
            // rather avoid running four different vendor apps.
            // All three IDs below verified against the winget-pkgs repo directly.
            new() { FriendlyName = "Corsair iCUE", Category = "RGB & Peripheral Control", Kind = AppKind.Winget, WingetId = "Corsair.iCUE.4" },
            // Was "Razer.Synapse.3" - that ID doesn't exist; Razer's actual winget
            // package is namespaced under their installer, not a standalone "Razer.*".
            // Synapse 4 is the current version per Razer's own site, so this targets
            // that rather than the still-available (but now legacy) Synapse 3 package.
            new() { FriendlyName = "Razer Synapse", Category = "RGB & Peripheral Control", Kind = AppKind.Winget, WingetId = "RazerInc.RazerInstaller.Synapse4" },
            // Was "CalcProgrammer1.OpenRGB" - the winget-pkgs maintainers renamed this to
            // the application-specific ID and switched its installer to an MSI.
            new() { FriendlyName = "OpenRGB (universal, multi-vendor)", Category = "RGB & Peripheral Control", Kind = AppKind.Winget, WingetId = "OpenRGB.OpenRGB" },
            new() { FriendlyName = "Hyte Nexus (HYTE case/AIO control)", Category = "RGB & Peripheral Control", Kind = AppKind.Manual,
                    ManualFileName = "HyteNexusInstaller.exe", ManualUrl = "https://hyte.co/nexus-download" },
            new() { FriendlyName = "L-Connect 3 (Lian Li fan/lighting control)", Category = "RGB & Peripheral Control", Kind = AppKind.Manual,
                    ManualFileName = "LConnect3.zip",
                    ManualUrl = "https://lianli-update-2025.lianli-cn.com/L3_CX/20260422-L-Connect%203-x64-v2.1.20-fde9a570.zip" }
        };

        // A handful of winget exit codes are common enough, and confusing enough out of
        // context, that they're worth translating into plain English instead of showing
        // a bare "exit code -1978335189" on the summary screen. AlreadyUpToDate codes are
        // treated as a Skipped result rather than a Failed one - winget refusing to
        // reinstall something that's already current isn't actually a problem. Sourced
        // from AppInstallerErrors.h in the winget-cli repo, plus the HRESULT convention
        // where a WinHTTP-facility code's low word is a literal HTTP status.
        private static readonly Dictionary<int, (bool AlreadyUpToDate, string Friendly)> KnownWingetExitCodes = new()
        {
            // APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE - an equal or newer version is
            // already installed, so winget refuses to reinstall it. Not a real failure.
            { unchecked((int)0x8A15002B), (true, "already installed and up to date - nothing to do") },
            // APPINSTALLER_CLI_ERROR_EXEC_UNINSTALL_COMMAND_FAILED - winget tried to
            // repair/replace an existing install and the uninstall step it ran first
            // failed. Usually a leftover broken install; uninstalling it by hand first
            // and re-running the install usually clears it.
            { unchecked((int)0x8A150030), (false, "a broken existing install is blocking this - try uninstalling it manually first, then rerun") },
            // WinHTTP-facility HRESULT whose low word is a literal HTTP status code -
            // 0x194 = 404. Means winget's own package manifest currently points at a dead
            // download URL upstream. Nothing wrong with this app or your PC - just wait
            // for the manifest to get fixed, or check "winget show <id>" another day.
            { unchecked((int)0x80190194), (false, "winget's download link for this is currently broken upstream (404) - not something this app can fix, try again another day") },
        };

        private static string DescribeExitCode(int exitCode) =>
            KnownWingetExitCodes.TryGetValue(exitCode, out var known)
                ? known.Friendly
                : $"exit code {exitCode} (unrecognized - check the full log for winget's own output)";

        private const int MaxRetries = 2;

        private readonly string _workDir = Path.Combine(Path.GetTempPath(), "GamingStack");
        private string FailFile => Path.Combine(_workDir, "failed.txt");

        // Every OnLog line, verbatim, written to disk as it happens - this exists
        // specifically because the terminal window scrolls past faster than anyone
        // can read exit codes off it, and failed.txt only ever captured the handful
        // of things that called Fail(), not the full "attempt 1... attempt 2..."
        // trail. One timestamped file per run, so a previous run's log is never
        // overwritten by the next one. MainForm reads this back via LogFilePath.
        private string? _logFilePath;
        public string? LogFilePath => _logFilePath;

        private void Log(string text)
        {
            OnLog?.Invoke(text);
            if (_logFilePath != null)
            {
                try { File.AppendAllText(_logFilePath, text + Environment.NewLine); }
                catch { /* logging shouldn't itself be fatal */ }
            }
        }

        private void Fail(string text)
        {
            try { File.AppendAllText(FailFile, text + Environment.NewLine); }
            catch { /* logging the failure shouldn't itself be fatal */ }
            Log($"  ! {text}");
        }

        /// <summary>
        /// Installs exactly what's passed in - the wizard (MainForm) is responsible
        /// for deciding that, whether it's the full catalog, a hand-picked subset, or
        /// (if the user picked nothing) an empty list, which is a no-op here.
        /// </summary>
        public async Task RunAsync(IReadOnlyList<AppEntry> selected)
        {
            try
            {
                Directory.CreateDirectory(_workDir);
                try { File.Delete(FailFile); } catch { /* fine if it didn't exist */ }

                var logsDir = Path.Combine(_workDir, "logs");
                Directory.CreateDirectory(logsDir);
                _logFilePath = Path.Combine(logsDir, $"install_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                try
                {
                    File.WriteAllText(_logFilePath,
                        $"GamingStack install log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}");
                }
                catch { /* if this fails, Log() below just no-ops the file side */ }

                if (selected.Count == 0)
                {
                    Log("Nothing selected - nothing to install.");
                    return;
                }

                Log($"Installing {selected.Count} selected item(s)...");
                Log("");

                var wingetOk = IsWingetAvailable();
                if (!wingetOk && selected.Any(a => a.Kind is AppKind.Winget or AppKind.Bundle))
                {
                    Log("WARNING: winget not found - winget-based selections will be skipped.");
                    Fail("winget not found - install 'App Installer' from the Microsoft Store to install winget-based apps.");
                }

                foreach (var entry in selected)
                {
                    if (entry.Kind == AppKind.Winget)
                    {
                        if (!wingetOk)
                        {
                            Log($"[{entry.FriendlyName}] skipped - winget unavailable");
                            OnItemResult?.Invoke(new ItemResult(entry, InstallResultKind.Skipped, "winget unavailable"));
                            continue;
                        }
                        var (kind, detail) = await InstallWingetAppAsync(entry);
                        OnItemResult?.Invoke(new ItemResult(entry, kind, detail));
                    }
                    else if (entry.Kind == AppKind.Bundle)
                    {
                        if (!wingetOk)
                        {
                            Log($"[{entry.FriendlyName}] skipped - winget unavailable");
                            OnItemResult?.Invoke(new ItemResult(entry, InstallResultKind.Skipped, "winget unavailable"));
                            continue;
                        }
                        var (kind, detail) = await InstallBundleAsync(entry);
                        OnItemResult?.Invoke(new ItemResult(entry, kind, detail));
                    }
                    else
                    {
                        var (ok, detail) = await HandleManualInstallerAsync(entry);
                        OnItemResult?.Invoke(new ItemResult(entry, ok ? InstallResultKind.Installed : InstallResultKind.Failed, detail));
                    }
                }

                Log("");
                Log("Applying gaming tweaks...");
                ApplyTweaks();

                Log("");
                if (File.Exists(FailFile))
                    Log($"Done, with a few things needing manual attention - see {FailFile}");
                else
                    Log("Done.");
            }
            catch (Exception ex)
            {
                Log($"FATAL: {ex.Message}");
            }
            finally
            {
                if (_logFilePath != null)
                    Log($"Full log saved to {_logFilePath}");
                OnFinished?.Invoke();
            }
        }

        private static bool IsWingetAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo("winget", "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private Task<(InstallResultKind Kind, string? Detail)> InstallWingetAppAsync(AppEntry entry) =>
            InstallWingetIdAsync(entry.WingetId!, entry.FriendlyName);

        /// <summary>
        /// A Bundle entry is just several winget IDs installed back-to-back under one
        /// friendly catalog line (e.g. every VC++ Redistributable version) - each ID
        /// still gets its own retry loop, but the outcome is summarized under the
        /// bundle's own name so the log doesn't read like a dozen unrelated packages.
        /// Already-up-to-date members don't count against the bundle - only a genuine
        /// failure (something other than "already installed") marks the whole bundle
        /// as Failed.
        /// </summary>
        private async Task<(InstallResultKind Kind, string? Detail)> InstallBundleAsync(AppEntry entry)
        {
            var ids = entry.BundleWingetIds ?? Array.Empty<string>();
            Log($"[{entry.FriendlyName}] installing {ids.Length} package(s) silently...");
            var installedCount = 0;
            var skippedCount = 0;
            var failedCount = 0;
            string? lastFailedDetail = null;
            foreach (var id in ids)
            {
                var (kind, detail) = await InstallWingetIdAsync(id, $"{entry.FriendlyName} ({id})", quiet: true);
                switch (kind)
                {
                    case InstallResultKind.Installed: installedCount++; break;
                    case InstallResultKind.Skipped: skippedCount++; break;
                    default: failedCount++; lastFailedDetail = detail; break;
                }
            }

            if (failedCount > 0)
            {
                var detail = $"{failedCount} of {ids.Length} genuinely failed - {lastFailedDetail}" +
                             (skippedCount > 0 ? $" ({skippedCount} more already up to date, which is fine)" : "");
                Fail($"{entry.FriendlyName}: {detail}");
                return (InstallResultKind.Failed, detail);
            }

            if (installedCount == 0)
            {
                Log($"[{entry.FriendlyName}] all {ids.Length} package(s) already up to date");
                return (InstallResultKind.Skipped, $"all {ids.Length} already installed and up to date");
            }

            Log($"[{entry.FriendlyName}] OK ({installedCount} installed{(skippedCount > 0 ? $", {skippedCount} already up to date" : "")})");
            return (InstallResultKind.Installed,
                skippedCount > 0 ? $"{installedCount} installed, {skippedCount} already up to date" : null);
        }

        /// <summary>
        /// Core winget install-by-id loop shared by single apps and bundle members.
        /// <paramref name="quiet"/> skips the individual Fail() write for bundle
        /// members (already-newer redist versions "fail" constantly and are noise);
        /// the bundle as a whole still reports its own summary via InstallBundleAsync.
        /// Returns a friendly Detail string alongside the outcome - for a recognized
        /// exit code that just means "already up to date", that's an immediate Skipped
        /// with no retries (winget isn't going to change its mind), since retrying three
        /// times just delays the run for no reason.
        /// </summary>
        private async Task<(InstallResultKind Kind, string? Detail)> InstallWingetIdAsync(string id, string displayName, bool quiet = false)
        {
            string? lastDetail = null;
            for (int attempt = 1; attempt <= MaxRetries + 1; attempt++)
            {
                Log($"[{displayName}] installing (attempt {attempt})...");
                try
                {
                    var psi = new ProcessStartInfo("winget",
                        $"install --id {id} -e --accept-package-agreements --accept-source-agreements --silent")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    if (p == null) throw new InvalidOperationException("winget failed to start");
                    await p.WaitForExitAsync();

                    if (p.ExitCode == 0)
                    {
                        Log($"[{displayName}] OK");
                        return (InstallResultKind.Installed, null);
                    }

                    if (KnownWingetExitCodes.TryGetValue(p.ExitCode, out var known) && known.AlreadyUpToDate)
                    {
                        Log($"[{displayName}] {known.Friendly}");
                        return (InstallResultKind.Skipped, known.Friendly);
                    }

                    lastDetail = DescribeExitCode(p.ExitCode);
                    Log($"[{displayName}] exit code {p.ExitCode} - {lastDetail}");
                }
                catch (Exception ex)
                {
                    lastDetail = ex.Message;
                    Log($"[{displayName}] error: {ex.Message}");
                }

                if (attempt <= MaxRetries)
                    await Task.Delay(3000);
            }

            if (!quiet)
                Fail($"winget install failed for {displayName} after {MaxRetries + 1} attempts - {lastDetail}");
            return (InstallResultKind.Failed, lastDetail ?? "unknown error");
        }

        private async Task<(bool Success, string? Detail)> HandleManualInstallerAsync(AppEntry entry)
        {
            var name = entry.FriendlyName;
            var fileName = entry.ManualFileName!;
            var url = entry.ManualUrl!;

            if (string.IsNullOrWhiteSpace(url))
            {
                Fail($"{name} missing download URL");
                return (false, "missing download URL");
            }

            var downloadDir = Path.Combine(_workDir, "downloads");
            Directory.CreateDirectory(downloadDir);
            var filePath = Path.Combine(downloadDir, fileName);

            Log($"[{name}] downloading...");
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, bytes);
            }
            catch (Exception ex)
            {
                Fail($"Download failed for {name}: {ex.Message}");
                return (false, $"download failed: {ex.Message}");
            }

            var installerPath = filePath;
            if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var extractDir = Path.Combine(downloadDir, Path.GetFileNameWithoutExtension(filePath));
                try
                {
                    if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                    ZipFile.ExtractToDirectory(filePath, extractDir);
                }
                catch (Exception ex)
                {
                    Fail($"Failed to extract {name}: {ex.Message}");
                    return (false, $"extract failed: {ex.Message}");
                }

                var exe = new DirectoryInfo(extractDir)
                    .GetFiles("*.exe", SearchOption.AllDirectories)
                    .OrderByDescending(f => f.Length)
                    .FirstOrDefault();
                if (exe == null)
                {
                    Fail($"No executable found inside {fileName} for {name}");
                    return (false, "no installer executable found in archive");
                }
                installerPath = exe.FullName;
            }

            Log($"[{name}] installing...");
            string[] silentArgs = { "/S", "/silent", "/quiet", "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", "--silent", "/qn" };
            foreach (var arg in silentArgs)
            {
                try
                {
                    var psi = new ProcessStartInfo(installerPath, arg)
                    {
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    if (p == null) continue;
                    await p.WaitForExitAsync();
                    if (p.ExitCode == 0)
                    {
                        Log($"[{name}] OK");
                        return (true, null);
                    }
                }
                catch
                {
                    // try the next flag variant
                }
            }

            Log($"[{name}] silent install didn't take - launching interactively");
            try
            {
                Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
                Fail($"{name} launched interactively - finish it manually if a window appeared");
                return (false, "no silent switch worked - launched interactively, finish it manually");
            }
            catch (Exception ex)
            {
                Fail($"Failed to launch installer for {name}: {ex.Message}");
                return (false, $"launch failed: {ex.Message}");
            }
        }

        private void ApplyTweaks()
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\GameBar");
                key?.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
                Log("[tweaks] Game Mode: enabled");
            }
            catch (Exception ex)
            {
                Fail($"Game Mode tweak failed: {ex.Message}");
            }

            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                key?.SetValue("HwSchMode", 2, RegistryValueKind.DWord);
                Log("[tweaks] Hardware-accelerated GPU scheduling: enabled");
            }
            catch (Exception ex)
            {
                Fail($"HAGS tweak failed: {ex.Message}");
            }

            try
            {
                // SCHEME_MIN is the built-in alias for the "High performance" power plan.
                var psi = new ProcessStartInfo("powercfg", "-setactive SCHEME_MIN")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
                Log("[tweaks] Power plan: High performance");
            }
            catch (Exception ex)
            {
                Fail($"Power plan tweak failed: {ex.Message}");
            }
        }
    }
}
