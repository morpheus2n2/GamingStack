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
    public enum AppKind { Winget, Manual }

    /// <summary>
    /// One installable thing, with a human-friendly name for display in the wizard
    /// (the winget ID / manual URL are what actually gets run, kept separate so the
    /// UI never has to show raw package IDs to the user).
    /// </summary>
    public class AppEntry
    {
        public required string FriendlyName { get; init; }
        public required AppKind Kind { get; init; }
        public string? WingetId { get; init; }
        public string? ManualFileName { get; init; }
        public string? ManualUrl { get; init; }
    }

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

        private static readonly HttpClient Http = new();

        // The full catalog the wizard offers. Edit here to add/remove/rename what's
        // on offer - friendly names are what the user sees, everything else is what
        // actually gets run.
        public static readonly IReadOnlyList<AppEntry> Catalog = new List<AppEntry>
        {
            // Launchers / core
            new() { FriendlyName = "Discord", Kind = AppKind.Winget, WingetId = "Discord.Discord" },
            new() { FriendlyName = "Steam", Kind = AppKind.Winget, WingetId = "Valve.Steam" },
            new() { FriendlyName = "Ubisoft Connect", Kind = AppKind.Winget, WingetId = "Ubisoft.Connect" },
            new() { FriendlyName = "Epic Games Launcher", Kind = AppKind.Winget, WingetId = "EpicGames.EpicGamesLauncher" },
            new() { FriendlyName = "GOG Galaxy", Kind = AppKind.Winget, WingetId = "GOG.Galaxy" },
            new() { FriendlyName = "Amazon Games", Kind = AppKind.Winget, WingetId = "Amazon.Games" },
            new() { FriendlyName = "Battle.net", Kind = AppKind.Winget, WingetId = "Blizzard.BattleNet" },
            new() { FriendlyName = "Playnite (unifies the above)", Kind = AppKind.Winget, WingetId = "Playnite.Playnite" },

            // GPU / drivers / monitoring
            new() { FriendlyName = "NVIDIA App", Kind = AppKind.Winget, WingetId = "Nvidia.App" },
            new() { FriendlyName = "HWiNFO", Kind = AppKind.Winget, WingetId = "REALiX.HWiNFO" },
            new() { FriendlyName = "CPU-Z", Kind = AppKind.Winget, WingetId = "CPUID.CPU-Z" },
            new() { FriendlyName = "Speccy", Kind = AppKind.Winget, WingetId = "Piriform.Speccy" },
            new() { FriendlyName = "CrystalDiskInfo", Kind = AppKind.Winget, WingetId = "CrystalDewWorld.CrystalDiskInfo" },
            new() { FriendlyName = "Process Lasso", Kind = AppKind.Winget, WingetId = "BitSum.ProcessLasso" },

            // Streaming / recording / audio
            new() { FriendlyName = "Streamlabs Desktop", Kind = AppKind.Winget, WingetId = "Streamlabs.StreamlabsOBS" },
            new() { FriendlyName = "OBS Studio", Kind = AppKind.Winget, WingetId = "OBSProject.OBSStudio" },
            new() { FriendlyName = "Medal.tv", Kind = AppKind.Winget, WingetId = "Medal.Medal" },
            new() { FriendlyName = "Voicemeeter Banana", Kind = AppKind.Winget, WingetId = "VB-Audio.Voicemeeter.Banana" },

            // General utility
            new() { FriendlyName = "VS Code", Kind = AppKind.Winget, WingetId = "Microsoft.VisualStudioCode" },
            new() { FriendlyName = "PowerShell 7", Kind = AppKind.Winget, WingetId = "Microsoft.PowerShell" },
            new() { FriendlyName = "Microsoft 365 Apps", Kind = AppKind.Winget, WingetId = "Microsoft.Office" },
            new() { FriendlyName = "PowerToys", Kind = AppKind.Winget, WingetId = "Microsoft.PowerToys" },
            new() { FriendlyName = "Python 3", Kind = AppKind.Winget, WingetId = "Python.Python.3" },
            new() { FriendlyName = "VLC Media Player", Kind = AppKind.Winget, WingetId = "VideoLAN.VLC" },
            new() { FriendlyName = "7-Zip", Kind = AppKind.Winget, WingetId = "7zip.7zip" },
            new() { FriendlyName = "Vortex Mod Manager", Kind = AppKind.Winget, WingetId = "NexusMods.Vortex" },

            // Manual installers - no reliable winget package. Included in the same
            // pickable list as everything else so the wizard can offer to skip them
            // (most people don't own this specific hardware).
            new() { FriendlyName = "Hyte Nexus (HYTE case/AIO control)", Kind = AppKind.Manual,
                    ManualFileName = "HyteNexusInstaller.exe", ManualUrl = "https://hyte.co/nexus-download" },
            new() { FriendlyName = "L-Connect 3 (Lian Li fan/lighting control)", Kind = AppKind.Manual,
                    ManualFileName = "LConnect3.zip",
                    ManualUrl = "https://lianli-update-2025.lianli-cn.com/L3_CX/20260422-L-Connect%203-x64-v2.1.20-fde9a570.zip" }
        };

        private const int MaxRetries = 2;

        private readonly string _workDir = Path.Combine(Path.GetTempPath(), "GamingStack");
        private string FailFile => Path.Combine(_workDir, "failed.txt");

        private void Log(string text) => OnLog?.Invoke(text);

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

                if (selected.Count == 0)
                {
                    Log("Nothing selected - nothing to install.");
                    return;
                }

                Log($"Installing {selected.Count} selected item(s)...");
                Log("");

                var wingetOk = IsWingetAvailable();
                if (!wingetOk && selected.Any(a => a.Kind == AppKind.Winget))
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
                            continue;
                        }
                        await InstallWingetAppAsync(entry);
                    }
                    else
                    {
                        await HandleManualInstallerAsync(entry);
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

        private async Task InstallWingetAppAsync(AppEntry entry)
        {
            var id = entry.WingetId!;
            for (int attempt = 1; attempt <= MaxRetries + 1; attempt++)
            {
                Log($"[{entry.FriendlyName}] installing (attempt {attempt})...");
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
                        Log($"[{entry.FriendlyName}] OK");
                        return;
                    }
                    Log($"[{entry.FriendlyName}] exit code {p.ExitCode}");
                }
                catch (Exception ex)
                {
                    Log($"[{entry.FriendlyName}] error: {ex.Message}");
                }

                if (attempt <= MaxRetries)
                    await Task.Delay(3000);
            }

            Fail($"winget install failed for {entry.FriendlyName} after {MaxRetries + 1} attempts");
        }

        private async Task HandleManualInstallerAsync(AppEntry entry)
        {
            var name = entry.FriendlyName;
            var fileName = entry.ManualFileName!;
            var url = entry.ManualUrl!;

            if (string.IsNullOrWhiteSpace(url))
            {
                Fail($"{name} missing download URL");
                return;
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
                return;
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
                    return;
                }

                var exe = new DirectoryInfo(extractDir)
                    .GetFiles("*.exe", SearchOption.AllDirectories)
                    .OrderByDescending(f => f.Length)
                    .FirstOrDefault();
                if (exe == null)
                {
                    Fail($"No executable found inside {fileName} for {name}");
                    return;
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
                        return;
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
            }
            catch (Exception ex)
            {
                Fail($"Failed to launch installer for {name}: {ex.Message}");
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
