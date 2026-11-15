# App catalog

Everything GamingStack can install, by setup type. This file is generated from the catalog in
`InstallerEngine.cs`, so it always matches the app. **Nothing is installed unless you say yes** on the
selection screens. "winget" entries install silently through Windows' own package manager;
"opens" entries have no winget package, so GamingStack just opens the vendor's own download page
in your default browser and you install from there.

## Gaming (135 items)

### Web Browsers

| App | How it installs |
|---|---|
| Firefox | winget `Mozilla.Firefox` |
| Google Chrome | winget `Google.Chrome` |
| Brave (privacy-focused browser) | winget `Brave.Brave` |

### Game Launchers

| App | How it installs |
|---|---|
| Steam | winget `Valve.Steam` |
| Epic Games Launcher | winget `EpicGames.EpicGamesLauncher` |
| Ubisoft Connect | winget `Ubisoft.Connect` |
| EA app | winget `ElectronicArts.EADesktop` |
| GOG Galaxy | winget `GOG.Galaxy` |
| Battle.net | winget `Blizzard.BattleNet` |
| Amazon Games | winget `Amazon.Games` |
| Rockstar Games Launcher | winget `RockstarGames.Launcher` |
| itch.io app | winget `ItchIo.Itch` |
| Heroic (Epic/GOG/Amazon in one) | winget `HeroicGamesLauncher.HeroicGamesLauncher` |
| Xbox app (Game Pass) | winget `9MV0B5HZVK9Z` |
| Playnite (unifies the above) | winget `Playnite.Playnite` |
| NVIDIA GeForce NOW (cloud gaming) | winget `Nvidia.GeForceNow` |

### Chat & Meetings

| App | How it installs |
|---|---|
| Discord | winget `Discord.Discord` |
| TeamSpeak (voice chat) | winget `TeamSpeakSystems.TeamSpeakClient` |
| Mumble (voice chat) | winget `Mumble.Mumble.Client` |

### Office & Documents

| App | How it installs |
|---|---|
| Microsoft 365 Apps | winget `Microsoft.Office` |

### Monitoring & Performance

| App | How it installs |
|---|---|
| NVIDIA App (drivers, game settings, recording - opens page) | opens [www.nvidia.com](https://www.nvidia.com/en-us/software/nvidia-app/) |
| NVIDIA Profile Inspector (advanced driver settings) | winget `Orbmu2k.nvidiaProfileInspector` |
| NVCleanstall (choose which parts of an NVIDIA driver to install) | winget `TechPowerUp.NVCleanstall` |
| NVIDIA Broadcast (AI mic noise removal + camera effects, opens page) | opens [www.nvidia.com](https://www.nvidia.com/en-us/geforce/broadcasting/broadcast-app/) |
| Display Driver Uninstaller (clean GPU driver swaps) | winget `Wagnardsoft.DisplayDriverUninstaller` |
| HWiNFO | winget `REALiX.HWiNFO` |
| CPU-Z | winget `CPUID.CPU-Z` |
| GPU-Z | winget `TechPowerUp.GPU-Z` |
| Speccy | winget `Piriform.Speccy` |
| CrystalDiskInfo | winget `CrystalDewWorld.CrystalDiskInfo` |
| MSI Afterburner (GPU overclock + FPS overlay) | winget `Guru3D.Afterburner` |
| CapFrameX (FPS capture + analysis) | winget `CXWorld.CapFrameX` |
| OCCT (stress test + stability) | winget `OCBase.OCCT.Personal` |
| FanControl (custom fan curves) | winget `Rem0o.FanControl` |
| Process Lasso | winget `BitSum.ProcessLasso` |
| LibreHardwareMonitor (open-source sensors) | winget `LibreHardwareMonitor.LibreHardwareMonitor` |
| RivaTuner Statistics Server (FPS overlay engine) | winget `Guru3D.RTSS` |
| CrystalDiskMark (drive speed test) | winget `CrystalDewWorld.CrystalDiskMark` |
| FurMark (GPU stress test) | winget `Geeks3D.FurMark.2` |
| HWMonitor (temperatures and voltages) | winget `CPUID.HWMonitor` |
| Core Temp (CPU temperature) | winget `ALCPU.CoreTemp` |
| ThrottleStop (Intel CPU throttling control) | winget `TechPowerUp.ThrottleStop` |
| Microsoft PC Manager (opens page) | opens [pcmanager.microsoft.com](https://pcmanager.microsoft.com/) |
| Razer Cortex | direct download (page: [www.razer.com](https://www.razer.com/cortex)) |

### Streaming & Recording

| App | How it installs |
|---|---|
| OBS Studio | winget `OBSProject.OBSStudio` |
| Streamlabs Desktop | winget `Streamlabs.Streamlabs` |
| Medal.tv | winget `MedalB.V.Medal` |
| Voicemeeter Banana | winget `VB-Audio.Voicemeeter.Banana` |
| Elgato Stream Deck | winget `Elgato.StreamDeck` |
| Elgato Wave Link | winget `Elgato.WaveLink` |
| Sunshine (stream games to Moonlight) | winget `LizardByte.Sunshine` |
| Moonlight (play streamed PC games) | winget `MoonlightGameStreamingProject.Moonlight` |
| Parsec (low-latency remote play) | winget `Parsec.Parsec` |
| Elgato 4K Capture Utility (capture cards) | winget `Elgato.4KCaptureUtility` |
| Elgato Camera Hub (webcam settings) | winget `Elgato.CameraHub` |
| Elgato Control Center (Key Light) | winget `Elgato.ControlCenter` |
| Streamer.bot (stream chat and alert automation) | winget `streamerbot.streamerbot` |
| Voicemod (live voice changer, opens page) | opens [www.voicemod.net](https://www.voicemod.net/) |
| VB-Cable (virtual audio cable, opens page) | opens [vb-audio.com](https://vb-audio.com/Cable/) |
| Voicemeeter Potato (advanced audio mixer) | winget `VB-Audio.Voicemeeter.Potato` |

### General Utilities

| App | How it installs |
|---|---|
| VS Code | winget `Microsoft.VisualStudioCode` |
| PowerShell 7 | winget `Microsoft.PowerShell` |
| Python 3 | winget `Python.Python.3.13` |
| PowerToys | winget `Microsoft.PowerToys` |
| VLC Media Player | winget `VideoLAN.VLC` |
| 7-Zip | winget `7zip.7zip` |
| Bitwarden (password manager) | winget `Bitwarden.Bitwarden` |
| WizTree (see what's filling a drive) | winget `AntibodySoftware.WizTree` |
| ShareX (screenshots + screen recording) | winget `ShareX.ShareX` |
| EarTrumpet (per-app volume mixer) | winget `File-New-Project.EarTrumpet` |
| Ditto (clipboard history) | winget `Ditto.Ditto` |
| Spotify | winget `Spotify.Spotify` |
| AutoHotkey (automation scripts) | winget `AutoHotkey.AutoHotkey` |
| Vortex Mod Manager | winget `NexusMods.Vortex` |

### Security & Privacy

| App | How it installs |
|---|---|
| Malwarebytes (on-demand malware scanner) | winget `Malwarebytes.Malwarebytes` |
| Proton VPN | winget `Proton.ProtonVPN` |

### Creative

| App | How it installs |
|---|---|
| Paint.NET (image editor) | winget `dotPDN.PaintDotNet` |
| GIMP (advanced image editor) | winget `GIMP.GIMP.3` |
| HandBrake (video converter) | winget `HandBrake.HandBrake` |
| Audacity (audio editor) | winget `Audacity.Audacity` |

### Customisation & Mods

| App | How it installs |
|---|---|
| Rainmeter (desktop widgets) | winget `Rainmeter.Rainmeter` |
| Lively Wallpaper (animated wallpapers) | winget `rocksdanister.LivelyWallpaper` |
| Mod Organizer 2 | winget `ModOrganizer2.modorganizer` |
| ReShade (post-processing filters for games) | winget `Reshade.Setup` |
| Special K (frame pacing, HDR and game fixes) | winget `SpecialK.SpecialK` |
| Overwolf (in-game overlay apps, opens page) | opens [www.overwolf.com](https://www.overwolf.com/) |
| Lossless Scaling (frame generation, opens its Steam page) | opens [store.steampowered.com](https://store.steampowered.com/app/993090/Lossless_Scaling/) |

### Redistributables

| App | How it installs |
|---|---|
| VC++ Redistributables Pack (all versions) | winget bundle |
| .NET Desktop Runtimes (8 + 9) | winget bundle |
| DirectX End-User Runtime (older game DLLs) | winget `Microsoft.DirectX` |

### Uninstall & Clean-up Tools

| App | How it installs |
|---|---|
| Bulk Crap Uninstaller (deep uninstaller, finds leftovers) | winget `Klocman.BulkCrapUninstaller` |
| Revo Uninstaller Free (deep uninstaller) | winget `RevoUninstaller.RevoUninstaller` |
| Geek Uninstaller (small, fast uninstaller) | winget `GeekUninstaller.GeekUninstaller` |
| BleachBit (clears junk and temporary files) | winget `BleachBit.BleachBit` |
| WinDirStat (what is filling your drive) | winget `WinDirStat.WinDirStat` |

### RGB & Peripheral Control

| App | How it installs |
|---|---|
| Corsair iCUE | winget `Corsair.iCUE.4` |
| Razer Synapse | winget `RazerInc.RazerInstaller.Synapse4` |
| Logitech G HUB | winget `Logitech.GHUB` |
| SteelSeries GG | winget `SteelSeries.GG` |
| OpenRGB (universal, multi-vendor) | winget `OpenRGB.OpenRGB` |
| SignalRGB (universal, multi-vendor) | winget `WhirlwindFX.SignalRgb` |
| NZXT CAM (NZXT cooling + lighting control) | winget `NZXT.CAM` |
| Logitech Options+ (MX mice and keyboards) | winget `Logitech.OptionsPlus` |
| Hyte Nexus (HYTE case/AIO control) | direct download (page: [hyte.com](https://hyte.com/)) |
| L-Connect 3 (Lian Li fan/lighting control) | direct download (page: [lian-li.com](https://lian-li.com/l-connect3/)) |

### Retro Computing

| App | How it installs |
|---|---|
| 86Box (Windows 98 VM, BYO ISO/ROMs) | winget `86Box.86Box` |
| RetroArch (all-in-one retro game emulator) | winget `Libretro.RetroArch` |
| PCSX2 (PlayStation 2 emulator, BYO games) | winget `PCSX2Team.PCSX2` |
| DOSBox-X (old DOS games) | winget `joncampbell123.DOSBox-X` |
| ScummVM (classic point-and-click adventures) | winget `ScummVM.ScummVM` |

### Console Emulators (bring your own games)

| App | How it installs |
|---|---|
| Dolphin (GameCube and Wii) | winget `DolphinEmulator.Dolphin` |
| DuckStation (PlayStation 1) | winget `Stenzek.DuckStation` |
| PPSSPP (PlayStation Portable) | winget `PPSSPPTeam.PPSSPP` |
| Cemu (Wii U) | winget `Cemu.Cemu` |
| melonDS (Nintendo DS) | winget `melonDS.melonDS` |
| Azahar (Nintendo 3DS) | winget `AzaharEmu.Azahar` |
| mGBA (Game Boy, Game Boy Color, Game Boy Advance) | winget `JeffreyPfau.mGBA` |
| xemu (original Xbox) | winget `xemu-project.xemu` |
| MAME (arcade machines) | winget `MAMEdev.MAME` |
| RPCS3 (PlayStation 3, opens download page) | opens [rpcs3.net](https://rpcs3.net/download) |
| Flycast (Dreamcast and Naomi, opens download page) | opens [github.com](https://github.com/flyinghead/flycast/releases) |
| EmulationStation Desktop Edition (one front-end for all your emulators) | winget `ES-DE.EmulationStation-DE` |
| LaunchBox (game library front-end, opens download page) | opens [www.launchbox-app.com](https://www.launchbox-app.com/download) |

### Drivers & Vendor Software

| App | How it installs |
|---|---|
| AMD Software: Adrenalin (opens download page) | opens [www.amd.com](https://www.amd.com/en/support/download/drivers.html) |
| AMD Ryzen Master (Ryzen tuning, opens download page) | opens [www.amd.com](https://www.amd.com/en/products/software/ryzen-master.html) |
| AMD Chipset Drivers (Ryzen motherboards, opens download page) | opens [www.amd.com](https://www.amd.com/en/support/download/drivers.html) |
| Intel Driver & Support Assistant (opens page) | opens [www.intel.com](https://www.intel.com/content/www/us/en/support/detect.html) |
| MSI Center (opens download page) | opens [www.msi.com](https://www.msi.com/Landing/MSI-Center) |
| ASUS Armoury Crate (opens download page) | opens [rog.asus.com](https://rog.asus.com/armoury-crate/) |
| Cinebench (opens download page) | opens [www.maxon.net](https://www.maxon.net/en/cinebench) |
| Wallpaper Engine (opens its Steam page) | opens [store.steampowered.com](https://store.steampowered.com/app/431960/Wallpaper_Engine/) |

### AI Assistants

| App | How it installs |
|---|---|
| Claude | winget `Anthropic.Claude` |
| ChatGPT | winget `9NT1R1C2HH7J` |
| LM Studio (run open AI models locally) | winget `ElementLabs.LMStudio` |
| Ollama (run open AI models locally) | winget `Ollama.Ollama` |

## Office (56 items)

### Web Browsers

| App | How it installs |
|---|---|
| Firefox | winget `Mozilla.Firefox` |
| Google Chrome | winget `Google.Chrome` |
| Brave (privacy-focused browser) | winget `Brave.Brave` |

### Chat & Meetings

| App | How it installs |
|---|---|
| Microsoft Teams | winget `Microsoft.Teams` |
| Zoom | winget `Zoom.Zoom` |
| Slack | winget `SlackTechnologies.Slack` |
| Signal (private messaging) | winget `OpenWhisperSystems.Signal` |
| Telegram | winget `Telegram.TelegramDesktop` |

### Office & Documents

| App | How it installs |
|---|---|
| Microsoft 365 Apps | winget `Microsoft.Office` |
| LibreOffice (free alternative) | winget `TheDocumentFoundation.LibreOffice` |
| Adobe Acrobat Reader (PDFs) | winget `Adobe.Acrobat.Reader.64-bit` |
| Thunderbird (email) | winget `Mozilla.Thunderbird` |
| Notepad++ | winget `Notepad++.Notepad++` |
| Obsidian (notes) | winget `Obsidian.Obsidian` |
| Google Drive | winget `Google.GoogleDrive` |
| Foxit PDF Reader | winget `Foxit.FoxitReader` |
| ONLYOFFICE Desktop Editors (free Office-style suite) | winget `ONLYOFFICE.DesktopEditors` |
| Greenshot (quick screenshots with markup) | winget `Greenshot.Greenshot` |
| KeePassXC (offline password manager) | winget `KeePassXCTeam.KeePassXC` |
| Joplin (notes that sync anywhere) | winget `Joplin.Joplin` |
| PDFgear (free PDF editor) | winget `PDFgear.PDFgear` |
| SumatraPDF (fast, light PDF reader) | winget `SumatraPDF.SumatraPDF` |
| Notion (notes + docs) | winget `Notion.Notion` |
| calibre (e-books) | winget `calibre.calibre` |

### Cloud & Sync

| App | How it installs |
|---|---|
| Dropbox | winget `Dropbox.Dropbox` |
| Nextcloud Desktop | winget `Nextcloud.NextcloudDesktop` |

### General Utilities

| App | How it installs |
|---|---|
| VS Code | winget `Microsoft.VisualStudioCode` |
| PowerToys | winget `Microsoft.PowerToys` |
| VLC Media Player | winget `VideoLAN.VLC` |
| 7-Zip | winget `7zip.7zip` |
| Bitwarden (password manager) | winget `Bitwarden.Bitwarden` |
| Everything (instant file search) | winget `voidtools.Everything` |
| WizTree (see what's filling a drive) | winget `AntibodySoftware.WizTree` |
| ShareX (screenshots + screen recording) | winget `ShareX.ShareX` |
| EarTrumpet (per-app volume mixer) | winget `File-New-Project.EarTrumpet` |
| Ditto (clipboard history) | winget `Ditto.Ditto` |
| AnyDesk (remote support) | winget `AnyDesk.AnyDesk` |
| Spotify | winget `Spotify.Spotify` |

### Security & Privacy

| App | How it installs |
|---|---|
| Malwarebytes (on-demand malware scanner) | winget `Malwarebytes.Malwarebytes` |
| Proton VPN | winget `Proton.ProtonVPN` |

### Creative

| App | How it installs |
|---|---|
| Paint.NET (image editor) | winget `dotPDN.PaintDotNet` |
| GIMP (advanced image editor) | winget `GIMP.GIMP.3` |
| Inkscape (vector graphics) | winget `Inkscape.Inkscape` |
| HandBrake (video converter) | winget `HandBrake.HandBrake` |
| Audacity (audio editor) | winget `Audacity.Audacity` |

### Redistributables

| App | How it installs |
|---|---|
| VC++ Redistributables Pack (all versions) | winget bundle |
| .NET Desktop Runtimes (8 + 9) | winget bundle |

### Uninstall & Clean-up Tools

| App | How it installs |
|---|---|
| Bulk Crap Uninstaller (deep uninstaller, finds leftovers) | winget `Klocman.BulkCrapUninstaller` |
| Revo Uninstaller Free (deep uninstaller) | winget `RevoUninstaller.RevoUninstaller` |
| Geek Uninstaller (small, fast uninstaller) | winget `GeekUninstaller.GeekUninstaller` |
| BleachBit (clears junk and temporary files) | winget `BleachBit.BleachBit` |
| WinDirStat (what is filling your drive) | winget `WinDirStat.WinDirStat` |

### RGB & Peripheral Control

| App | How it installs |
|---|---|
| Logitech Options+ (MX mice and keyboards) | winget `Logitech.OptionsPlus` |

### Drivers & Vendor Software

| App | How it installs |
|---|---|
| Intel Driver & Support Assistant (opens page) | opens [www.intel.com](https://www.intel.com/content/www/us/en/support/detect.html) |

### AI Assistants

| App | How it installs |
|---|---|
| Claude | winget `Anthropic.Claude` |
| ChatGPT | winget `9NT1R1C2HH7J` |

## Developer (99 items)

### Web Browsers

| App | How it installs |
|---|---|
| Firefox | winget `Mozilla.Firefox` |
| Google Chrome | winget `Google.Chrome` |
| Brave (privacy-focused browser) | winget `Brave.Brave` |

### Chat & Meetings

| App | How it installs |
|---|---|
| Discord | winget `Discord.Discord` |
| Slack | winget `SlackTechnologies.Slack` |

### Office & Documents

| App | How it installs |
|---|---|
| Notepad++ | winget `Notepad++.Notepad++` |
| Obsidian (notes) | winget `Obsidian.Obsidian` |
| KeePassXC (offline password manager) | winget `KeePassXCTeam.KeePassXC` |
| Joplin (notes that sync anywhere) | winget `Joplin.Joplin` |

### Cloud & Sync

| App | How it installs |
|---|---|
| Nextcloud Desktop | winget `Nextcloud.NextcloudDesktop` |

### Developer Tools

| App | How it installs |
|---|---|
| Git | winget `Git.Git` |
| GitHub CLI | winget `GitHub.cli` |
| GitHub Desktop | winget `GitHub.GitHubDesktop` |
| Windows Terminal | winget `Microsoft.WindowsTerminal` |
| Visual Studio 2022 Community (large download) | winget `Microsoft.VisualStudio.2022.Community` |
| JetBrains Toolbox | winget `JetBrains.Toolbox` |
| Postman (API testing) | winget `Postman.Postman` |
| DBeaver (database client) | winget `DBeaver.DBeaver.Community` |
| Neovim (modal text editor) | winget `Neovim.Neovim` |
| Fork (Git client) | winget `Fork.Fork` |
| Insomnia (API client) | winget `Insomnia.Insomnia` |
| WinMerge (compare files and folders) | winget `WinMerge.WinMerge` |
| MobaXterm (SSH + remote tools) | winget `Mobatek.MobaXterm` |
| CMake | winget `Kitware.CMake` |
| Visual Studio 2022 Build Tools (C++/.NET compilers) | winget `Microsoft.VisualStudio.2022.BuildTools` |
| Zed (fast code editor) | winget `ZedIndustries.Zed` |
| Cursor (AI code editor) | winget `Anysphere.Cursor` |
| Bruno (offline API client) | winget `Bruno.Bruno` |
| HxD (hex editor) | winget `MHNexus.HxD` |
| Android Studio | winget `Google.AndroidStudio` |

### Command-line Tools

| App | How it installs |
|---|---|
| lazygit (Git in the terminal) | winget `JesseDuffield.lazygit` |
| Starship (nicer shell prompt) | winget `Starship.Starship` |
| ripgrep (very fast text search) | winget `BurntSushi.ripgrep.MSVC` |
| fzf (fuzzy finder) | winget `junegunn.fzf` |
| jq (read JSON in the terminal) | winget `jqlang.jq` |

### Databases

| App | How it installs |
|---|---|
| PostgreSQL 16 | winget `PostgreSQL.PostgreSQL.16` |
| MongoDB Compass | winget `MongoDB.Compass.Full` |
| HeidiSQL | winget `HeidiSQL.HeidiSQL` |

### Languages & Runtimes

| App | How it installs |
|---|---|
| Node.js (LTS) | winget `OpenJS.NodeJS.LTS` |
| .NET SDK 8 | winget `Microsoft.DotNet.SDK.8` |
| Go | winget `GoLang.Go` |
| Rust (rustup) | winget `Rustlang.Rustup` |
| Java 21 (Temurin JDK) | winget `EclipseAdoptium.Temurin.21.JDK` |
| Deno (JavaScript/TypeScript runtime) | winget `DenoLand.Deno` |
| Bun (fast JavaScript runtime) | winget `Oven-sh.Bun` |
| uv (fast Python package manager) | winget `astral-sh.uv` |
| .NET SDK 9 | winget `Microsoft.DotNet.SDK.9` |

### Containers & Cloud

| App | How it installs |
|---|---|
| Docker Desktop | winget `Docker.DockerDesktop` |
| WSL (Windows Subsystem for Linux) | winget `Microsoft.WSL` |
| Azure CLI | winget `Microsoft.AzureCLI` |
| kubectl | winget `Kubernetes.kubectl` |
| Terraform | winget `Hashicorp.Terraform` |
| Helm (Kubernetes packages) | winget `Helm.Helm` |
| minikube (local Kubernetes) | winget `Kubernetes.minikube` |
| Bicep (Azure infrastructure) | winget `Microsoft.Bicep` |
| Vagrant | winget `Hashicorp.Vagrant` |
| Ubuntu 24.04 for WSL | winget `Canonical.Ubuntu.2404` |
| Podman Desktop (containers without Docker) | winget `RedHat.Podman-Desktop` |

### Home Lab & Network

| App | How it installs |
|---|---|
| Tailscale (private network) | winget `Tailscale.Tailscale` |
| Wireshark | winget `WiresharkFoundation.Wireshark` |
| PuTTY (SSH) | winget `PuTTY.PuTTY` |
| WinSCP (file transfer) | winget `WinSCP.WinSCP` |
| Sysinternals Suite | winget `Microsoft.Sysinternals.Suite` |
| VirtualBox | winget `Oracle.VirtualBox` |
| Balena Etcher (flash USB/SD images) | winget `Balena.Etcher` |
| Rufus (bootable USB drives) | winget `Rufus.Rufus` |
| Ventoy (multi-boot USB) | winget `Ventoy.Ventoy` |
| WireGuard (VPN) | winget `WireGuard.WireGuard` |
| Nmap (network scanner) | winget `Insecure.Nmap` |
| Advanced IP Scanner | winget `Famatech.AdvancedIPScanner` |
| Angry IP Scanner | winget `angryziber.AngryIPScanner` |
| Syncthing (private file sync) | winget `Syncthing.Syncthing` |

### General Utilities

| App | How it installs |
|---|---|
| VS Code | winget `Microsoft.VisualStudioCode` |
| PowerShell 7 | winget `Microsoft.PowerShell` |
| Python 3 | winget `Python.Python.3.13` |
| PowerToys | winget `Microsoft.PowerToys` |
| VLC Media Player | winget `VideoLAN.VLC` |
| 7-Zip | winget `7zip.7zip` |
| Bitwarden (password manager) | winget `Bitwarden.Bitwarden` |
| Everything (instant file search) | winget `voidtools.Everything` |
| WizTree (see what's filling a drive) | winget `AntibodySoftware.WizTree` |
| ShareX (screenshots + screen recording) | winget `ShareX.ShareX` |
| EarTrumpet (per-app volume mixer) | winget `File-New-Project.EarTrumpet` |
| Ditto (clipboard history) | winget `Ditto.Ditto` |
| AutoHotkey (automation scripts) | winget `AutoHotkey.AutoHotkey` |

### Security & Privacy

| App | How it installs |
|---|---|
| Malwarebytes (on-demand malware scanner) | winget `Malwarebytes.Malwarebytes` |

### Redistributables

| App | How it installs |
|---|---|
| VC++ Redistributables Pack (all versions) | winget bundle |
| .NET Desktop Runtimes (8 + 9) | winget bundle |

### Uninstall & Clean-up Tools

| App | How it installs |
|---|---|
| Bulk Crap Uninstaller (deep uninstaller, finds leftovers) | winget `Klocman.BulkCrapUninstaller` |
| Revo Uninstaller Free (deep uninstaller) | winget `RevoUninstaller.RevoUninstaller` |
| Geek Uninstaller (small, fast uninstaller) | winget `GeekUninstaller.GeekUninstaller` |
| BleachBit (clears junk and temporary files) | winget `BleachBit.BleachBit` |
| WinDirStat (what is filling your drive) | winget `WinDirStat.WinDirStat` |

### Drivers & Vendor Software

| App | How it installs |
|---|---|
| Intel Driver & Support Assistant (opens page) | opens [www.intel.com](https://www.intel.com/content/www/us/en/support/detect.html) |

### AI Assistants

| App | How it installs |
|---|---|
| Claude | winget `Anthropic.Claude` |
| ChatGPT | winget `9NT1R1C2HH7J` |
| Claude Code (AI coding in the terminal) | winget `Anthropic.ClaudeCode` |
| LM Studio (run open AI models locally) | winget `ElementLabs.LMStudio` |
| Ollama (run open AI models locally) | winget `Ollama.Ollama` |

---
_223 entries in total. Some appear in more than one setup type._
