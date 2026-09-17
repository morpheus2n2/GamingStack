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
    /// <summary>
    /// The actual install stack, ported directly from the earlier PowerShell version -
    /// same behavior (retries, manual-installer fallback chain, tweaks), just written
    /// as C# so it runs in-process instead of shelling out to a script.
    /// Fires <see cref="OnLog"/> for every line so the UI can print it into the
    /// terminal window without this class knowing anything about WinForms.
    /// </summary>
    public class InstallerEngine
    {
        public event Action<string>? OnLog;
        public event Action? OnFinished;

        private static readonly HttpClient Http = new();

        // winget package IDs to install - edit this list to customise.
        private static readonly string[] AppsToInstall =
        {
            // Launchers / core
            "Discord.Discord",
            "Valve.Steam",
            "Ubisoft.Connect",
            "EpicGames.EpicGamesLauncher",
            "GOG.Galaxy",
            "Amazon.Games",
            "Blizzard.BattleNet",
            "Playnite.Playnite",              // unifies all the above into one library

            // GPU / drivers / monitoring
            "Nvidia.App",
            "REALiX.HWiNFO",
            "CPUID.CPU-Z",
            "Piriform.Speccy",
            "CrystalDewWorld.CrystalDiskInfo",
            "BitSum.ProcessLasso",

            // Streaming / recording / audio
            "Streamlabs.StreamlabsOBS",
            "OBSProject.OBSStudio",
            "Medal.Medal",
            "VB-Audio.Voicemeeter.Banana",

            // General utility
            "Microsoft.VisualStudioCode",
            "Microsoft.PowerShell",
            "Microsoft.Office",
            "Microsoft.PowerToys",
            "Python.Python.3",
            "VideoLAN.VLC",
            "7zip.7zip",
            "NexusMods.Vortex"
        };

        // Apps with no reliable winget package - downloaded and installed manually.
        // (MSI Afterburner is deliberately not here - it isn't reliably available via
        // winget and there's no stable direct-download URL to hardcode safely.)
        private static readonly (string Name, string FileName, string Url)[] ManualInstallers =
        {
            ("Hyte Nexus", "HyteNexusInstaller.exe", "https://hyte.co/nexus-download"),
            ("L-Connect 3", "LConnect3.zip", "https://lianli-update-2025.lianli-cn.com/L3_CX/20260422-L-Connect%203-x64-v2.1.20-fde9a570.zip")
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

        public async Task RunAsync()
        {
            try
            {
                Directory.CreateDirectory(_workDir);
                try { File.Delete(FailFile); } catch { /* fine if it didn't exist */ }

                Log("GamingStack Installer");
                Log("");

                if (!IsWingetAvailable())
                {
                    Log("ERROR: winget not found.");
                    Fail("winget not found - install 'App Installer' from the Microsoft Store and re-run.");
                    OnFinished?.Invoke();
                    return;
                }

                Log($"Installing {AppsToInstall.Length} apps via winget...");
                Log("");
                foreach (var id in AppsToInstall)
                    await InstallWingetAppAsync(id);

                Log("");
                Log("Handling manual installers...");
                foreach (var (name, fileName, url) in ManualInstallers)
                    await HandleManualInstallerAsync(name, fileName, url);

                Log("");
                Log("Applying gaming tweaks...");
                ApplyTweaks();

                Log("");
                if (File.Exists(FailFile))
                    Log($"Done, with a few things needing manual attention - see {FailFile}");
                else
                    Log("Done. GamingStack is ready - go install your games and enjoy.");
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

        private async Task InstallWingetAppAsync(string id)
        {
            for (int attempt = 1; attempt <= MaxRetries + 1; attempt++)
            {
                Log($"[{id}] installing (attempt {attempt})...");
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
                        Log($"[{id}] OK");
                        return;
                    }
                    Log($"[{id}] exit code {p.ExitCode}");
                }
                catch (Exception ex)
                {
                    Log($"[{id}] error: {ex.Message}");
                }

                if (attempt <= MaxRetries)
                    await Task.Delay(3000);
            }

            Fail($"winget install failed for {id} after {MaxRetries + 1} attempts");
        }

        private async Task HandleManualInstallerAsync(string name, string fileName, string url)
        {
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
