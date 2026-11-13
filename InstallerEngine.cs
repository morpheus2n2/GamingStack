using System.Diagnostics;
using System.IO.Compression;
using System.Management;
using Microsoft.Win32;

namespace GamingStackGUI
{
    public enum AppKind { Winget, Manual, Bundle, Link }

    /// <summary>
    /// Which kind of PC is being set up - picked on the first wizard screen. It decides
    /// which catalog entries are offered (<see cref="AppEntry.Profiles"/>) and which
    /// tweaks are shown for consent (gaming-only tweaks like HAGS, core parking and
    /// mouse acceleration are never offered on an Office or Developer PC).
    /// Flags so one catalog entry can belong to several profiles.
    /// </summary>
    [Flags]
    public enum SetupProfile
    {
        None = 0,
        Gaming = 1,
        Office = 2,
        Developer = 4,
        All = Gaming | Office | Developer
    }

    public static class SetupProfileInfo
    {
        public static string Title(SetupProfile p) => p switch
        {
            SetupProfile.Office => "Office computer",
            SetupProfile.Developer => "Developer / home lab",
            _ => "Gaming / streaming PC"
        };
    }

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
        // The vendor's own download page. Link entries (no winget package, no stable direct
        // download) just open this in your default browser; for Manual entries it is the
        // fallback if the automatic download fails. Always https.
        public string? PageUrl { get; init; }
        // Bundle kind only: a handful of winget IDs installed back-to-back under one
        // catalog entry (e.g. every VC++ Redistributable version), so the wizard shows
        // a single friendly line instead of a dozen near-identical ones.
        public string[]? BundleWingetIds { get; init; }
        // Which setup profiles offer this entry. Defaults to Gaming only, so an entry
        // that doesn't say otherwise never shows up on an Office/Developer PC.
        public SetupProfile Profiles { get; init; } = SetupProfile.Gaming;
    }

    /// <summary>
    /// What happened to one catalog entry, once. Fired via
    /// <see cref="InstallerEngine.OnItemResult"/> alongside the free-text log so the
    /// UI can build a structured end-of-run summary (grouped by category, like the
    /// wizard's own list) instead of parsing human-readable log lines.
    /// </summary>
    public enum InstallResultKind { Installed, Failed, Skipped, Opened }

    public readonly record struct ItemResult(AppEntry Entry, InstallResultKind Kind, string? Detail = null);

    /// <summary>
    /// Which public DNS provider the optional "DNS servers" tweak should switch to.
    /// Skip means "leave my DNS exactly as it is" and is the safe default, so a bug
    /// in wiring the ChooseDns screen in can never silently change anyone's DNS.
    /// </summary>
    public enum DnsChoice { Skip, Cloudflare, Google, Quad9 }

    /// <summary>
    /// Everything the user chose on the wizard's tweak-related screens, bundled so
    /// PreviewTweaks / ApplyTweaks / RunAsync all see the exact same answers.
    /// <see cref="DebloatIds"/> holds <see cref="DebloatItem.Id"/>s the user ticked
    /// on the debloat screen - empty means nothing gets removed.
    /// </summary>
    /// <summary>
    /// What the wizard knew about this run, written to the top of the log so the file on
    /// its own explains exactly what the user agreed to.
    /// </summary>
    public sealed record RunContext(string ProfileTitle, string BackupSummary);

    public sealed record TweakOptions(
        InstallerEngine.PowerPlanChoice PowerPlan,
        DnsChoice Dns,
        IReadOnlyCollection<string> DebloatIds,
        SetupProfile Profile = SetupProfile.Gaming);

    /// <summary>
    /// One preinstalled app the opt-in debloat screen can offer to remove.
    /// <see cref="PackageId"/> is the package family ID winget lists/uninstalls it
    /// by; <see cref="StoreProductId"/> is the Microsoft Store listing used in the
    /// revert script so the app can be put back.
    /// </summary>
    public sealed record DebloatItem(string Id, string Name, string Why, string PackageId, string StoreProductId);

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

        private static readonly HttpClient Http = CreateHttp();

        private static HttpClient CreateHttp()
        {
            // Default timeout is 100s with no sign of life; a stalled download should
            // fail clearly rather than hang the whole run. Some hosts also refuse
            // requests that have no User-Agent.
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"GamingStack/{typeof(InstallerEngine).Assembly.GetName().Version?.ToString(3) ?? "1"} (+Windows setup helper)");
            return client;
        }

        // The full catalog, for every profile. Each entry says which profiles offer it
        // (Profiles - defaults to Gaming only); the wizard shows CatalogFor(profile),
        // grouped under one heading per Category in the order categories first appear
        // here. Friendly names are what the user sees, everything else is what runs.
        //
        // Every winget ID below has been checked against a live `winget show` listing
        // (the 1.1.0 additions on 2026-11-13). Vendor-published apps (RGB/peripheral
        // control especially) change package IDs more than most, so if one starts
        // failing every time, re-check it first with `winget search`.
        private const SetupProfile Gm = SetupProfile.Gaming;
        private const SetupProfile Of = SetupProfile.Office;
        private const SetupProfile Dv = SetupProfile.Developer;
        private const SetupProfile Everyone = SetupProfile.All;

        public static readonly IReadOnlyList<AppEntry> Catalog = new List<AppEntry>
        {
            // ---- Browsers (every profile) ----
            new() { FriendlyName = "Firefox", Category = "Web Browsers", Kind = AppKind.Winget, WingetId = "Mozilla.Firefox", Profiles = Everyone },
            new() { FriendlyName = "Google Chrome", Category = "Web Browsers", Kind = AppKind.Winget, WingetId = "Google.Chrome", Profiles = Everyone },
            new() { FriendlyName = "Brave (privacy-focused browser)", Category = "Web Browsers", Kind = AppKind.Winget, WingetId = "Brave.Brave", Profiles = Everyone },

            // ---- Game launchers ----
            new() { FriendlyName = "Steam", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "Valve.Steam" },
            new() { FriendlyName = "Epic Games Launcher", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "EpicGames.EpicGamesLauncher" },
            new() { FriendlyName = "Ubisoft Connect", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "Ubisoft.Connect" },
            new() { FriendlyName = "EA app", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "ElectronicArts.EADesktop" },
            new() { FriendlyName = "GOG Galaxy", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "GOG.Galaxy" },
            new() { FriendlyName = "Battle.net", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "Blizzard.BattleNet" },
            new() { FriendlyName = "Amazon Games", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "Amazon.Games" },
            new() { FriendlyName = "Rockstar Games Launcher", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "RockstarGames.Launcher" },
            new() { FriendlyName = "itch.io app", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "ItchIo.Itch" },
            new() { FriendlyName = "Heroic (Epic/GOG/Amazon in one)", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "HeroicGamesLauncher.HeroicGamesLauncher" },
            // Microsoft Store listing ID (not a community winget ID) - same trick as ChatGPT below.
            new() { FriendlyName = "Xbox app (Game Pass)", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "9MV0B5HZVK9Z" },
            new() { FriendlyName = "Playnite (unifies the above)", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "Playnite.Playnite" },
            new() { FriendlyName = "NVIDIA GeForce NOW (cloud gaming)", Category = "Game Launchers", Kind = AppKind.Winget, WingetId = "Nvidia.GeForceNow", Profiles = Gm },

            // ---- Chat & meetings ----
            new() { FriendlyName = "Discord", Category = "Chat & Meetings", Kind = AppKind.Winget, WingetId = "Discord.Discord", Profiles = Gm | Dv },
            new() { FriendlyName = "Microsoft Teams", Category = "Chat & Meetings", Kind = AppKind.Winget, WingetId = "Microsoft.Teams", Profiles = Of },
            new() { FriendlyName = "Zoom", Category = "Chat & Meetings", Kind = AppKind.Winget, WingetId = "Zoom.Zoom", Profiles = Of },
            new() { FriendlyName = "Slack", Category = "Chat & Meetings", Kind = AppKind.Winget, WingetId = "SlackTechnologies.Slack", Profiles = Of | Dv },
            new() { FriendlyName = "TeamSpeak (voice chat)", Category = "Chat & Meetings", Kind = AppKind.Winget, WingetId = "TeamSpeakSystems.TeamSpeakClient", Profiles = Gm },
            new() { FriendlyName = "Mumble (voice chat)", Category = "Chat & Meetings", Kind = AppKind.Winget, WingetId = "Mumble.Mumble.Client", Profiles = Gm },
            new() { FriendlyName = "Signal (private messaging)", Category = "Chat & Meetings", Kind = AppKind.Winget, WingetId = "OpenWhisperSystems.Signal", Profiles = Of },
            new() { FriendlyName = "Telegram", Category = "Chat & Meetings", Kind = AppKind.Winget, WingetId = "Telegram.TelegramDesktop", Profiles = Of },

            // ---- Office & documents ----
            new() { FriendlyName = "Microsoft 365 Apps", Category = "Office & Documents", Kind = AppKind.Winget, WingetId = "Microsoft.Office", Profiles = Gm | Of },
            new() { FriendlyName = "LibreOffice (free alternative)", Category = "Office & Documents", Kind = AppKind.Winget, WingetId = "TheDocumentFoundation.LibreOffice", Profiles = Of },
            new() { FriendlyName = "Adobe Acrobat Reader (PDFs)", Category = "Office & Documents", Kind = AppKind.Winget, WingetId = "Adobe.Acrobat.Reader.64-bit", Profiles = Of },
            new() { FriendlyName = "Thunderbird (email)", Category = "Office & Documents", Kind = AppKind.Winget, WingetId = "Mozilla.Thunderbird", Profiles = Of },
            new() { FriendlyName = "Notepad++", Category = "Office & Documents", Kind = AppKind.Winget, WingetId = "Notepad++.Notepad++", Profiles = Of | Dv },
            new() { FriendlyName = "Obsidian (notes)", Category = "Office & Documents", Kind = AppKind.Winget, WingetId = "Obsidian.Obsidian", Profiles = Of | Dv },
            new() { FriendlyName = "Google Drive", Category = "Office & Documents", Kind = AppKind.Winget, WingetId = "Google.GoogleDrive", Profiles = Of },
            new() { FriendlyName = "Foxit PDF Reader", Category = "Office & Documents", Kind = AppKind.Winget, WingetId = "Foxit.FoxitReader", Profiles = Of },
            new() { FriendlyName = "PDFgear (free PDF editor)", Category = "Office & Documents", Kind = AppKind.Winget, WingetId = "PDFgear.PDFgear", Profiles = Of },
            new() { FriendlyName = "SumatraPDF (fast, light PDF reader)", Category = "Office & Documents", Kind = AppKind.Winget, WingetId = "SumatraPDF.SumatraPDF", Profiles = Of },
            new() { FriendlyName = "Notion (notes + docs)", Category = "Office & Documents", Kind = AppKind.Winget, WingetId = "Notion.Notion", Profiles = Of },
            new() { FriendlyName = "calibre (e-books)", Category = "Office & Documents", Kind = AppKind.Winget, WingetId = "calibre.calibre", Profiles = Of },
            new() { FriendlyName = "Dropbox", Category = "Cloud & Sync", Kind = AppKind.Winget, WingetId = "Dropbox.Dropbox", Profiles = Of },
            new() { FriendlyName = "Nextcloud Desktop", Category = "Cloud & Sync", Kind = AppKind.Winget, WingetId = "Nextcloud.NextcloudDesktop", Profiles = Of | Dv },

            // ---- GPU / drivers / monitoring / performance ----
            new() { FriendlyName = "NVIDIA App", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "Nvidia.App" },
            new() { FriendlyName = "Display Driver Uninstaller (clean GPU driver swaps)", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "Wagnardsoft.DisplayDriverUninstaller" },
            new() { FriendlyName = "HWiNFO", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "REALiX.HWiNFO" },
            new() { FriendlyName = "CPU-Z", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "CPUID.CPU-Z" },
            new() { FriendlyName = "GPU-Z", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "TechPowerUp.GPU-Z" },
            new() { FriendlyName = "Speccy", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "Piriform.Speccy" },
            new() { FriendlyName = "CrystalDiskInfo", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "CrystalDewWorld.CrystalDiskInfo" },
            new() { FriendlyName = "MSI Afterburner (GPU overclock + FPS overlay)", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "Guru3D.Afterburner" },
            new() { FriendlyName = "CapFrameX (FPS capture + analysis)", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "CXWorld.CapFrameX" },
            new() { FriendlyName = "OCCT (stress test + stability)", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "OCBase.OCCT.Personal" },
            new() { FriendlyName = "FanControl (custom fan curves)", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "Rem0o.FanControl" },
            new() { FriendlyName = "Process Lasso", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "BitSum.ProcessLasso" },
            new() { FriendlyName = "LibreHardwareMonitor (open-source sensors)", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "LibreHardwareMonitor.LibreHardwareMonitor", Profiles = Gm },
            new() { FriendlyName = "RivaTuner Statistics Server (FPS overlay engine)", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "Guru3D.RTSS", Profiles = Gm },
            // Microsoft's own first-party monitoring/cleanup app (labelled Beta upstream,
            // but the package installs cleanly).
            new() { FriendlyName = "Microsoft PC Manager", Category = "Monitoring & Performance", Kind = AppKind.Winget, WingetId = "Microsoft.PCManager" },
            // No winget package exists for Cortex - Razer only ships a direct download,
            // so it is a Manual entry using Razer's own stable short link.
            new() { FriendlyName = "Razer Cortex", Category = "Monitoring & Performance", Kind = AppKind.Manual,
                    ManualFileName = "RazerCortexInstaller.exe",
                    ManualUrl = "https://rzr.to/cortex-download", PageUrl = "https://www.razer.com/cortex" },

            // ---- Streaming / recording ----
            new() { FriendlyName = "OBS Studio", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "OBSProject.OBSStudio" },
            new() { FriendlyName = "Streamlabs Desktop", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "Streamlabs.StreamlabsOBS" },
            new() { FriendlyName = "Medal.tv", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "Medal.Medal" },
            new() { FriendlyName = "Voicemeeter Banana", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "VB-Audio.Voicemeeter.Banana" },
            new() { FriendlyName = "Elgato Stream Deck", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "Elgato.StreamDeck" },
            new() { FriendlyName = "Elgato Wave Link", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "Elgato.WaveLink" },
            // Self-hosted game-streaming host - pairs with the free Moonlight app on a
            // phone/TV/handheld to stream this PC's games around the house.
            new() { FriendlyName = "Sunshine (stream games to Moonlight)", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "LizardByte.Sunshine" },
            new() { FriendlyName = "Moonlight (play streamed PC games)", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "MoonlightGameStreamingProject.Moonlight" },
            new() { FriendlyName = "Parsec (low-latency remote play)", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "Parsec.Parsec" },
            new() { FriendlyName = "Voicemeeter Potato (advanced audio mixer)", Category = "Streaming & Recording", Kind = AppKind.Winget, WingetId = "VB-Audio.Voicemeeter.Potato", Profiles = Gm },

            // ---- Developer tools ----
            new() { FriendlyName = "Git", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "Git.Git", Profiles = Dv },
            new() { FriendlyName = "GitHub CLI", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "GitHub.cli", Profiles = Dv },
            new() { FriendlyName = "GitHub Desktop", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "GitHub.GitHubDesktop", Profiles = Dv },
            new() { FriendlyName = "Windows Terminal", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "Microsoft.WindowsTerminal", Profiles = Dv },
            new() { FriendlyName = "Visual Studio 2022 Community (large download)", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "Microsoft.VisualStudio.2022.Community", Profiles = Dv },
            new() { FriendlyName = "JetBrains Toolbox", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "JetBrains.Toolbox", Profiles = Dv },
            new() { FriendlyName = "Postman (API testing)", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "Postman.Postman", Profiles = Dv },
            new() { FriendlyName = "DBeaver (database client)", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "DBeaver.DBeaver.Community", Profiles = Dv },
            new() { FriendlyName = "Neovim (modal text editor)", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "Neovim.Neovim", Profiles = Dv },
            new() { FriendlyName = "Fork (Git client)", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "Fork.Fork", Profiles = Dv },
            new() { FriendlyName = "Insomnia (API client)", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "Insomnia.Insomnia", Profiles = Dv },
            new() { FriendlyName = "WinMerge (compare files and folders)", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "WinMerge.WinMerge", Profiles = Dv },
            new() { FriendlyName = "MobaXterm (SSH + remote tools)", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "Mobatek.MobaXterm", Profiles = Dv },
            new() { FriendlyName = "CMake", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "Kitware.CMake", Profiles = Dv },
            new() { FriendlyName = "Visual Studio 2022 Build Tools (C++/.NET compilers)", Category = "Developer Tools", Kind = AppKind.Winget, WingetId = "Microsoft.VisualStudio.2022.BuildTools", Profiles = Dv },

            // ---- Command-line tools ----
            new() { FriendlyName = "lazygit (Git in the terminal)", Category = "Command-line Tools", Kind = AppKind.Winget, WingetId = "JesseDuffield.lazygit", Profiles = Dv },
            new() { FriendlyName = "Starship (nicer shell prompt)", Category = "Command-line Tools", Kind = AppKind.Winget, WingetId = "Starship.Starship", Profiles = Dv },
            new() { FriendlyName = "ripgrep (very fast text search)", Category = "Command-line Tools", Kind = AppKind.Winget, WingetId = "BurntSushi.ripgrep.MSVC", Profiles = Dv },
            new() { FriendlyName = "fzf (fuzzy finder)", Category = "Command-line Tools", Kind = AppKind.Winget, WingetId = "junegunn.fzf", Profiles = Dv },
            new() { FriendlyName = "jq (read JSON in the terminal)", Category = "Command-line Tools", Kind = AppKind.Winget, WingetId = "jqlang.jq", Profiles = Dv },

            // ---- Databases ----
            new() { FriendlyName = "PostgreSQL 16", Category = "Databases", Kind = AppKind.Winget, WingetId = "PostgreSQL.PostgreSQL.16", Profiles = Dv },
            new() { FriendlyName = "MongoDB Compass", Category = "Databases", Kind = AppKind.Winget, WingetId = "MongoDB.Compass.Full", Profiles = Dv },
            new() { FriendlyName = "HeidiSQL", Category = "Databases", Kind = AppKind.Winget, WingetId = "HeidiSQL.HeidiSQL", Profiles = Dv },

            // ---- Languages & runtimes ----
            new() { FriendlyName = "Node.js (LTS)", Category = "Languages & Runtimes", Kind = AppKind.Winget, WingetId = "OpenJS.NodeJS.LTS", Profiles = Dv },
            new() { FriendlyName = ".NET SDK 8", Category = "Languages & Runtimes", Kind = AppKind.Winget, WingetId = "Microsoft.DotNet.SDK.8", Profiles = Dv },
            new() { FriendlyName = "Go", Category = "Languages & Runtimes", Kind = AppKind.Winget, WingetId = "GoLang.Go", Profiles = Dv },
            new() { FriendlyName = "Rust (rustup)", Category = "Languages & Runtimes", Kind = AppKind.Winget, WingetId = "Rustlang.Rustup", Profiles = Dv },
            new() { FriendlyName = "Java 21 (Temurin JDK)", Category = "Languages & Runtimes", Kind = AppKind.Winget, WingetId = "EclipseAdoptium.Temurin.21.JDK", Profiles = Dv },
            new() { FriendlyName = ".NET SDK 9", Category = "Languages & Runtimes", Kind = AppKind.Winget, WingetId = "Microsoft.DotNet.SDK.9", Profiles = Dv },

            // ---- Containers & cloud (Docker and WSL need virtualization switched on in
            // the BIOS/UEFI and usually a restart afterwards) ----
            new() { FriendlyName = "Docker Desktop", Category = "Containers & Cloud", Kind = AppKind.Winget, WingetId = "Docker.DockerDesktop", Profiles = Dv },
            new() { FriendlyName = "WSL (Windows Subsystem for Linux)", Category = "Containers & Cloud", Kind = AppKind.Winget, WingetId = "Microsoft.WSL", Profiles = Dv },
            new() { FriendlyName = "Azure CLI", Category = "Containers & Cloud", Kind = AppKind.Winget, WingetId = "Microsoft.AzureCLI", Profiles = Dv },
            new() { FriendlyName = "kubectl", Category = "Containers & Cloud", Kind = AppKind.Winget, WingetId = "Kubernetes.kubectl", Profiles = Dv },
            new() { FriendlyName = "Terraform", Category = "Containers & Cloud", Kind = AppKind.Winget, WingetId = "Hashicorp.Terraform", Profiles = Dv },
            new() { FriendlyName = "Helm (Kubernetes packages)", Category = "Containers & Cloud", Kind = AppKind.Winget, WingetId = "Helm.Helm", Profiles = Dv },
            new() { FriendlyName = "minikube (local Kubernetes)", Category = "Containers & Cloud", Kind = AppKind.Winget, WingetId = "Kubernetes.minikube", Profiles = Dv },
            new() { FriendlyName = "Bicep (Azure infrastructure)", Category = "Containers & Cloud", Kind = AppKind.Winget, WingetId = "Microsoft.Bicep", Profiles = Dv },
            new() { FriendlyName = "Vagrant", Category = "Containers & Cloud", Kind = AppKind.Winget, WingetId = "Hashicorp.Vagrant", Profiles = Dv },
            new() { FriendlyName = "Ubuntu 24.04 for WSL", Category = "Containers & Cloud", Kind = AppKind.Winget, WingetId = "Canonical.Ubuntu.2404", Profiles = Dv },

            // ---- Home lab & network ----
            new() { FriendlyName = "Tailscale (private network)", Category = "Home Lab & Network", Kind = AppKind.Winget, WingetId = "Tailscale.Tailscale", Profiles = Dv },
            new() { FriendlyName = "Wireshark", Category = "Home Lab & Network", Kind = AppKind.Winget, WingetId = "WiresharkFoundation.Wireshark", Profiles = Dv },
            new() { FriendlyName = "PuTTY (SSH)", Category = "Home Lab & Network", Kind = AppKind.Winget, WingetId = "PuTTY.PuTTY", Profiles = Dv },
            new() { FriendlyName = "WinSCP (file transfer)", Category = "Home Lab & Network", Kind = AppKind.Winget, WingetId = "WinSCP.WinSCP", Profiles = Dv },
            new() { FriendlyName = "Sysinternals Suite", Category = "Home Lab & Network", Kind = AppKind.Winget, WingetId = "Microsoft.Sysinternals.Suite", Profiles = Dv },
            new() { FriendlyName = "VirtualBox", Category = "Home Lab & Network", Kind = AppKind.Winget, WingetId = "Oracle.VirtualBox", Profiles = Dv },
            new() { FriendlyName = "Balena Etcher (flash USB/SD images)", Category = "Home Lab & Network", Kind = AppKind.Winget, WingetId = "Balena.Etcher", Profiles = Dv },
            new() { FriendlyName = "Rufus (bootable USB drives)", Category = "Home Lab & Network", Kind = AppKind.Winget, WingetId = "Rufus.Rufus", Profiles = Dv },
            new() { FriendlyName = "Ventoy (multi-boot USB)", Category = "Home Lab & Network", Kind = AppKind.Winget, WingetId = "Ventoy.Ventoy", Profiles = Dv },
            new() { FriendlyName = "WireGuard (VPN)", Category = "Home Lab & Network", Kind = AppKind.Winget, WingetId = "WireGuard.WireGuard", Profiles = Dv },
            new() { FriendlyName = "Nmap (network scanner)", Category = "Home Lab & Network", Kind = AppKind.Winget, WingetId = "Insecure.Nmap", Profiles = Dv },
            new() { FriendlyName = "Advanced IP Scanner", Category = "Home Lab & Network", Kind = AppKind.Winget, WingetId = "Famatech.AdvancedIPScanner", Profiles = Dv },

            // ---- General utilities ----
            new() { FriendlyName = "VS Code", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "Microsoft.VisualStudioCode", Profiles = Everyone },
            new() { FriendlyName = "PowerShell 7", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "Microsoft.PowerShell", Profiles = Gm | Dv },
            new() { FriendlyName = "Python 3", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "Python.Python.3", Profiles = Gm | Dv },
            new() { FriendlyName = "PowerToys", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "Microsoft.PowerToys", Profiles = Everyone },
            new() { FriendlyName = "VLC Media Player", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "VideoLAN.VLC", Profiles = Everyone },
            new() { FriendlyName = "7-Zip", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "7zip.7zip", Profiles = Everyone },
            new() { FriendlyName = "Bitwarden (password manager)", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "Bitwarden.Bitwarden", Profiles = Everyone },
            new() { FriendlyName = "Everything (instant file search)", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "voidtools.Everything", Profiles = Of | Dv },
            new() { FriendlyName = "WizTree (see what's filling a drive)", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "AntibodySoftware.WizTree", Profiles = Everyone },
            new() { FriendlyName = "ShareX (screenshots + screen recording)", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "ShareX.ShareX", Profiles = Everyone },
            new() { FriendlyName = "EarTrumpet (per-app volume mixer)", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "File-New-Project.EarTrumpet", Profiles = Everyone },
            new() { FriendlyName = "Ditto (clipboard history)", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "Ditto.Ditto", Profiles = Everyone },
            new() { FriendlyName = "AnyDesk (remote support)", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "AnyDesk.AnyDesk", Profiles = Of },
            new() { FriendlyName = "Spotify", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "Spotify.Spotify", Profiles = Gm | Of },
            new() { FriendlyName = "Malwarebytes (on-demand malware scanner)", Category = "Security & Privacy", Kind = AppKind.Winget, WingetId = "Malwarebytes.Malwarebytes", Profiles = Everyone },
            new() { FriendlyName = "Proton VPN", Category = "Security & Privacy", Kind = AppKind.Winget, WingetId = "Proton.ProtonVPN", Profiles = Of | Gm },
            new() { FriendlyName = "Paint.NET (image editor)", Category = "Creative", Kind = AppKind.Winget, WingetId = "dotPDN.PaintDotNet", Profiles = Of | Gm },
            new() { FriendlyName = "GIMP (advanced image editor)", Category = "Creative", Kind = AppKind.Winget, WingetId = "GIMP.GIMP.3", Profiles = Of | Gm },
            new() { FriendlyName = "Inkscape (vector graphics)", Category = "Creative", Kind = AppKind.Winget, WingetId = "Inkscape.Inkscape", Profiles = Of },
            new() { FriendlyName = "HandBrake (video converter)", Category = "Creative", Kind = AppKind.Winget, WingetId = "HandBrake.HandBrake", Profiles = Of | Gm },
            new() { FriendlyName = "Audacity (audio editor)", Category = "Creative", Kind = AppKind.Winget, WingetId = "Audacity.Audacity", Profiles = Of | Gm },
            new() { FriendlyName = "AutoHotkey (automation scripts)", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "AutoHotkey.AutoHotkey", Profiles = Dv | Gm },
            new() { FriendlyName = "Rainmeter (desktop widgets)", Category = "Customisation & Mods", Kind = AppKind.Winget, WingetId = "Rainmeter.Rainmeter", Profiles = Gm },
            new() { FriendlyName = "Lively Wallpaper (animated wallpapers)", Category = "Customisation & Mods", Kind = AppKind.Winget, WingetId = "rocksdanister.LivelyWallpaper", Profiles = Gm },
            new() { FriendlyName = "Mod Organizer 2", Category = "Customisation & Mods", Kind = AppKind.Winget, WingetId = "ModOrganizer2.modorganizer", Profiles = Gm },
            new() { FriendlyName = "Vortex Mod Manager", Category = "General Utilities", Kind = AppKind.Winget, WingetId = "NexusMods.Vortex" },

            // ---- Redistributables - the entries everyone should just say yes to. A
            // single catalog line that silently installs every runtime version games and
            // creator apps commonly expect, so people stop hitting "missing MSVCP140.dll"
            // errors. See InstallBundleAsync for how a Bundle entry actually installs. ----
            new() { FriendlyName = "VC++ Redistributables Pack (all versions)", Category = "Redistributables", Kind = AppKind.Bundle, Profiles = Everyone,
                    BundleWingetIds = new[]
                    {
                        "Microsoft.VCRedist.2005.x86", "Microsoft.VCRedist.2005.x64",
                        "Microsoft.VCRedist.2008.x86", "Microsoft.VCRedist.2008.x64",
                        "Microsoft.VCRedist.2010.x86", "Microsoft.VCRedist.2010.x64",
                        "Microsoft.VCRedist.2012.x86", "Microsoft.VCRedist.2012.x64",
                        "Microsoft.VCRedist.2013.x86", "Microsoft.VCRedist.2013.x64",
                        "Microsoft.VCRedist.2015+.x86", "Microsoft.VCRedist.2015+.x64"
                    } },
            new() { FriendlyName = ".NET Desktop Runtimes (8 + 9)", Category = "Redistributables", Kind = AppKind.Bundle, Profiles = Everyone,
                    BundleWingetIds = new[] { "Microsoft.DotNet.DesktopRuntime.8", "Microsoft.DotNet.DesktopRuntime.9" } },
            // Older DirectX 9/10/11 helper DLLs (d3dx9_43.dll and friends) that plenty of
            // older games still ask for and Windows 11 doesn't ship.
            new() { FriendlyName = "DirectX End-User Runtime (older game DLLs)", Category = "Redistributables", Kind = AppKind.Winget, WingetId = "Microsoft.DirectX" },

            // ---- RGB / peripheral ecosystems - manufacturer control software. Nobody
            // owns all of these, so they're skippable. OpenRGB and SignalRGB are the odd
            // ones out: single apps that talk to hardware from many vendors at once. ----
            new() { FriendlyName = "Corsair iCUE", Category = "RGB & Peripheral Control", Kind = AppKind.Winget, WingetId = "Corsair.iCUE.4" },
            // Synapse 4 is the current version (the old "Razer.Synapse.3" ID doesn't exist).
            new() { FriendlyName = "Razer Synapse", Category = "RGB & Peripheral Control", Kind = AppKind.Winget, WingetId = "RazerInc.RazerInstaller.Synapse4" },
            new() { FriendlyName = "Logitech G HUB", Category = "RGB & Peripheral Control", Kind = AppKind.Winget, WingetId = "Logitech.GHUB" },
            new() { FriendlyName = "SteelSeries GG", Category = "RGB & Peripheral Control", Kind = AppKind.Winget, WingetId = "SteelSeries.GG" },
            new() { FriendlyName = "OpenRGB (universal, multi-vendor)", Category = "RGB & Peripheral Control", Kind = AppKind.Winget, WingetId = "OpenRGB.OpenRGB" },
            new() { FriendlyName = "SignalRGB (universal, multi-vendor)", Category = "RGB & Peripheral Control", Kind = AppKind.Winget, WingetId = "WhirlwindFX.SignalRgb" },
            new() { FriendlyName = "Hyte Nexus (HYTE case/AIO control)", Category = "RGB & Peripheral Control", Kind = AppKind.Manual,
                    ManualFileName = "HyteNexusInstaller.exe", ManualUrl = "https://hyte.co/nexus-download", PageUrl = "https://hyte.com/" },
            new() { FriendlyName = "L-Connect 3 (Lian Li fan/lighting control)", Category = "RGB & Peripheral Control", Kind = AppKind.Manual,
                    ManualFileName = "LConnect3.zip",
                    ManualUrl = "https://lianli-update-2025.lianli-cn.com/L3_CX/20260422-L-Connect%203-x64-v2.1.20-fde9a570.zip", PageUrl = "https://lian-li.com/l-connect3/" },

            // ---- Retro computing. 86Box emulates the real period chipsets/sound/graphics
            // Windows 98 games expect. It only installs the emulator: Windows 98 itself and
            // the ROM files stay firmly "bring your own" (see WriteRetroVmReadme). ----
            new() { FriendlyName = "86Box (Windows 98 VM, BYO ISO/ROMs)", Category = "Retro Computing", Kind = AppKind.Winget, WingetId = "86Box.86Box" },
            new() { FriendlyName = "RetroArch (all-in-one retro game emulator)", Category = "Retro Computing", Kind = AppKind.Winget, WingetId = "Libretro.RetroArch", Profiles = Gm },
            new() { FriendlyName = "PCSX2 (PlayStation 2 emulator, BYO games)", Category = "Retro Computing", Kind = AppKind.Winget, WingetId = "PCSX2Team.PCSX2", Profiles = Gm },
            new() { FriendlyName = "DOSBox-X (old DOS games)", Category = "Retro Computing", Kind = AppKind.Winget, WingetId = "joncampbell123.DOSBox-X", Profiles = Gm },
            new() { FriendlyName = "ScummVM (classic point-and-click adventures)", Category = "Retro Computing", Kind = AppKind.Winget, WingetId = "ScummVM.ScummVM", Profiles = Gm },

            // ---- Vendor software with no winget package: these open the vendor's own page in your
            // default browser and you install from there. ----
            new() { FriendlyName = "AMD Software: Adrenalin (opens download page)", Category = "Drivers & Vendor Software", Kind = AppKind.Link, PageUrl = "https://www.amd.com/en/support/download/drivers.html", Profiles = Gm },
            new() { FriendlyName = "Intel Driver & Support Assistant (opens page)", Category = "Drivers & Vendor Software", Kind = AppKind.Link, PageUrl = "https://www.intel.com/content/www/us/en/support/detect.html", Profiles = Everyone },
            new() { FriendlyName = "MSI Center (opens download page)", Category = "Drivers & Vendor Software", Kind = AppKind.Link, PageUrl = "https://www.msi.com/Landing/MSI-Center", Profiles = Gm },
            new() { FriendlyName = "ASUS Armoury Crate (opens download page)", Category = "Drivers & Vendor Software", Kind = AppKind.Link, PageUrl = "https://rog.asus.com/armoury-crate/", Profiles = Gm },
            new() { FriendlyName = "Cinebench (opens download page)", Category = "Drivers & Vendor Software", Kind = AppKind.Link, PageUrl = "https://www.maxon.net/en/cinebench", Profiles = Gm },
            new() { FriendlyName = "Wallpaper Engine (opens its Steam page)", Category = "Drivers & Vendor Software", Kind = AppKind.Link, PageUrl = "https://store.steampowered.com/app/431960/Wallpaper_Engine/", Profiles = Gm },

            // ---- AI assistants. "9NT1R1C2HH7J" is the Microsoft Store listing ID for the
            // official ChatGPT Windows app (OpenAI documents installing it this way).
            // Google Gemini (no official package) and Perplexity (package being removed
            // upstream) are left out - see ROADMAP.md. ----
            new() { FriendlyName = "Claude", Category = "AI Assistants", Kind = AppKind.Winget, WingetId = "Anthropic.Claude", Profiles = Everyone },
            new() { FriendlyName = "ChatGPT", Category = "AI Assistants", Kind = AppKind.Winget, WingetId = "9NT1R1C2HH7J", Profiles = Everyone },
            new() { FriendlyName = "Claude Code (AI coding in the terminal)", Category = "AI Assistants", Kind = AppKind.Winget, WingetId = "Anthropic.ClaudeCode", Profiles = Dv },
            new() { FriendlyName = "LM Studio (run open AI models locally)", Category = "AI Assistants", Kind = AppKind.Winget, WingetId = "ElementLabs.LMStudio", Profiles = Gm | Dv },
            new() { FriendlyName = "Ollama (run open AI models locally)", Category = "AI Assistants", Kind = AppKind.Winget, WingetId = "Ollama.Ollama", Profiles = Gm | Dv }
        };

        /// <summary>The catalog entries a given profile offers, in catalog order.</summary>
        public static List<AppEntry> CatalogFor(SetupProfile profile) =>
            Catalog.Where(a => (a.Profiles & profile) != 0).ToList();

        // Preinstalled consumer apps the opt-in debloat screen can offer to remove.
        // Never a blanket "remove everything": the screen only lists the ones actually
        // found on this PC (see ScanDebloatAsync), every one starts UNticked, and
        // nothing is touched unless the user ticks it - same disclosed/reversible
        // rule as the tweaks. Removal goes through `winget uninstall` (a native CLI
        // call, like the rest of this class - no PowerShell), using the package
        // family IDs winget itself lists these apps under. Removal is for the signed-in
        // user's account; the revert script reinstalls from the Microsoft Store.
        // Skype is deliberately not here - Microsoft retired it in May 2025.
        public static readonly IReadOnlyList<DebloatItem> DebloatCatalog = new List<DebloatItem>
        {
            new("news", "Microsoft News", "news feed app that nags from the widgets/taskbar",
                "Microsoft.BingNews_8wekyb3d8bbwe", "9WZDNCRFHVFW"),
            new("weather", "Weather", "weather app (the taskbar widget stays separate)",
                "Microsoft.BingWeather_8wekyb3d8bbwe", "9WZDNCRFJ3Q2"),
            new("clipchamp", "Clipchamp", "basic video editor bundled with Windows 11",
                "Clipchamp.Clipchamp_yxz26nhyzhsrt", "9P1J8S7CCWWT"),
            new("solitaire", "Solitaire Collection", "card games with video ads",
                "Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe", "9WZDNCRFHWD2"),
            new("teams", "Microsoft Teams (free/personal)", "the consumer chat app pinned to the taskbar",
                "MicrosoftTeams_8wekyb3d8bbwe", "XP8BT8DW290MPQ"),
            new("office", "Microsoft 365 trial nag (Office hub)", "the 'Get Microsoft 365' trial launcher - not Word/Excel themselves",
                "Microsoft.MicrosoftOfficeHub_8wekyb3d8bbwe", "9WZDNCRD29V9"),
            new("tiktok", "TikTok", "preinstalled on some PCs",
                "BytedancePte.Ltd.TikTok_6yccndn6064se", "9NH2GPH4JZS4"),
            new("disney", "Disney+", "preinstalled on some PCs",
                "Disney.37853FC22B2CE_6rarf9sa4v8jt", "9NXQXXLFST89"),
            new("spotify", "Spotify (Store version)", "preinstalled on some PCs - skip it if you use Spotify!",
                "SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "9NCBCSZSJRSB"),
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

        // The folder logs are kept in: Documents\GamingStack\logs, so people can find them.
        public static string LogsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GamingStack", "logs");

        // ---- live progress (read by the installer screen) and cancel ----
        private DateTime _runStamp = DateTime.Now;
        private volatile bool _cancelRequested;
        public bool CancelRequested => _cancelRequested;
        public string? CurrentActivity { get; private set; }
        public int ItemsDone { get; private set; }
        public int ItemsTotal { get; private set; }

        /// <summary>Asks the run to stop after the item it is on. Nothing is rolled back; tweaks and
        /// app removals that had not started yet are skipped.</summary>
        public void RequestCancel()
        {
            if (_cancelRequested) return;
            _cancelRequested = true;
            Log("The user asked to stop - finishing the current item, then skipping everything else.");
        }

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

        public static List<TweakPreview> PreviewTweaks(TweakOptions options)
        {
            var powerPlanChoice = options.PowerPlan;
            var gaming = options.Profile == SetupProfile.Gaming;
            var developer = options.Profile == SetupProfile.Developer;
            var list = new List<TweakPreview>();

            if (gaming)
            {
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
            }

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

            if (gaming) try
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

            if (gaming) try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
                var val = key?.GetValue("HiberbootEnabled");
                var current = val == null ? "on (Windows default)" : (Convert.ToInt32(val) == 1 ? "on" : "off");
                list.Add(new TweakPreview("Fast Startup", current, "off"));
            }
            catch { list.Add(new TweakPreview("Fast Startup", "unknown", "off")); }

            if (gaming) try
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

            if (gaming) try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
                var val = key?.GetValue("GameDVR_Enabled");
                var current = val == null ? "enabled (Windows default)" : (Convert.ToInt32(val) == 1 ? "enabled" : "disabled");
                list.Add(new TweakPreview("Xbox Game Bar / Game DVR", current, "disabled"));
            }
            catch { list.Add(new TweakPreview("Xbox Game Bar / Game DVR", "unknown", "disabled")); }

            if (gaming) try
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

            if (gaming) try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                var speed = key?.GetValue("MouseSpeed") as string;
                var current = speed == null ? "enabled (Windows default)" : (speed == "0" ? "disabled" : "enabled");
                list.Add(new TweakPreview("Mouse acceleration (\"Enhance pointer precision\")", current, "disabled"));
            }
            catch { list.Add(new TweakPreview("Mouse acceleration (\"Enhance pointer precision\")", "unknown", "disabled")); }

            if (gaming) try
            {
                var (ac, dc) = GetPowerSettingValueIndex("SCHEME_CURRENT", CoreParkingSubgroupGuid, CoreParkingSettingGuid);
                var current = (ac == null && dc == null) ? "not set (Windows default - cores may park when idle)" : $"{ac ?? dc}% minimum unparked";
                list.Add(new TweakPreview("CPU core parking (minimum unparked)", current, "0% (all cores stay available)"));
            }
            catch { list.Add(new TweakPreview("CPU core parking (minimum unparked)", "unknown", "0%")); }

            if (gaming) try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                var val = key?.GetValue("Win32PrioritySeparation");
                var current = val == null ? "2 (Windows default)" : $"{Convert.ToInt32(val)}";
                list.Add(new TweakPreview("Foreground app priority (Win32PrioritySeparation)", current, "26 (short, fixed, high foreground boost)"));
            }
            catch { list.Add(new TweakPreview("Foreground app priority (Win32PrioritySeparation)", "unknown", "26")); }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization");
                var val = key?.GetValue("DODownloadMode");
                var current = val == null ? "not set (Windows default - peer sharing may be used)" : $"{Convert.ToInt32(val)}";
                list.Add(new TweakPreview("Delivery Optimization (P2P update sharing)", current, "0 (HTTP only, no peer-to-peer)"));
            }
            catch { list.Add(new TweakPreview("Delivery Optimization (P2P update sharing)", "unknown", "0")); }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack");
                var val = key?.GetValue("Start");
                var current = val == null ? "unknown" : Convert.ToInt32(val) switch
                {
                    2 => "automatic",
                    3 => "manual",
                    4 => "disabled",
                    _ => "unknown"
                };
                list.Add(new TweakPreview("Telemetry (DiagTrack service)", current, "disabled"));
            }
            catch { list.Add(new TweakPreview("Telemetry (DiagTrack service)", "unknown", "disabled")); }

            if (developer)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
                    var val = key?.GetValue("LongPathsEnabled");
                    var current = val == null ? "off (Windows default)" : (Convert.ToInt32(val) == 1 ? "on" : "off");
                    list.Add(new TweakPreview("Long file paths (over 260 characters)", current, "on"));
                }
                catch { list.Add(new TweakPreview("Long file paths (over 260 characters)", "unknown", "on")); }

                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
                    var val = key?.GetValue("AllowDevelopmentWithoutDevLicense");
                    var current = val == null ? "off (Windows default)" : (Convert.ToInt32(val) == 1 ? "on" : "off");
                    list.Add(new TweakPreview("Developer Mode", current, "on (lets you sideload and debug your own apps)"));
                }
                catch { list.Add(new TweakPreview("Developer Mode", "unknown", "on")); }
            }

            // Only shown when the user picked a provider on the ChooseDns screen -
            // "Skip" means this row (and its change) simply doesn't exist.
            if (options.Dns != DnsChoice.Skip)
            {
                var (_, label) = GetDnsServers(options.Dns);
                try
                {
                    var adapter = GetActiveAdapter();
                    list.Add(new TweakPreview("DNS servers (IPv4)", adapter == null
                        ? "no active network adapter found"
                        : adapter.Describe(), label));
                }
                catch { list.Add(new TweakPreview("DNS servers (IPv4)", "unknown", label)); }
            }

            return list;
        }

        // ---- DNS ----
        // The two IPv4 servers (primary, secondary) for each provider, plus the text
        // the consent screen/log shows. IPv6 DNS is deliberately left alone.
        private static (string[] Servers, string Label) GetDnsServers(DnsChoice choice) => choice switch
        {
            DnsChoice.Cloudflare => (new[] { "1.1.1.1", "1.0.0.1" }, "Cloudflare (1.1.1.1 / 1.0.0.1)"),
            DnsChoice.Google => (new[] { "8.8.8.8", "8.8.4.4" }, "Google (8.8.8.8 / 8.8.4.4)"),
            DnsChoice.Quad9 => (new[] { "9.9.9.9", "149.112.112.112" }, "Quad9 (9.9.9.9 / 149.112.112.112)"),
            _ => (Array.Empty<string>(), "unchanged")
        };

        /// <summary>
        /// The network adapter actually in use (has an IP and a default gateway), with
        /// what its IPv4 DNS is set to right now. <see cref="StaticDns"/> is non-empty
        /// only when someone set DNS by hand - empty means it comes from DHCP (the
        /// router/ISP), which is what a revert has to put back.
        /// </summary>
        private sealed class ActiveAdapter
        {
            public string SettingId = "";
            public string? ConnectionName;
            public string[] EffectiveDns = Array.Empty<string>();
            public string[] StaticDns = Array.Empty<string>();

            public string Describe() =>
                StaticDns.Length > 0
                    ? $"custom ({string.Join(" / ", StaticDns)})"
                    : EffectiveDns.Length > 0
                        ? $"automatic from your router ({string.Join(" / ", EffectiveDns)})"
                        : "automatic from your router";
        }

        private static ActiveAdapter? GetActiveAdapter()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True AND DefaultIPGateway IS NOT NULL");
                foreach (var obj in searcher.Get())
                {
                    var settingId = obj["SettingID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(settingId)) continue;

                    var adapter = new ActiveAdapter
                    {
                        SettingId = settingId,
                        EffectiveDns = (obj["DNSServerSearchOrder"] as string[]) ?? Array.Empty<string>()
                    };

                    // A hand-set DNS list lives in the per-interface NameServer value;
                    // blank/missing means DHCP is supplying it.
                    using var key = Registry.LocalMachine.OpenSubKey(
                        $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{settingId}");
                    var nameServer = key?.GetValue("NameServer") as string;
                    if (!string.IsNullOrWhiteSpace(nameServer))
                        adapter.StaticDns = nameServer.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    // The friendly connection name ("Ethernet", "Wi-Fi") is what netsh
                    // needs for the revert script; it lives on the Win32_NetworkAdapter
                    // twin of this config object.
                    var index = obj["InterfaceIndex"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(index))
                    {
                        using var nameSearcher = new ManagementObjectSearcher(
                            $"SELECT NetConnectionID FROM Win32_NetworkAdapter WHERE InterfaceIndex = {index}");
                        foreach (var a in nameSearcher.Get())
                        {
                            adapter.ConnectionName = a["NetConnectionID"]?.ToString();
                            break;
                        }
                    }
                    return adapter;
                }
            }
            catch { /* falls through to null below */ }
            return null;
        }

        // Power setting GUIDs for the "Processor power management > Processor
        // performance core parking min cores" setting - the documented way to stop
        // Windows parking CPU cores when it thinks they're idle.
        private const string CoreParkingSubgroupGuid = "54533251-82be-4824-96c1-47b60b740d00";
        private const string CoreParkingSettingGuid = "0cc5b647-c1df-4637-891a-dec35c318583";

        /// <summary>
        /// Reads a power setting's current AC/DC value index via `powercfg /q`,
        /// parsing the plain-text output - there's no WMI/registry shortcut for this,
        /// `powercfg` is the documented way to read and write these. Returns (null,
        /// null) if the setting can't be found or parsed (e.g. an unusual scheme).
        /// </summary>
        private static (int? Ac, int? Dc) GetPowerSettingValueIndex(string schemeGuid, string subgroupGuid, string settingGuid)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", $"/q {schemeGuid} {subgroupGuid} {settingGuid}")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return (null, null);
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);

                var acMatch = System.Text.RegularExpressions.Regex.Match(output, @"Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)");
                var dcMatch = System.Text.RegularExpressions.Regex.Match(output, @"Current DC Power Setting Index:\s*0x([0-9a-fA-F]+)");
                int? ac = acMatch.Success ? Convert.ToInt32(acMatch.Groups[1].Value, 16) : null;
                int? dc = dcMatch.Success ? Convert.ToInt32(dcMatch.Groups[1].Value, 16) : null;
                return (ac, dc);
            }
            catch
            {
                return (null, null);
            }
        }

        /// <summary>
        /// Fires a `powercfg` command and waits for it, ignoring output - used for
        /// the apply-side core-parking/power-plan writes where we only care that it
        /// ran, not what it printed (reads go through GetPowerSettingValueIndex).
        /// </summary>
        private static void RunPowercfg(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch { /* best-effort; failure is surfaced by the caller's own catch */ }
        }

        /// <summary>
        /// Fires an `sc` (service control) command and waits for it, same best-effort
        /// shape as RunPowercfg - used for stopping/disabling the DiagTrack service.
        /// </summary>
        private static void RunSc(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("sc", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch { /* best-effort; failure is surfaced by the caller's own catch */ }
        }

        /// <summary>
        /// Maps a service's numeric Start registry value back to the word `sc config
        /// start=` expects, for the revert script.
        /// </summary>
        private static string ServiceStartModeArg(int startValue) => startValue switch
        {
            2 => "auto",
            3 => "demand",
            4 => "disabled",
            _ => "auto"
        };

        /// <summary>
        /// Runs a command-line tool to completion and returns its exit code plus
        /// everything it printed (stdout and stderr together). Output is read as it
        /// arrives rather than after the fact - a tool that prints more than the OS
        /// pipe buffer holds (winget's progress spinner does) would otherwise block
        /// forever waiting for someone to read it, which looks exactly like a hung
        /// install. <paramref name="timeoutMs"/> of 0 means wait as long as it takes.
        /// </summary>
        private static async Task<(int ExitCode, string Output)> RunProcessAsync(string fileName, string arguments, int timeoutMs = 0)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var sb = new System.Text.StringBuilder();
            var sync = new object();
            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sync) sb.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sync) sb.AppendLine(e.Data); };

            if (!p.Start()) throw new InvalidOperationException($"{fileName} failed to start");
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            if (timeoutMs > 0)
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    await p.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { p.Kill(true); } catch { /* already gone */ }
                    lock (sync) sb.AppendLine($"(gave up after {timeoutMs / 1000}s)");
                    return (-1, sb.ToString());
                }
            }
            else
            {
                await p.WaitForExitAsync();
            }

            // WaitForExitAsync can return before the last redirected lines have been
            // delivered; the parameterless WaitForExit() waits for end-of-stream.
            await Task.Run(() => p.WaitForExit());
            lock (sync) return (p.ExitCode, sb.ToString());
        }

        /// <summary>
        /// The last few meaningful lines of a tool's output, for the log file - winget
        /// prints a progress spinner made of control characters and block glyphs that
        /// would bury the useful "why it failed" line.
        /// </summary>
        private static string TailOfOutput(string output, int maxLines = 4)
        {
            var useful = output
                .Split('\n')
                .Select(l => new string(l.Where(c => !char.IsControl(c)).ToArray()).Trim())
                .Where(l => l.Length > 3 && l.Any(char.IsLetter))
                .ToList();
            var tail = useful.Skip(Math.Max(0, useful.Count - maxLines)).Select(l => l.Length > 160 ? l[..160] : l);
            return string.Join(" | ", tail);
        }

        /// <summary>Writes a line to the log file only - not the on-screen terminal.</summary>
        private void LogFileOnly(string text)
        {
            if (_logFilePath == null) return;
            try { File.AppendAllText(_logFilePath, text + Environment.NewLine); }
            catch { /* logging shouldn't itself be fatal */ }
        }

        // Applies a new mouse setting to the running session straight away - without
        // this, the registry values we write below only take effect the next time the
        // user signs in, which reads as "the tweak did nothing".
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);
        private const uint SpiSetMouse = 0x0004;
        private const uint SpifUpdateIniFile = 0x01;
        private const uint SpifSendChange = 0x02;

        /// <summary>
        /// Points the given adapter's IPv4 DNS at <paramref name="servers"/> through
        /// WMI (the same call the Network Connections control panel uses), so it takes
        /// effect immediately rather than after the next adapter reset. Returns the
        /// WMI result code - 0 (or 1, "reboot needed") is success.
        /// </summary>
        private static uint SetDnsServers(string settingId, string[] servers)
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_NetworkAdapterConfiguration WHERE SettingID = '{settingId}'");
            foreach (var obj in searcher.Get())
            {
                if (obj is not ManagementObject config) continue;
                var result = config.InvokeMethod("SetDNSServerSearchOrder", new object[] { servers });
                return result == null ? 0u : Convert.ToUInt32(result);
            }
            return uint.MaxValue;
        }

        /// <summary>
        /// The undo script, written to disk as it grows: every tweak adds its "put it
        /// back" lines BEFORE it changes anything, and a blank line (which every tweak
        /// ends its block with) flushes the file. So if the app is killed halfway
        /// through - power cut, crash, Alt+F4 - the script on disk already covers
        /// everything that had started changing, instead of only being written at the
        /// very end.
        /// </summary>
        private sealed class RevertScript : IEnumerable<string>
        {
            private readonly List<string> _lines = new();
            private readonly string _path;

            public RevertScript(string path) { _path = path; }

            public void Add(string line)
            {
                _lines.Add(line);
                if (line.Length == 0) Flush();
            }

            public void Flush()
            {
                try
                {
                    var dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllLines(_path, _lines);
                }
                catch { /* the final write in ApplyTweaks reports a real failure */ }
            }

            public IEnumerator<string> GetEnumerator() => _lines.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private RevertScript? _revert;

        // One undo script shared by the tweaks and the debloat pass, created the first
        // time either needs it.
        private RevertScript EnsureRevert()
        {
            if (_revert != null) return _revert;

            // One undo script per run, kept in Documents (not Temp, which Windows cleans out).
            // Never overwritten: a second run would otherwise record the ALREADY-changed values
            // and the first run's original settings would be lost.
            var undoDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GamingStack", "undo");
            Directory.CreateDirectory(undoDir);
            TweaksRevertFilePath = Path.Combine(undoDir, $"revert-tweaks_{_runStamp:yyyyMMdd_HHmmss}.cmd");
            _revert = new RevertScript(TweaksRevertFilePath)
            {
                "@echo off",
                $"REM GamingStack revert script - generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                "REM Puts back what GamingStack changed after you said yes. Right-click this",
                "REM file and choose \"Run as administrator\" - the registry/service lines below",
                "REM need elevation, same as GamingStack itself did.",
                ""
            };
            return _revert;
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
                try
                {
                    var stamped = text.Length == 0 ? "" : $"[{DateTime.Now:HH:mm:ss}] {text}";
                    File.AppendAllText(_logFilePath, stamped + Environment.NewLine);
                }
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
        public async Task RunAsync(IReadOnlyList<AppEntry> selected, bool applyTweaks, TweakOptions options, RunContext? context = null)
        {
            var started = DateTime.Now;
            _runStamp = started;
            int installed = 0, failed = 0, skipped = 0, opened = 0;
            void Count(InstallResultKind k)
            {
                switch (k)
                {
                    case InstallResultKind.Installed: installed++; break;
                    case InstallResultKind.Failed: failed++; break;
                    case InstallResultKind.Skipped: skipped++; break;
                    case InstallResultKind.Opened: opened++; break;
                }
            }

            try
            {
                _revert = null; // a fresh undo script for each run
                _cancelRequested = false;
                ItemsDone = 0; ItemsTotal = selected.Count;
                Directory.CreateDirectory(_workDir);
                try { File.Delete(FailFile); } catch { /* fine if it didn't exist */ }

                Directory.CreateDirectory(LogsFolder);
                _logFilePath = Path.Combine(LogsFolder, $"gamingstack_{started:yyyyMMdd_HHmmss}.log");
                try { File.WriteAllText(_logFilePath, BuildLogHeader(selected, applyTweaks, options, context)); }
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
                    Log(wingetOk ? "winget found." : "winget NOT found.");
                    if (!wingetOk && selected.Any(a => a.Kind is AppKind.Winget or AppKind.Bundle))
                    {
                        Log("WARNING: winget not found - winget-based selections will be skipped.");
                        Fail("winget not found - install 'App Installer' from the Microsoft Store to install winget-based apps.");
                    }

                    var index = 0;
                    foreach (var entry in selected)
                    {
                        index++;
                        if (_cancelRequested)
                        {
                            Log($"[{entry.FriendlyName}] skipped - run was stopped");
                            OnItemResult?.Invoke(new ItemResult(entry, InstallResultKind.Skipped, "stopped before it started"));
                            skipped++;
                            continue;
                        }

                        CurrentActivity = $"{index}/{selected.Count}  {entry.FriendlyName}";
                        Log($"--- ({index}/{selected.Count}) {entry.FriendlyName} ---");
                        var t0 = DateTime.Now;
                        InstallResultKind kind;
                        string? detail;

                        if (entry.Kind is AppKind.Winget or AppKind.Bundle && !wingetOk)
                        {
                            kind = InstallResultKind.Skipped; detail = "winget unavailable";
                            Log($"[{entry.FriendlyName}] skipped - winget unavailable");
                        }
                        else if (entry.Kind == AppKind.Winget)
                            (kind, detail) = await InstallWingetAppAsync(entry);
                        else if (entry.Kind == AppKind.Bundle)
                            (kind, detail) = await InstallBundleAsync(entry);
                        else if (entry.Kind == AppKind.Link)
                            (kind, detail) = OpenLink(entry);
                        else
                        {
                            var (ok, d) = await HandleManualInstallerAsync(entry);
                            if (!ok && !string.IsNullOrWhiteSpace(entry.PageUrl))
                            {
                                Log($"[{entry.FriendlyName}] the automatic install did not work - opening the vendor page instead.");
                                (kind, detail) = OpenLink(entry, $"automatic install failed ({d})");
                            }
                            else
                            {
                                kind = ok ? InstallResultKind.Installed : InstallResultKind.Failed; detail = d;
                            }
                        }

                        ItemsDone = index;
                        Count(kind);
                        Log($"[{entry.FriendlyName}] result: {kind}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}  (took {(DateTime.Now - t0).TotalSeconds:0}s)");
                        OnItemResult?.Invoke(new ItemResult(entry, kind, detail));
                    }
                    CurrentActivity = null;

                    if (selected.Any(a => a.WingetId == "86Box.86Box"))
                        WriteRetroVmReadme();
                }

                Log("");
                if (_cancelRequested)
                {
                    Log("STOPPED BY THE USER. Tweaks and app removals were NOT applied (nothing was changed beyond the installs above).");
                }
                else if (applyTweaks)
                {
                    Log("Applying tweaks (you said yes to this)...");
                    ApplyTweaks(options);
                    if (options.DebloatIds.Count > 0)
                    {
                        Log("");
                        await ApplyDebloatAsync(options.DebloatIds);
                    }
                    EnsureRevert().Flush();
                    if (TweaksRevertFilePath != null) Log($"Undo script written to {TweaksRevertFilePath}");
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
                Log(ex.ToString());
            }
            finally
            {
                if (_logFilePath != null)
                {
                    Log($"SUMMARY: {installed} installed, {opened} opened in browser, {skipped} skipped, {failed} failed - took {(DateTime.Now - started).TotalMinutes:0.#} min");
                    Log($"Full log saved to {_logFilePath}");
                }
                OnFinished?.Invoke();
            }
        }

        // The first lines of every log: enough to understand the whole run without the app.
        private string BuildLogHeader(IReadOnlyList<AppEntry> selected, bool applyTweaks, TweakOptions options, RunContext? context)
        {
            var sb = new System.Text.StringBuilder();
            var version = typeof(InstallerEngine).Assembly.GetName().Version?.ToString(3) ?? "?";
            bool admin;
            try
            {
                using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                admin = new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { admin = false; }

            sb.AppendLine($"GamingStack {version} - run log");
            sb.AppendLine($"Started        {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Computer/user  {Environment.MachineName} / {Environment.UserName}");
            sb.AppendLine($"Windows        {Environment.OSVersion.VersionString}");
            sb.AppendLine($"Administrator  {(admin ? "yes" : "NO - some changes will fail")}");
            sb.AppendLine($"Setup type     {context?.ProfileTitle ?? "(unknown)"}");
            sb.AppendLine($"Backup         {context?.BackupSummary ?? "(unknown)"}");
            sb.AppendLine($"Tweaks         {(applyTweaks ? "YES - the user agreed on the consent screen" : "no")}");
            if (applyTweaks)
            {
                sb.AppendLine($"  power plan   {options.PowerPlan}");
                sb.AppendLine($"  DNS          {options.Dns}");
                sb.AppendLine($"  removals     {(options.DebloatIds.Count == 0 ? "none" : string.Join(", ", options.DebloatIds))}");
            }
            sb.AppendLine($"Items          {selected.Count}");
            foreach (var a in selected)
            {
                var how = a.Kind switch
                {
                    AppKind.Winget => $"winget {a.WingetId}",
                    AppKind.Bundle => $"winget bundle ({a.BundleWingetIds?.Length ?? 0} packages)",
                    AppKind.Link => $"opens {a.PageUrl}",
                    _ => $"downloads {a.ManualUrl}"
                };
                sb.AppendLine($"  - {a.FriendlyName}  [{how}]");
            }
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();
            return sb.ToString();
        }

        // Link entries: no winget package, so open the vendor's own page in the default
        // browser. Only ever https, only ever an address from this catalog - never anything
        // typed in or read from the internet.
        private (InstallResultKind Kind, string? Detail) OpenLink(AppEntry entry, string? reason = null)
        {
            var url = entry.PageUrl;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                Fail($"{entry.FriendlyName}: no safe download page address in the catalog");
                return (InstallResultKind.Failed, "no download page address");
            }
            try
            {
                Log($"[{entry.FriendlyName}] opening {uri.Host} in your default browser...");
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                var why = reason == null ? "" : $" ({reason})";
                return (InstallResultKind.Opened, $"opened {uri.Host} - finish the install there{why}");
            }
            catch (Exception ex)
            {
                Fail($"{entry.FriendlyName}: could not open the browser - {ex.Message}. Go to {uri.AbsoluteUri} yourself.");
                return (InstallResultKind.Failed, $"could not open the browser - visit {uri.Host}");
            }
        }

        /// <summary>
        /// Which of the <see cref="DebloatCatalog"/> apps are actually installed for
        /// this user right now - so the wizard only offers things that exist. Reads
        /// the per-user list of installed Store packages from the registry (no
        /// PowerShell); a package is a match when its full name starts with the app's
        /// name and ends with its publisher id.
        /// </summary>
        public static IReadOnlyList<DebloatItem> ScanDebloat()
        {
            var found = new List<DebloatItem>();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
                if (key == null) return found;
                var installed = key.GetSubKeyNames();

                foreach (var item in DebloatCatalog)
                {
                    var split = item.PackageId.LastIndexOf('_');
                    if (split <= 0) continue;
                    var name = item.PackageId[..split];
                    var publisher = item.PackageId[(split + 1)..];
                    var prefix = name + "_";
                    var suffix = "__" + publisher;
                    if (installed.Any(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                        && n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                        found.Add(item);
                }
            }
            catch
            {
                // Can't tell what's installed - offer nothing rather than guess.
            }
            return found;
        }

        /// <summary>
        /// Removes the Store apps the user ticked, one at a time, via winget. Every
        /// removal writes its "put it back" line (a Store reinstall) to the undo script
        /// first. Never touches anything that wasn't explicitly ticked.
        /// </summary>
        private async Task ApplyDebloatAsync(IReadOnlyCollection<string> ids)
        {
            var revert = EnsureRevert();
            var chosen = DebloatCatalog.Where(d => ids.Contains(d.Id)).ToList();
            if (chosen.Count == 0) return;

            if (!IsWingetAvailable())
            {
                Fail("Debloat skipped - winget isn't available.");
                return;
            }

            Log("Removing the apps you ticked (you said yes to this)...");
            foreach (var item in chosen)
            {
                revert.Add($"REM Removed app: {item.Name}");
                revert.Add($"winget install --id {item.StoreProductId} --source msstore --accept-package-agreements --accept-source-agreements");
                revert.Add("");

                try
                {
                    var (code, output) = await RunProcessAsync("winget",
                        $"uninstall --id {item.PackageId} --silent --accept-source-agreements", 180000);
                    if (code == 0)
                        Log($"[debloat] {item.Name}: removed");
                    else
                    {
                        Fail($"Couldn't remove {item.Name} (exit code {code}) - it may already be gone.");
                        LogFileOnly($"    winget said: {TailOfOutput(output)}");
                    }
                }
                catch (Exception ex)
                {
                    Fail($"Couldn't remove {item.Name}: {ex.Message}");
                }
            }
            revert.Flush();
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
                    // Output is drained as it arrives (RunProcessAsync) - winget prints a
                    // lot, and an un-read pipe fills up and freezes the install.
                    var (exitCode, output) = await RunProcessAsync("winget",
                        $"install --id {id} -e --accept-package-agreements --accept-source-agreements --silent");

                    if (exitCode == 0)
                    {
                        Log($"[{displayName}] OK");
                        return (InstallResultKind.Installed, null);
                    }

                    if (KnownWingetExitCodes.TryGetValue(exitCode, out var known) && known.AlreadyUpToDate)
                    {
                        Log($"[{displayName}] {known.Friendly}");
                        return (InstallResultKind.Skipped, known.Friendly);
                    }

                    lastDetail = DescribeExitCode(exitCode);
                    Log($"[{displayName}] exit code {exitCode} - {lastDetail}");
                    LogFileOnly($"    winget said: {TailOfOutput(output)}");
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

        // Writes what was downloaded to the log: size, SHA-256 and (if it has one) who signed it,
        // so there is a record of exactly what was run on this PC.
        private void LogDownloadFingerprint(string name, string path)
        {
            try
            {
                var info = new FileInfo(path);
                using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                string signer;
                try
                {
                    using var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                        System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path));
                    signer = cert.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false);
                }
                catch { signer = "not digitally signed"; }
                Log($"[{name}] downloaded {info.Length / 1024.0 / 1024.0:0.0} MB, SHA-256 {hash}");
                Log($"[{name}] signer: {signer}");
            }
            catch (Exception ex) { Log($"[{name}] could not fingerprint the download: {ex.Message}"); }
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

            LogDownloadFingerprint(name, filePath);

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
        /// Drops a plain-English readme on the desktop once 86Box has been installed
        /// - not a pre-built VM config, deliberately. 86Box's config format and its
        /// own "Add Machine" setup wizard are both real but version-specific, and
        /// getting either wrong by hand would hand back a machine that silently
        /// fails to boot rather than no machine at all - worse than just pointing at
        /// 86Box's own wizard with good hardware-pick guidance. The two things this
        /// genuinely can't supply - a legal Windows 98 ISO/key, and ROM dumps copied
        /// from real hardware - are called out clearly rather than glossed over.
        /// </summary>
        private void WriteRetroVmReadme()
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var folder = Path.Combine(desktop, "Windows 98 Gaming VM");
                Directory.CreateDirectory(folder);

                var lines = new[]
                {
                    "GamingStack - Windows 98 Gaming VM",
                    "===================================",
                    "",
                    "86Box is now installed. It's a virtual PC built to run old games",
                    "properly, using real emulated 90s hardware instead of a generic VM.",
                    "",
                    "Two things GamingStack can't give you, and had to leave out:",
                    "",
                    "1. Windows 98 itself. It's old, but it's still Microsoft's",
                    "   software, not something GamingStack is allowed to hand you.",
                    "   You'll need your own legally-owned Windows 98 CD/ISO and key.",
                    "",
                    "2. ROM files. 86Box needs small files copied from real old PC",
                    "   BIOS chips to work at all. These aren't included either - the",
                    "   86Box team explains where to get a legitimate set here:",
                    "   https://86box.readthedocs.io/en/latest/usage/roms.html",
                    "",
                    "Once you have both, here's what to do:",
                    "",
                    "1. Put your ROM files in one of these folders (86Box checks both",
                    "   automatically):",
                    "     %LOCALAPPDATA%\\86Box\\roms",
                    "     %PROGRAMDATA%\\86Box\\roms",
                    "",
                    "2. Open 86Box and use its own \"Add Machine\" setup wizard. When it",
                    "   asks you to pick hardware, a good period-accurate Windows 98",
                    "   gaming setup is:",
                    "     CPU:         Pentium II \"Deschutes\" @ 200MHz (3x multiplier)",
                    "     Motherboard: a \"BX\" chipset board (e.g. Tyan Trinity 100AT)",
                    "     Graphics:    3dfx Voodoo3 3500 (AGP)",
                    "     Sound:       Sound Blaster 16 PnP",
                    "     Memory:      128MB",
                    "     Hard drive:  a new virtual hard disk (IDE)",
                    "",
                    "3. Mount your Windows 98 ISO in 86Box's virtual CD drive and boot",
                    "   from it, same as installing on a real PC.",
                    "",
                    "This is just a starting point - 86Box can emulate plenty of other",
                    "setups too if you want to experiment.",
                };

                var path = Path.Combine(folder, "READ ME FIRST.txt");
                File.WriteAllLines(path, lines);
                Log($"[retro] Windows 98 VM readme written to: {path}");
            }
            catch (Exception ex)
            {
                Fail($"Couldn't write the Windows 98 VM readme: {ex.Message}");
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
        private void ApplyTweaks(TweakOptions options)
        {
            var powerPlanChoice = options.PowerPlan;
            var gaming = options.Profile == SetupProfile.Gaming;
            var developer = options.Profile == SetupProfile.Developer;
            var revertLines = EnsureRevert();

            if (gaming) try
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

            if (gaming) try
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

            if (gaming) try
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

            if (gaming) try
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

            if (gaming) try
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

            if (gaming) try
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

            if (gaming) try
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

            if (gaming) try
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

            if (gaming) try
            {
                using var readKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                var existingSpeed = readKey?.GetValue("MouseSpeed") as string;
                var existingThreshold1 = readKey?.GetValue("MouseThreshold1") as string;
                var existingThreshold2 = readKey?.GetValue("MouseThreshold2") as string;

                revertLines.Add("REM Mouse acceleration (\"Enhance pointer precision\")");
                revertLines.Add(existingSpeed == null
                    ? @"reg delete ""HKCU\Control Panel\Mouse"" /v MouseSpeed /f"
                    : $@"reg add ""HKCU\Control Panel\Mouse"" /v MouseSpeed /t REG_SZ /d {existingSpeed} /f");
                revertLines.Add(existingThreshold1 == null
                    ? @"reg delete ""HKCU\Control Panel\Mouse"" /v MouseThreshold1 /f"
                    : $@"reg add ""HKCU\Control Panel\Mouse"" /v MouseThreshold1 /t REG_SZ /d {existingThreshold1} /f");
                revertLines.Add(existingThreshold2 == null
                    ? @"reg delete ""HKCU\Control Panel\Mouse"" /v MouseThreshold2 /f"
                    : $@"reg add ""HKCU\Control Panel\Mouse"" /v MouseThreshold2 /t REG_SZ /d {existingThreshold2} /f");
                revertLines.Add("");

                using var writeKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse");
                writeKey?.SetValue("MouseSpeed", "0", RegistryValueKind.String);
                writeKey?.SetValue("MouseThreshold1", "0", RegistryValueKind.String);
                writeKey?.SetValue("MouseThreshold2", "0", RegistryValueKind.String);
                // Apply to the live session too (speed 0, thresholds 0, 0) so it works
                // now rather than after the next sign-in.
                try { SystemParametersInfo(SpiSetMouse, 0, new[] { 0, 0, 0 }, SpifUpdateIniFile | SpifSendChange); }
                catch { /* registry value is set; it'll apply at next sign-in */ }
                var wasMouse = existingSpeed == null ? "enabled (default)" : (existingSpeed == "0" ? "disabled" : "enabled");
                Log($"[tweaks] Mouse acceleration: disabled (was: {wasMouse})");
            }
            catch (Exception ex)
            {
                Fail($"Mouse acceleration tweak failed: {ex.Message}");
            }

            if (gaming) try
            {
                var (existingAc, existingDc) = GetPowerSettingValueIndex("SCHEME_CURRENT", CoreParkingSubgroupGuid, CoreParkingSettingGuid);

                revertLines.Add("REM CPU core parking (minimum unparked)");
                revertLines.Add(existingAc == null
                    ? $@"powercfg -setacvalueindex SCHEME_CURRENT {CoreParkingSubgroupGuid} {CoreParkingSettingGuid} 100"
                    : $@"powercfg -setacvalueindex SCHEME_CURRENT {CoreParkingSubgroupGuid} {CoreParkingSettingGuid} {existingAc}");
                revertLines.Add(existingDc == null
                    ? $@"powercfg -setdcvalueindex SCHEME_CURRENT {CoreParkingSubgroupGuid} {CoreParkingSettingGuid} 100"
                    : $@"powercfg -setdcvalueindex SCHEME_CURRENT {CoreParkingSubgroupGuid} {CoreParkingSettingGuid} {existingDc}");
                revertLines.Add(@"powercfg -setactive SCHEME_CURRENT");
                revertLines.Add("");

                RunPowercfg($"-setacvalueindex SCHEME_CURRENT {CoreParkingSubgroupGuid} {CoreParkingSettingGuid} 0");
                RunPowercfg($"-setdcvalueindex SCHEME_CURRENT {CoreParkingSubgroupGuid} {CoreParkingSettingGuid} 0");
                RunPowercfg("-setactive SCHEME_CURRENT");
                var wasParking = (existingAc == null && existingDc == null) ? "not set (default)" : $"{existingAc ?? existingDc}%";
                Log($"[tweaks] CPU core parking: all cores kept available (was: {wasParking} minimum unparked)");
            }
            catch (Exception ex)
            {
                Fail($"CPU core parking tweak failed: {ex.Message}");
            }

            if (gaming) try
            {
                using var readKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                var existing = readKey?.GetValue("Win32PrioritySeparation");

                revertLines.Add("REM Foreground app priority (Win32PrioritySeparation)");
                revertLines.Add(existing == null
                    ? @"reg delete ""HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl"" /v Win32PrioritySeparation /f"
                    : $@"reg add ""HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl"" /v Win32PrioritySeparation /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                revertLines.Add("");

                using var writeKey = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                writeKey?.SetValue("Win32PrioritySeparation", 26, RegistryValueKind.DWord);
                var wasPriority = existing == null ? "2 (default)" : $"{Convert.ToInt32(existing)}";
                Log($"[tweaks] Foreground app priority: set to 26 (was: {wasPriority})");
            }
            catch (Exception ex)
            {
                Fail($"Foreground app priority tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization");
                var existing = readKey?.GetValue("DODownloadMode");

                revertLines.Add("REM Delivery Optimization (P2P update sharing)");
                revertLines.Add(existing == null
                    ? @"reg delete ""HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization"" /v DODownloadMode /f"
                    : $@"reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization"" /v DODownloadMode /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                revertLines.Add("");

                using var writeKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization");
                writeKey?.SetValue("DODownloadMode", 0, RegistryValueKind.DWord);
                var wasDo = existing == null ? "not set (default)" : $"{Convert.ToInt32(existing)}";
                Log($"[tweaks] Delivery Optimization: set to HTTP-only (was: {wasDo})");
            }
            catch (Exception ex)
            {
                Fail($"Delivery Optimization tweak failed: {ex.Message}");
            }

            try
            {
                using var readKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack");
                var existing = readKey?.GetValue("Start");
                var existingStart = existing == null ? 2 : Convert.ToInt32(existing);

                revertLines.Add("REM Telemetry (DiagTrack service)");
                revertLines.Add($@"sc config DiagTrack start= {ServiceStartModeArg(existingStart)}");
                if (existingStart == 2 || existingStart == 3)
                    revertLines.Add(@"sc start DiagTrack");
                revertLines.Add("");

                RunSc("stop DiagTrack");
                RunSc("config DiagTrack start= disabled");
                var wasDiag = existing == null ? "automatic (default)" : existingStart switch
                {
                    2 => "automatic",
                    3 => "manual",
                    4 => "disabled",
                    _ => "unknown"
                };
                Log($"[tweaks] Telemetry (DiagTrack service): disabled (was: {wasDiag})");
            }
            catch (Exception ex)
            {
                Fail($"Telemetry tweak failed: {ex.Message}");
            }

            // Developer-profile tweaks. Same pattern as every other tweak: read the real
            // current value, write the undo line first, then change it.
            if (developer)
            {
                try
                {
                    const string fsPath = @"SYSTEM\CurrentControlSet\Control\FileSystem";
                    using var readKey = Registry.LocalMachine.OpenSubKey(fsPath);
                    var existing = readKey?.GetValue("LongPathsEnabled");

                    revertLines.Add("REM Long file paths");
                    revertLines.Add(existing == null
                        ? $@"reg delete ""HKLM\{fsPath}"" /v LongPathsEnabled /f"
                        : $@"reg add ""HKLM\{fsPath}"" /v LongPathsEnabled /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                    revertLines.Add("");

                    using var writeKey = Registry.LocalMachine.CreateSubKey(fsPath);
                    writeKey?.SetValue("LongPathsEnabled", 1, RegistryValueKind.DWord);
                    Log($"[tweaks] Long file paths: enabled (was: {(existing == null ? "not set (off)" : (Convert.ToInt32(existing) == 1 ? "enabled" : "disabled"))})");
                }
                catch (Exception ex)
                {
                    Fail($"Long file paths tweak failed: {ex.Message}");
                }

                try
                {
                    const string devPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock";
                    using var readKey = Registry.LocalMachine.OpenSubKey(devPath);
                    var existing = readKey?.GetValue("AllowDevelopmentWithoutDevLicense");

                    revertLines.Add("REM Developer Mode");
                    revertLines.Add(existing == null
                        ? $@"reg delete ""HKLM\{devPath}"" /v AllowDevelopmentWithoutDevLicense /f"
                        : $@"reg add ""HKLM\{devPath}"" /v AllowDevelopmentWithoutDevLicense /t REG_DWORD /d {Convert.ToInt32(existing)} /f");
                    revertLines.Add("");

                    using var writeKey = Registry.LocalMachine.CreateSubKey(devPath);
                    writeKey?.SetValue("AllowDevelopmentWithoutDevLicense", 1, RegistryValueKind.DWord);
                    Log($"[tweaks] Developer Mode: on (was: {(existing == null ? "not set (off)" : (Convert.ToInt32(existing) == 1 ? "on" : "off"))})");
                }
                catch (Exception ex)
                {
                    Fail($"Developer Mode tweak failed: {ex.Message}");
                }
            }

            if (options.Dns != DnsChoice.Skip)
            {
                try
                {
                    var servers = GetDnsServers(options.Dns);
                    var adapter = GetActiveAdapter();
                    if (adapter == null)
                    {
                        revertLines.Add("REM DNS servers - no active adapter found, nothing changed");
                        revertLines.Add("");
                        Log("[tweaks] DNS servers: skipped - no active network adapter found");
                    }
                    else
                    {
                        // Put-back line goes in FIRST: DHCP if the adapter wasn't using
                        // fixed DNS, otherwise the exact fixed servers it had.
                        revertLines.Add($"REM DNS servers ({adapter.ConnectionName})");
                        if (adapter.StaticDns.Length == 0)
                            revertLines.Add($@"netsh interface ip set dns name=""{adapter.ConnectionName}"" source=dhcp");
                        else
                        {
                            revertLines.Add($@"netsh interface ip set dns name=""{adapter.ConnectionName}"" static {adapter.StaticDns[0]}");
                            for (var i = 1; i < adapter.StaticDns.Length; i++)
                                revertLines.Add($@"netsh interface ip add dns name=""{adapter.ConnectionName}"" {adapter.StaticDns[i]} index={i + 1}");
                        }
                        revertLines.Add("");

                        var code = SetDnsServers(adapter.SettingId, servers.Servers);
                        if (code == 0 || code == 1)
                            Log($"[tweaks] DNS servers: {servers.Label} on {adapter.ConnectionName} (was: {adapter.Describe()})");
                        else
                            Fail($"DNS tweak failed: Windows returned code {code}");
                    }
                }
                catch (Exception ex)
                {
                    Fail($"DNS tweak failed: {ex.Message}");
                }
            }

            revertLines.Flush();
            Log($"[tweaks] To undo these, run (as Administrator): {TweaksRevertFilePath}");

            TweaksApplied = true;
        }
    }
}
