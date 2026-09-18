using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
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
            // Microsoft's own first-party monitoring/cleanup app. Confirmed on winget
            // as Microsoft.PCManager (still labelled Beta upstream as of writing, but
            // the package itself installs cleanly) - CPU/RAM/GPU monitoring, storage
            // cleanup and startup-app management overlap with the utilities above, but
            // it's opt-in and skippable like everything else in this list.
            new() { FriendlyName = "Microsoft PC Manager", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "Microsoft.PCManager" },
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

        // ---- gaming tweaks: disclosed before they happen, reversible after ----
        // Set once ApplyTweaks() actually runs (i.e. the user said yes at the
        // wizard's "Apply these tweaks?" prompt) - the summary screen reads these
        // rather than assuming tweaks always ran, which is exactly the bug this
        // whole section exists to fix (see ROADMAP.md's disclosed/reversible rule).
        public bool TweaksApplied { get; private set; }
        public string? TweaksRevertFilePath { get; private set; }

        /// <summary>
        /// One tweak GamingStack can apply, with its real current value and what it
        /// would change to - built for the wizard's consent screen so nothing is a
        /// surprise. Read-only: calling this never changes anything on the machine.
        /// </summary>
        public readonly record struct TweakPreview(string Name, string Current, string NewValue);

        /// <summary>
        /// Which power plan the "Power plan" tweak targets - answered once on the
        /// wizard's ChoosePowerPlan screen, right before the tweaks it feeds into
        /// PreviewTweaks/ApplyTweaks so the preview always matches what will actually
        /// run. Ultimate is the hidden, more aggressive plan Microsoft ships but
        /// doesn't surface in Settings by default.
        /// </summary>
        public enum PowerPlanChoice { High, Ultimate }

        // Microsoft's own fixed template GUID for the hidden "Ultimate Performance"
        // plan - documented in Microsoft's own Tech Community post introducing it.
        // `powercfg -duplicatescheme` against this GUID creates a real, visible,
        // switchable copy of it (Windows doesn't let you activate the hidden
        // template directly on modern builds - it has to be duplicated first).
        private const string UltimatePerformanceTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        public static List<TweakPreview> PreviewTweaks(PowerPlanChoice powerPlanChoice)
        {
            var list = new List<TweakPreview>();

            string gameMode;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\GameBar");
                var val = key?.GetValue("AllowAutoGameMode");
                gameMode = val == null ? "not set (Windows default)" : (Convert.ToInt32(val) == 1 ? "enabled" : "disabled");
            }
            catch { gameMode = "unknown"; }
            list.Add(new TweakPreview("Game Mode", gameMode, "enabled"));

            string hags;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                var val = key?.GetValue("HwSchMode");
                hags = val == null ? "not set (Windows default: off)" : (Convert.ToInt32(val) == 2 ? "on" : "off");
            }
            catch { hags = "unknown"; }
            list.Add(new TweakPreview("Hardware-accelerated GPU scheduling", hags, "on"));

            var (_, planName) = GetActivePowerScheme();
            var powerPlanTarget = powerPlanChoice == PowerPlanChoice.Ultimate ? "Ultimate Performance" : "High performance";
            list.Add(new TweakPreview("Power plan", planName ?? "unknown", powerPlanTarget));

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                var val = key?.GetValue("HideFileExt");
                var current = val == null ? "hidden (Windows default)" : (Convert.ToInt32(val) == 0 ? "shown" : "hidden");
                list.Add(new TweakPreview("File extensions", current, "shown"));
            }
            catch { list.Add(new TweakPreview("File extensions", "unknown", "shown")); }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                var val = key?.GetValue("Hidden");
                var current = val == null ? "hidden (Windows default)" : (Convert.ToInt32(val) == 1 ? "shown" : "hidden");
                list.Add(new TweakPreview("Hidden files", current, "shown"));
            }
            catch { list.Add(new TweakPreview("Hidden files", "unknown", "shown")); }

            try
            {
                var exists = Registry.CurrentUser.OpenSubKey(ClassicContextMenuKeyPath) != null;
                list.Add(new TweakPreview("Right-click context menu", exists ? "already classic" : "modern (Windows 11 default)", "classic (full menu, no \"Show more options\")"));
            }
            catch { list.Add(new TweakPreview("Right-click context menu", "unknown", "classic")); }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                var val = key?.GetValue("TaskbarDa");
                var current = val == null ? "shown (Windows default)" : (Convert.ToInt32(val) == 0 ? "hidden" : "shown");
                list.Add(new TweakPreview("Taskbar widgets button", current, "hidden"));
            }
            catch { list.Add(new TweakPreview("Taskbar widgets button", "unknown", "hidden")); }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search");
                var val = key?.GetValue("SearchboxTaskbarMode");
                var current = val == null ? "shown (Windows default)" : (Convert.ToInt32(val) == 0 ? "hidden" : "shown");
                list.Add(new TweakPreview("Taskbar search box", current, "hidden"));
            }
            catch { list.Add(new TweakPreview("Taskbar search box", "unknown", "hidden")); }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                var val = key?.GetValue("ShowCopilotButton");
                var current = val == null ? "shown (Windows default)" : (Convert.ToInt32(val) == 0 ? "hidden" : "shown");
                list.Add(new TweakPreview("Taskbar Copilot button", current, "hidden"));
            }
            catch { list.Add(new TweakPreview("Taskbar Copilot button", "unknown", "hidden")); }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                var val = key?.GetValue("SystemResponsiveness");
                var current = val == null ? "20% (Windows default)" : $"{Convert.ToInt32(val)}%";
                list.Add(new TweakPreview("Background task CPU reservation (MMCSS)", current, "0% (games get full priority)"));
            }
            catch { list.Add(new TweakPreview("Background task CPU reservation (MMCSS)", "unknown", "0%")); }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager");
                var suggestions = key?.GetValue("SystemPaneSuggestionsEnabled");
                var ads = key?.GetValue("SubscribedContent-338388Enabled");
                var current = (suggestions == null && ads == null)
                    ? "shown (Windows default)"
                    : ((suggestions == null || Convert.ToInt32(suggestions) == 1) || (ads == null || Convert.ToInt32(ads) == 1) ? "shown" : "hidden");
                list.Add(new TweakPreview("Start menu suggestions/ads", current, "hidden"));
            }
            catch { list.Add(new TweakPreview("Start menu suggestions/ads", "unknown", "hidden")); }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
                var val = key?.GetValue("HiberbootEnabled");
                var current = val == null ? "on (Windows default)" : (Convert.ToInt32(val) == 1 ? "on" : "off");
                list.Add(new TweakPreview("Fast Startup", current, "off"));
            }
            catch { list.Add(new TweakPreview("Fast Startup", "unknown", "off")); }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings");
                var start = key?.GetValue("ActiveHoursStart");
                var end = key?.GetValue("ActiveHoursEnd");
                var current = (start == null && end == null)
                    ? "08:00-17:00 (Windows default)"
                    : $"{Convert.ToInt32(start ?? 8):D2}:00-{Convert.ToInt32(end ?? 17):D2}:00";
                list.Add(new TweakPreview("Windows Update active hours", current, "16:00-23:00 (typical evening gaming window)"));
            }
            catch { list.Add(new TweakPreview("Windows Update active hours", "unknown", "16:00-23:00")); }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
                var val = key?.GetValue("GameDVR_Enabled");
                var current = val == null ? "enabled (Windows default)" : (Convert.ToInt32(val) == 1 ? "enabled" : "disabled");
                list.Add(new TweakPreview("Xbox Game Bar / Game DVR", current, "disabled"));
            }
            catch { list.Add(new TweakPreview("Xbox Game Bar / Game DVR", "unknown", "disabled")); }

            try
            {
                var ifacePath = GetActiveNetworkInterfaceRegistryPath();
                if (ifacePath == null)
                {
                    list.Add(new TweakPreview("Network latency (Nagle's algorithm)", "no active network adapter found", "disabled"));
                }
                else
                {
                    using var key = Registry.LocalMachine.OpenSubKey(ifacePath);
                    var ack = key?.GetValue("TcpAckFrequency");
                    var noDelay = key?.GetValue("TCPNoDelay");
                    var current = (ack == null && noDelay == null)
                        ? "not set (Windows default, Nagle's algorithm enabled)"
                        : (Convert.ToInt32(ack) == 1 && Convert.ToInt32(noDelay) == 1 ? "disabled" : "partially set");
                    list.Add(new TweakPreview("Network latency (Nagle's algorithm)", current, "disabled"));
                }
            }
            catch { list.Add(new TweakPreview("Network latency (Nagle's algorithm)", "unknown", "disabled")); }

            return list;
        }

        /// <summary>
        /// Finds the registry path for the network interface actually in use right
        /// now (has IP + a default gateway) via WMI, since Nagle's algorithm is
        /// controlled per-interface under `Tcpip\Parameters\Interfaces\{GUID}`, not
        /// globally. Returns null if no active adapter can be identified.
        /// </summary>
        private static string? GetActiveNetworkInterfaceRegistryPath()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT SettingID FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True AND DefaultIPGateway IS NOT NULL");
                foreach (var obj in searcher.Get())
                {
                    var settingId = obj["SettingID"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(settingId))
                        return $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{settingId}";
                }
            }
            catch { /* falls through to null below */ }
            return null;
        }

        // CLSID that, when registered with a blank InprocServer32 default value, tells
        // Explorer to fall back to the classic (pre-Windows 11) right-click context
        // menu - the full list up front instead of needing "Show more options". This
        // is a well-documented, widely-used Explorer shell extension override, not an
        // undocumented hack; deleting the key (see ApplyTweaks) reverts it cleanly.
        private const string ClassicContextMenuKeyPath =
            @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

        /// <summary>
        /// Reads the currently active power plan via `powercfg /getactivescheme`,
        /// e.g. "Power Scheme GUID: 381b4222-...  (Balanced)". Needed both for the
        /// consent-screen preview (friendly name) and the revert script (the real
        /// GUID, since "Balanced" alone isn't something powercfg can switch back to
        /// if the user has a custom plan with a name that happens to collide).
        /// </summary>
        private static (string? Guid, string? Name) GetActivePowerScheme()
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return (null, null);
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);

                var guidMatch = System.Text.RegularExpressions.Regex.Match(output,
                    @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                var nameMatch = System.Text.RegularExpressions.Regex.Match(output, @"\(([^)]+)\)");
                return (guidMatch.Success ? guidMatch.Value : null, nameMatch.Success ? nameMatch.Groups[1].Value : null);
            }
            catch
            {
                return (null, null);
            }
        }

        /// <summary>
        /// Scans `powercfg /list` for a scheme whose friendly name matches exactly,
        /// returning its real GUID - used to find a previously-duplicated "Ultimate
        /// Performance" plan so re-running the tweak doesn't create a fresh duplicate
        /// every time.
        /// </summary>
        private static string? FindPowerSchemeGuidByName(string name)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/list")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);

                foreach (var line in output.Split('\n'))
                {
                    if (!line.Contains($"({name})")) continue;
                    var guidMatch = System.Text.RegularExpressions.Regex.Match(line,
                        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                    if (guidMatch.Success) return guidMatch.Value;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Runs `powercfg -duplicatescheme &lt;templateGuid&gt;`, which creates a new,
        /// real, switchable power plan copied from a template (visible or hidden) and
        /// returns its freshly-assigned GUID from the command's own output.
        /// </summary>
        private static string? DuplicatePowerScheme(string templateGuid)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", $"-duplicatescheme {templateGuid}")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);

                var guidMatch = System.Text.RegularExpressions.Regex.Match(output,
                    @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                return guidMatch.Success ? guidMatch.Value : null;
            }
            catch
            {
                return null;
            }
        }

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
        /// <paramref name="applyTweaks"/> is the user's explicit answer to the
        /// wizard's "Apply these tweaks?" prompt (see <see cref="PreviewTweaks"/>) -
        /// this method never decides that for itself, same principle as the app
        /// selection above it.
        /// </summary>
        public async Task RunAsync(IReadOnlyList<AppEntry> selected, bool applyTweaks, PowerPlanChoice powerPlanChoice)
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
                }
                else
                {
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
                }

                Log("");
                if (applyTweaks)
                {
                    Log("Applying tweaks (you said yes to this)...");
                    ApplyTweaks(powerPlanChoice);
                }
                else
                {
                    Log("Tweaks skipped - you said no.");
                }

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

        /// <summary>
        /// Only ever called after the user has explicitly agreed to it (see
        /// <see cref="RunAsync"/>'s <c>applyTweaks</c> parameter and the wizard's
        /// "Apply these tweaks?" prompt, built from <see cref="PreviewTweaks"/>).
        /// Records each setting's real previous value before changing it, and writes
        /// them out as a plain, human-readable .cmd script that puts everything back
        /// exactly as it was - the "reversible" half of the disclosed/reversible rule
        /// in ROADMAP.md. The script is regenerated every run, so it always reflects
        /// what *this* run actually changed.
        /// </summary>
        private void ApplyTweaks(PowerPlanChoice powerPlanChoice)
        {
            var revertLines = new List<string>
            {
                "@echo off",
                $"REM GamingStack tweak revert - generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                "REM Restores the settings GamingStack changed after you said yes to \"Apply these",
                "REM tweaks?\". Right-click this file and choose \"Run as administrator\" - the",
                "REM registry lines below need elevation, same as GamingStack itself did.",
                ""
            };

            try
            {
                using var readKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\GameBar");
                var existing = readKey?.GetValue("AllowAutoGameMode");

                revertLines.Add("REM Game Mode");
                revertLines.Add(existing == null
                    ? @"reg delete ""HKLM\SOFTWARE\Microsoft\GameBar"" /v AllowAutoGameMode /f"
                    : $@"reg add ""HKLM\SOFTWARE\Microsoft\GameBar"" /v AllowAutoGameMode /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                revertLines.Add("");

                using var writeKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\GameBar");
                writeKey?.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
                var was = existing == null ? "not set" : (Convert.ToInt32(existing) == 1 ? "enabled" : "disabled");
                Log($"[tweaks] Game Mode: enabled (was: {was})");
            }
            catch (Exception ex)
            {
                Fail($"Game Mode tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                var existing = readKey?.GetValue("HwSchMode");

                revertLines.Add("REM Hardware-accelerated GPU scheduling");
                revertLines.Add(existing == null
                    ? @"reg delete ""HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"" /v HwSchMode /f"
                    : $@"reg add ""HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"" /v HwSchMode /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                revertLines.Add("");

                using var writeKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                writeKey?.SetValue("HwSchMode", 2, RegistryValueKind.DWord);
                var was = existing == null ? "not set" : (Convert.ToInt32(existing) == 2 ? "on" : "off");
                Log($"[tweaks] Hardware-accelerated GPU scheduling: enabled (was: {was})");
            }
            catch (Exception ex)
            {
                Fail($"HAGS tweak failed: {ex.Message}");
            }

            try
            {
                var (previousGuid, previousName) = GetActivePowerScheme();

                revertLines.Add("REM Power plan");
                revertLines.Add(previousGuid != null
                    ? $"powercfg -setactive {previousGuid}"
                    : "REM (couldn't read the previous power plan - nothing to restore here)");
                revertLines.Add("");

                if (powerPlanChoice == PowerPlanChoice.Ultimate)
                {
                    // Windows doesn't let modern builds activate the hidden Ultimate
                    // Performance template GUID directly - it has to be duplicated
                    // into a real, visible plan first. Reuse an existing duplicate if
                    // one's already there from a previous run, rather than creating a
                    // fresh (and identical) copy every single time.
                    var existingGuid = FindPowerSchemeGuidByName("Ultimate Performance");
                    var targetGuid = existingGuid ?? DuplicatePowerScheme(UltimatePerformanceTemplateGuid);

                    if (targetGuid != null)
                    {
                        var psi = new ProcessStartInfo("powercfg", $"-setactive {targetGuid}")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var p = Process.Start(psi);
                        p?.WaitForExit(5000);
                        Log($"[tweaks] Power plan: Ultimate Performance (was: {previousName ?? "unknown"})");
                    }
                    else
                    {
                        Fail("Power plan tweak failed: couldn't create the Ultimate Performance plan");
                    }
                }
                else
                {
                    // SCHEME_MIN is the built-in alias for the "High performance" power plan.
                    var psi = new ProcessStartInfo("powercfg", "-setactive SCHEME_MIN")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(5000);
                    Log($"[tweaks] Power plan: High performance (was: {previousName ?? "unknown"})");
                }
            }
            catch (Exception ex)
            {
                Fail($"Power plan tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                var existing = readKey?.GetValue("HideFileExt");

                revertLines.Add("REM File extensions");
                revertLines.Add(existing == null
                    ? @"reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v HideFileExt /f"
                    : $@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v HideFileExt /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                revertLines.Add("");

                using var writeKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                writeKey?.SetValue("HideFileExt", 0, RegistryValueKind.DWord);
                var was = existing == null ? "hidden (default)" : (Convert.ToInt32(existing) == 0 ? "shown" : "hidden");
                Log($"[tweaks] File extensions: shown (was: {was})");
            }
            catch (Exception ex)
            {
                Fail($"File extensions tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                var existing = readKey?.GetValue("Hidden");

                revertLines.Add("REM Hidden files");
                revertLines.Add(existing == null
                    ? @"reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v Hidden /f"
                    : $@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v Hidden /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                revertLines.Add("");

                using var writeKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                writeKey?.SetValue("Hidden", 1, RegistryValueKind.DWord);
                var was = existing == null ? "hidden (default)" : (Convert.ToInt32(existing) == 1 ? "shown" : "hidden");
                Log($"[tweaks] Hidden files: shown (was: {was})");
            }
            catch (Exception ex)
            {
                Fail($"Hidden files tweak failed: {ex.Message}");
            }

            try
            {
                var alreadyExisted = Registry.CurrentUser.OpenSubKey(ClassicContextMenuKeyPath) != null;

                revertLines.Add("REM Right-click context menu");
                revertLines.Add(alreadyExisted
                    ? "REM (already classic before this run - nothing to restore here)"
                    : @"reg delete ""HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}"" /f");
                revertLines.Add("");

                using var writeKey = Registry.CurrentUser.CreateSubKey(ClassicContextMenuKeyPath);
                writeKey?.SetValue(string.Empty, string.Empty, RegistryValueKind.String);
                Log($"[tweaks] Right-click context menu: classic (was: {(alreadyExisted ? "already classic" : "modern")}) - takes effect after Explorer restarts or you sign in again");
            }
            catch (Exception ex)
            {
                Fail($"Classic context menu tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                var existing = readKey?.GetValue("TaskbarDa");

                revertLines.Add("REM Taskbar widgets button");
                revertLines.Add(existing == null
                    ? @"reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v TaskbarDa /f"
                    : $@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v TaskbarDa /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                revertLines.Add("");

                using var writeKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                writeKey?.SetValue("TaskbarDa", 0, RegistryValueKind.DWord);
                var was = existing == null ? "shown (default)" : (Convert.ToInt32(existing) == 0 ? "hidden" : "shown");
                Log($"[tweaks] Taskbar widgets button: hidden (was: {was})");
            }
            catch (Exception ex)
            {
                Fail($"Taskbar widgets tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search");
                var existing = readKey?.GetValue("SearchboxTaskbarMode");

                revertLines.Add("REM Taskbar search box");
                revertLines.Add(existing == null
                    ? @"reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Search"" /v SearchboxTaskbarMode /f"
                    : $@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Search"" /v SearchboxTaskbarMode /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                revertLines.Add("");

                using var writeKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search");
                writeKey?.SetValue("SearchboxTaskbarMode", 0, RegistryValueKind.DWord);
                var was = existing == null ? "shown (default)" : (Convert.ToInt32(existing) == 0 ? "hidden" : "shown");
                Log($"[tweaks] Taskbar search box: hidden (was: {was})");
            }
            catch (Exception ex)
            {
                Fail($"Taskbar search box tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                var existing = readKey?.GetValue("ShowCopilotButton");

                revertLines.Add("REM Taskbar Copilot button");
                revertLines.Add(existing == null
                    ? @"reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v ShowCopilotButton /f"
                    : $@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v ShowCopilotButton /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                revertLines.Add("");

                using var writeKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                writeKey?.SetValue("ShowCopilotButton", 0, RegistryValueKind.DWord);
                var was = existing == null ? "shown (default)" : (Convert.ToInt32(existing) == 0 ? "hidden" : "shown");
                Log($"[tweaks] Taskbar Copilot button: hidden (was: {was})");
            }
            catch (Exception ex)
            {
                Fail($"Taskbar Copilot button tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                var existing = readKey?.GetValue("SystemResponsiveness");

                revertLines.Add("REM Background task CPU reservation (MMCSS)");
                revertLines.Add(existing == null
                    ? @"reg delete ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v SystemResponsiveness /f"
                    : $@"reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v SystemResponsiveness /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                revertLines.Add("");

                using var writeKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                writeKey?.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                var was = existing == null ? "20% (default)" : $"{Convert.ToInt32(existing)}%";
                Log($"[tweaks] Background task CPU reservation (MMCSS): 0% (was: {was})");
            }
            catch (Exception ex)
            {
                Fail($"MMCSS tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager");
                var existingSuggestions = readKey?.GetValue("SystemPaneSuggestionsEnabled");
                var existingAds = readKey?.GetValue("SubscribedContent-338388Enabled");

                revertLines.Add("REM Start menu suggestions/ads");
                revertLines.Add(existingSuggestions == null
                    ? @"reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"" /v SystemPaneSuggestionsEnabled /f"
                    : $@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"" /v SystemPaneSuggestionsEnabled /t REG_DWORD /d {Convert.ToInt32(existingSuggestions)} /f");
                revertLines.Add(existingAds == null
                    ? @"reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"" /v SubscribedContent-338388Enabled /f"
                    : $@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"" /v SubscribedContent-338388Enabled /t REG_DWORD /d {Convert.ToInt32(existingAds)} /f");
                revertLines.Add("");

                using var writeKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager");
                writeKey?.SetValue("SystemPaneSuggestionsEnabled", 0, RegistryValueKind.DWord);
                writeKey?.SetValue("SubscribedContent-338388Enabled", 0, RegistryValueKind.DWord);
                Log("[tweaks] Start menu suggestions/ads: hidden");
            }
            catch (Exception ex)
            {
                Fail($"Start menu suggestions tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
                var existing = readKey?.GetValue("HiberbootEnabled");

                revertLines.Add("REM Fast Startup");
                revertLines.Add(existing == null
                    ? @"reg delete ""HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power"" /v HiberbootEnabled /f"
                    : $@"reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power"" /v HiberbootEnabled /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                revertLines.Add("");

                using var writeKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
                writeKey?.SetValue("HiberbootEnabled", 0, RegistryValueKind.DWord);
                var was = existing == null ? "on (default)" : (Convert.ToInt32(existing) == 1 ? "on" : "off");
                Log($"[tweaks] Fast Startup: off (was: {was})");
            }
            catch (Exception ex)
            {
                Fail($"Fast Startup tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings");
                var existingStart = readKey?.GetValue("ActiveHoursStart");
                var existingEnd = readKey?.GetValue("ActiveHoursEnd");

                revertLines.Add("REM Windows Update active hours");
                revertLines.Add(existingStart == null
                    ? @"reg delete ""HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings"" /v ActiveHoursStart /f"
                    : $@"reg add ""HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings"" /v ActiveHoursStart /t REG_DWORD /d {Convert.ToInt32(existingStart)} /f");
                revertLines.Add(existingEnd == null
                    ? @"reg delete ""HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings"" /v ActiveHoursEnd /f"
                    : $@"reg add ""HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings"" /v ActiveHoursEnd /t REG_DWORD /d {Convert.ToInt32(existingEnd)} /f");
                revertLines.Add("");

                using var writeKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings");
                writeKey?.SetValue("ActiveHoursStart", 16, RegistryValueKind.DWord);
                writeKey?.SetValue("ActiveHoursEnd", 23, RegistryValueKind.DWord);
                var was = existingStart == null ? "08:00-17:00 (default)" : $"{Convert.ToInt32(existingStart):D2}:00-{Convert.ToInt32(existingEnd ?? 17):D2}:00";
                Log($"[tweaks] Windows Update active hours: 16:00-23:00 (was: {was})");
            }
            catch (Exception ex)
            {
                Fail($"Windows Update active hours tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
                var existing = readKey?.GetValue("GameDVR_Enabled");

                revertLines.Add("REM Xbox Game Bar / Game DVR");
                revertLines.Add(existing == null
                    ? @"reg delete ""HKCU\System\GameConfigStore"" /v GameDVR_Enabled /f"
                    : $@"reg add ""HKCU\System\GameConfigStore"" /v GameDVR_Enabled /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                revertLines.Add("");

                using var writeKey = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");
                writeKey?.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                var was = existing == null ? "enabled (default)" : (Convert.ToInt32(existing) == 1 ? "enabled" : "disabled");
                Log($"[tweaks] Xbox Game Bar / Game DVR: disabled (was: {was})");
            }
            catch (Exception ex)
            {
                Fail($"Game DVR tweak failed: {ex.Message}");
            }

            try
            {
                var ifacePath = GetActiveNetworkInterfaceRegistryPath();
                if (ifacePath == null)
                {
                    revertLines.Add("REM Network latency (Nagle's algorithm) - no active adapter found, nothing changed");
                    revertLines.Add("");
                    Log("[tweaks] Network latency (Nagle's algorithm): skipped - no active network adapter found");
                }
                else
                {
                    using var readKey = Registry.LocalMachine.OpenSubKey(ifacePath);
                    var existingAck = readKey?.GetValue("TcpAckFrequency");
                    var existingNoDelay = readKey?.GetValue("TCPNoDelay");

                    revertLines.Add("REM Network latency (Nagle's algorithm)");
                    revertLines.Add(existingAck == null
                        ? $@"reg delete ""HKLM\{ifacePath}"" /v TcpAckFrequency /f"
                        : $@"reg add ""HKLM\{ifacePath}"" /v TcpAckFrequency /t REG_DWORD /d {Convert.ToInt32(existingAck)} /f");
                    revertLines.Add(existingNoDelay == null
                        ? $@"reg delete ""HKLM\{ifacePath}"" /v TCPNoDelay /f"
                        : $@"reg add ""HKLM\{ifacePath}"" /v TCPNoDelay /t REG_DWORD /d {Convert.ToInt32(existingNoDelay)} /f");
                    revertLines.Add("");

                    using var writeKey = Registry.LocalMachine.CreateSubKey(ifacePath);
                    writeKey?.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                    writeKey?.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                    Log("[tweaks] Network latency (Nagle's algorithm): disabled (was: not set/enabled)");
                }
            }
            catch (Exception ex)
            {
                Fail($"Network latency tweak failed: {ex.Message}");
            }

            try
            {
                Directory.CreateDirectory(_workDir);
                TweaksRevertFilePath = Path.Combine(_workDir, "revert-tweaks.cmd");
                File.WriteAllLines(TweaksRevertFilePath, revertLines);
                Log($"[tweaks] To undo these, run (as Administrator): {TweaksRevertFilePath}");
            }
            catch (Exception ex)
            {
                Fail($"Couldn't write the tweak revert script: {ex.Message}");
            }

            TweaksApplied = true;
        }
    }
}
