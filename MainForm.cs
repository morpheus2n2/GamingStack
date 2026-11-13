using System.Diagnostics;
using System.Management;
using System.Media;

namespace GamingStackGUI
{
    /// <summary>
    /// Flow: Bios -> Boot (original animation) -> Desktop (a small retro "OS": windows,
    /// Start menu, icons, apps) -> Terminal (the installer wizard window, then the real
    /// installer, via InstallerEngine). The class is split across MainForm*.cs files by topic.
    /// Pressing Delete during the Bios stage detours into a joke Bsod stage for a few
    /// seconds, then carries on into Boot as normal.
    /// </summary>
    public partial class MainForm : Form
    {
        private enum Stage { Bios, Bsod, Boot, Desktop, Terminal }

        // The interactive Q&A inside the Terminal stage, before/instead of the real
        // install run. See HandleWizardKey for the full flow between these.
        private enum WizardPhase
        {
            ChooseProfile,       // "[1] Gaming [2] Office [3] Developer" - the very first screen
            ConfirmAll,          // "Install everything shown? Y/N"
            ChooseIndividually,  // "Want to choose what's installed? Y/N"
            PerApp,              // asking Y/N for one catalog entry at a time
            ConfirmQuit,         // "Just want to quit? Y/N"
            EasterEgg,           // declined everything - a little joke, then Farewell
            ConfirmBackup,       // "Full disk-image backup: before / after / skip?" -
                                 // shown once app selection is finalized, before
                                 // ConfirmTweaks. Restore point always happens
                                 // regardless of the answer here.
            SelectBackupDrive,   // shown only if ConfirmBackup wasn't "skip" - pick a
                                 // detected drive, or back out and skip
            PreparingBackup,     // restore point + (if "before") the image backup;
                                 // blocking, auto-advances to ChoosePowerPlan when done
            ChoosePowerPlan,     // "[1] High performance [2] Ultimate Performance" -
                                 // shown once, right before ConfirmTweaks, since the
                                 // power plan tweak needs to know which one to preview
            ChooseDns,           // "[1] Keep mine [2] Cloudflare [3] Google [4] Quad9" -
                                 // optional DNS switch, defaults to leaving it alone
            ChooseDebloat,       // tick/untick preinstalled Store apps to remove; only
                                 // shown if the scan found any. Nothing ticked by default.
            Credits,             // short scrolling credits, between Summary and ShuttingDown
            FinalReview,         // the point of no return: everything that is about to happen, in one
                                 // list. Needs the word GO typed and Enter - not just a single key.
            ConfirmTweaks,       // "Apply these tweaks? Y/N" - shown once app
                                 // selection is finalized, before Installing. Never
                                 // skipped: tweaks only ever run after an explicit yes.
            Installing,          // InstallerEngine is actually running
            RunningBackup,       // the "after" image backup, run once Installing
                                 // finishes and before Summary - only reached if
                                 // ConfirmBackup's answer was "after"
            Summary,             // end-of-run report (installed/failed/skipped), shown before ShuttingDown
            ShuttingDown,        // fake "shutting down" outro, auto-advances to Farewell
            Farewell             // final thank-you screen (reached from ShuttingDown or EasterEgg)
        }

        private const int BiosMinHoldMs = 13000;
        private const int BsodHoldMs = 7000;
        private const int BootHoldMs = 10000;
        private const int SoundTriggerMs = 8000;
        private const int MemCountDurationMs = 3000;
        private const int DesktopHoldMs = 7000;
        private const int TerminalOpenMs = 400;
        private const int EasterEggHoldMs = 5000;
        private const long SpeedrunThresholdMs = 90000; // "Speedrunner" achievement cutoff

        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
        private readonly Stopwatch _stageWatch = new();
        private readonly Stopwatch _phaseWatch = new();

        private Stage _stage = Stage.Bios;

        // ---- BIOS timeline ----
        private class TimedLine
        {
            public long At;
            public string Text = "";
            public Color Color;
        }

        private readonly List<TimedLine> _timedLines = new();
        private long _memCountAt;
        private long _targetRamGb;
        private long _footerAt;
        private bool _postBeepPlayed;
        private bool _memBeepDone;
        private bool _footerBeepPlayed;
        private long _lastMemBeepAt = -1000;

        private bool _soundFired;
        private SoundPlayer? _soundPlayer;
        private Bitmap? _wallpaper;   // the pixel-art desk scene, screen-sized

        // The boot artwork (sky, mountains, clouds, flag) is drawn procedurally by
        // BootScene at low resolution and scaled up - no image files involved.
        private BootScene? _bootScene;

        private readonly Font _mono = Ui.Font(14f, FontStyle.Regular);
        private readonly Font _monoSmall = Ui.Font(12f, FontStyle.Regular);
        private readonly Font _monoTiny = Ui.Font(10f, FontStyle.Regular);              // used when the app list is tight
        private readonly Font _headingTiny = Ui.Font(10f, FontStyle.Bold | FontStyle.Underline);
        private readonly Font _headingFont = Ui.Font(12f, FontStyle.Bold | FontStyle.Underline);
        private readonly Font _monoLarge = Ui.Font(22f, FontStyle.Bold);

        // ---- installer + wizard ----
        private readonly InstallerEngine _installer = new();
        private readonly List<string> _terminalLines = new();
        private readonly object _terminalLock = new();

        private WizardPhase _wizardPhase = WizardPhase.ChooseProfile;
        // Chosen on the first wizard screen; decides the catalog and which tweaks apply.
        private SetupProfile _profile = SetupProfile.Gaming;
        private List<AppEntry> _catalog = InstallerEngine.CatalogFor(SetupProfile.Gaming);
        // Stopwatch time at which the app list starts typing (reset when a profile is chosen).
        private long _typingStartMs = TerminalOpenMs;
        private readonly List<AppEntry> _selectedApps = new();
        private int _perAppIndex;
        private bool?[] _perAppDecisions = Array.Empty<bool?>();

        // ---- tweak consent (ConfirmTweaks phase) ----
        // Populated from InstallerEngine.PreviewTweaks() right before showing the
        // "Apply these tweaks?" screen, so what's on screen is always the real
        // current value, not a guess. _applyTweaks is the user's actual Y/N answer -
        // ApplyTweaks() in InstallerEngine never runs without it being true.
        private List<InstallerEngine.TweakPreview> _tweakPreview = new();
        private bool _applyTweaks;
        private string _finalTyped = "";   // what has been typed on the FinalReview screen so far
        // Answered once on the ChoosePowerPlan screen, right before ConfirmTweaks -
        // defaults to High (the safe, always-available choice) so a bug in wiring the
        // ChoosePowerPlan phase in can never silently end up on Ultimate.
        private InstallerEngine.PowerPlanChoice _powerPlanChoice = InstallerEngine.PowerPlanChoice.High;
        // DNS switch: Skip unless the user picks a provider on the ChooseDns screen.
        private DnsChoice _dnsChoice = DnsChoice.Skip;
        private string? _buildSheetPath;
        // Debloat: what the scan found installed, and which of those the user ticked.
        // Starts empty (nothing is removed unless explicitly ticked).
        private IReadOnlyList<DebloatItem> _debloatOffered = Array.Empty<DebloatItem>();
        private readonly HashSet<string> _debloatTicked = new();
        private TweakOptions CurrentTweakOptions =>
            new(_powerPlanChoice, _dnsChoice, _debloatTicked.ToList(), _profile);

        // ---- restore point + optional full image backup ----
        // The restore point always runs, no consent screen needed for it (see
        // BackupEngine.CreateRestorePoint) - only the full image backup is a
        // choice, and that choice is made once, right after app selection is
        // finalized. _backupDriveOptions is a live read (BackupEngine.FindEligibleDrives)
        // taken right before showing SelectBackupDrive, same "real value, not a
        // guess" principle as the tweaks screen.
        private readonly BackupEngine _backup = new();
        private BackupTiming _backupTiming = BackupTiming.Skip;
        private string? _backupDrive;
        private List<DriveOption> _backupDriveOptions = new();
        private bool _noEligibleBackupDrive;

        // Per-item outcomes for the Summary screen, populated via InstallerEngine's
        // OnItemResult (fired on a background task, hence the lock) rather than by
        // parsing the free-text terminal log.
        private readonly List<ItemResult> _itemResults = new();
        private readonly object _itemResultLock = new();

        // Sized from the actual title font metrics (+8px padding) rather than a guessed
        // constant, so the bar is always at least a few px taller than the text it
        // holds - a hardcoded height was clipping/crowding the title on some displays.
        private readonly int _titleBarHeight;

        public MainForm()
        {
            Text = "GamingStack";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.Black;
            DoubleBuffered = true;

            using (var measureBmp = new Bitmap(1, 1))
            using (var measureG = Graphics.FromImage(measureBmp))
            {
                var textHeight = measureG.MeasureString("Gg", _mono).Height;
                _titleBarHeight = (int)Math.Ceiling(textHeight) + 8;
            }

            _installer.OnLog += line =>
            {
                lock (_terminalLock)
                {
                    _terminalLines.Add(line);
                    if (_terminalLines.Count > 300) _terminalLines.RemoveAt(0);
                }
            };
            _backup.OnLog += line =>
            {
                lock (_terminalLock)
                {
                    _terminalLines.Add(line);
                    if (_terminalLines.Count > 300) _terminalLines.RemoveAt(0);
                }
            };
            _installer.OnItemResult += result =>
            {
                lock (_itemResultLock) { _itemResults.Add(result); }

                // Achievement checks - these fire from the installer's background
                // task, hence going through UnlockAchievement's own lock rather than
                // touching the toast queue directly.
                if (result.Kind == InstallResultKind.Installed)
                    UnlockAchievement("first_blood");

                if (result.Kind == InstallResultKind.Failed)
                    UnlockAchievement("not_meant_to_be");

                if (result.Kind == InstallResultKind.Skipped && result.Detail != null &&
                    result.Detail.Contains("already", StringComparison.OrdinalIgnoreCase) &&
                    result.Detail.Contains("up to date", StringComparison.OrdinalIgnoreCase))
                    UnlockAchievement("already_perfect");

                if (result.Entry.FriendlyName.Contains("VC++ Redistributables", StringComparison.OrdinalIgnoreCase) &&
                    result.Kind != InstallResultKind.Failed)
                    UnlockAchievement("redistributable_rampage");
            };
            _installer.OnFinished += () =>
            {
                // Checked against _phaseWatch before it gets restarted below - at this
                // point it's been running since StartInstall() kicked the Installing
                // phase off, so its elapsed time is the whole install run's duration.
                if (_selectedApps.Count > 0 && _phaseWatch.ElapsedMilliseconds < SpeedrunThresholdMs)
                    UnlockAchievement("speedrunner");

                ItemResult[] finalResults;
                lock (_itemResultLock) { finalResults = _itemResults.ToArray(); }
                ProgressStore.AddInstalled(finalResults.Count(r => r.Kind == InstallResultKind.Installed));
                _buildSheetPath = BuildSheet.Write(finalResults, _installer.TweaksApplied,
                    _installer.TweaksRevertFilePath, _installer.LogFilePath, _dnsChoice,
                    _applyTweaks ? _debloatTicked.Count : 0, SetupProfileInfo.Title(_profile));

                if (_backupTiming == BackupTiming.After && _backupDrive != null)
                {
                    _wizardPhase = WizardPhase.RunningBackup;
                    _phaseWatch.Restart();
                    _ = RunAfterBackupAsync();
                }
                else
                {
                    _wizardPhase = WizardPhase.Summary;
                    _phaseWatch.Restart();
                }
            };

            _timer.Tick += (_, _) =>
            {
                Advance();
                Invalidate();
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // Borderless + WindowState.Maximized doesn't reliably cover the entire
            // physical screen on every DPI/multi-monitor setup, and separately, the
            // real Windows taskbar is an "always on top" window that can still render
            // above our own drawn one at the bottom of the screen even when our bounds
            // are correct. Fix both: size explicitly to the screen the user is actually
            // on (based on where the cursor is, which is more reliable than the
            // window's undefined initial position), and mark the form TopMost so it
            // sits above the real taskbar instead of getting cut off behind it.
            var screen = Screen.FromPoint(Cursor.Position);
            StartPosition = FormStartPosition.Manual;
            WindowState = FormWindowState.Normal;
            Bounds = screen.Bounds;
            TopMost = true;

            LoadEmbeddedSound();
            LoadWallpaper();
            BuildBiosTimeline();
            if (ProgressStore.StartSession()) UnlockAchievement("welcome_back");
            if (ProgressStore.Streak >= 3) UnlockAchievement("on_a_roll");
            if (ProgressStore.Runs >= 7) UnlockAchievement("regular");
            _stageWatch.Restart();
            _timer.Start();
        }

        // ---------- stage progression ----------

        private long _lastWindowTick = Environment.TickCount64;

        private void Advance()
        {
            var nowTick = Environment.TickCount64;
            TickWindows(nowTick - _lastWindowTick);
            _lastWindowTick = nowTick;
            UpdateToasts();
            CheckScreensaver();

            switch (_stage)
            {
                case Stage.Bios:
                    var elapsed = _stageWatch.ElapsedMilliseconds;
                    var memCountEndAt = _memCountAt + MemCountDurationMs;

                    // Sounds go through the software mixer (RetroAudio), so they play on any
                    // sound card - Console.Beep needs a motherboard speaker most PCs lack.
                    if (!_postBeepPlayed && elapsed >= 250)
                    {
                        _postBeepPlayed = true;
                        PlayBios(a => a.SpinUp());
                    }

                    // Little drive-seek clicks while the BIOS "detects" things.
                    if (elapsed >= 900 && elapsed < memCountEndAt && elapsed - _lastSeekAt >= 650)
                    {
                        _lastSeekAt = elapsed + _typeRng.Next(0, 500);
                        PlayBios(a => a.Seek());
                    }

                    if (elapsed >= _memCountAt && elapsed < memCountEndAt)
                    {
                        if (elapsed - _lastMemBeepAt >= 180)
                        {
                            _lastMemBeepAt = elapsed;
                            PlayBios(a => a.MemTick());
                        }
                    }
                    else if (elapsed >= memCountEndAt && !_memBeepDone)
                    {
                        _memBeepDone = true;
                        PlayBios(a => a.DoneBeep());
                    }

                    if (elapsed >= _footerAt && !_footerBeepPlayed)
                    {
                        _footerBeepPlayed = true;
                        PlayBios(a => a.PostBeep());   // the classic single "all OK" beep
                    }

                    var holdTarget = Math.Max(BiosMinHoldMs, _footerAt + 300);
                    if (elapsed >= holdTarget)
                    {
                        _stage = Stage.Boot;
                        _stageWatch.Restart();
                        _soundFired = false;
                    }
                    break;

                case Stage.Bsod:
                    if (_stageWatch.ElapsedMilliseconds >= BsodHoldMs)
                    {
                        _stage = Stage.Boot;
                        _stageWatch.Restart();
                        _soundFired = false;
                    }
                    break;

                case Stage.Boot:
                    var bootElapsed = _stageWatch.ElapsedMilliseconds;
                    if (!_soundFired && bootElapsed >= SoundTriggerMs)
                    {
                        _soundFired = true;
                        try { _soundPlayer?.Play(); } catch { /* non-fatal */ }
                    }
                    if (bootElapsed >= BootHoldMs)
                    {
                        _stage = Stage.Desktop;
                        _stageWatch.Restart();
                    }
                    break;

                case Stage.Desktop:
                    // After a few seconds the installer opens by itself - unless you have
                    // already opened something else to play with, in which case it waits
                    // for you (Start menu, the Gaming/Office/Developer icons, or SPACE).
                    if (_stageWatch.ElapsedMilliseconds >= DesktopHoldMs && _windows.Count == 0)
                        EnterTerminalStage();
                    break;

                case Stage.Terminal:

                    // Typewriter sound: works out how many new characters appeared since
                    // the last tick (same maths as DrawWizard's reveal) and plays one key
                    // clack per ~2 letters, spread across the frame so a fast burst
                    // sounds like a rattle of separate keys rather than one smear. A bell
                    // rings once when the whole list has finished typing out.
                    if (IsWizardTypingPhase(_wizardPhase))
                    {
                        var typingElapsedMs = Math.Max(0, _stageWatch.ElapsedMilliseconds - _typingStartMs);
                        var typedBudget = (int)(typingElapsedMs / 1000.0 * WizardTypeCharsPerSecond);
                        var total = GetWizardTotalChars();
                        var clamped = Math.Min(typedBudget, total);
                        if (clamped > _wizardCharsRevealed)
                        {
                            var newChars = clamped - _wizardCharsRevealed;
                            _wizardCharsRevealed = clamped;
                            var heard = _windows.Any(w => w.Id == "installer" && !w.Minimized);   // silent if the window is closed or minimised
                            if (heard) PlayTypewriterKeys(newChars);
                            if (clamped >= total && !_typewriterBellRung)
                            {
                                _typewriterBellRung = true;
                                if (heard) _audio?.Bell();
                            }
                        }
                    }

                    if (_wizardPhase == WizardPhase.EasterEgg &&
                        _phaseWatch.ElapsedMilliseconds >= EasterEggHoldMs)
                    {
                        _wizardPhase = WizardPhase.Farewell;
                        _phaseWatch.Restart();
                    }

                    if (_wizardPhase == WizardPhase.Credits &&
                        _phaseWatch.ElapsedMilliseconds >= CreditsAutoAdvanceMs)
                    {
                        _wizardPhase = WizardPhase.ShuttingDown;
                        _phaseWatch.Restart();
                    }

                    // The shutdown outro is a beat, not a question - it plays itself
                    // out and moves on to Farewell on its own, no keypress needed.
                    if (_wizardPhase == WizardPhase.ShuttingDown &&
                        _phaseWatch.ElapsedMilliseconds >= ShutdownAutoAdvanceMs)
                    {
                        _wizardPhase = WizardPhase.Farewell;
                        _phaseWatch.Restart();
                    }
                    break;
            }
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.Clear(Color.Black);

            switch (_stage)
            {
                case Stage.Bios:
                {
                    var st = g.Save();
                    g.ScaleTransform((float)UiScale, (float)UiScale);
                    PaintBios(g);
                    g.Restore(st);
                    DrawScanlines(g);          // real pixels, so the lines stay fine
                    break;
                }
                case Stage.Bsod: PaintBsod(g); break;
                case Stage.Boot: PaintBoot(g); break;
                case Stage.Desktop: PaintShell(g); break;
                case Stage.Terminal: PaintShell(g); break;
            }
        }

        // ---------- keyboard + closing ----------

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (WakeFromScreensaver()) return;     // the key that wakes the screensaver does nothing else
            _lastInputTick = Environment.TickCount64;

            CheckKonamiCode(e.KeyCode);

            if (e.KeyCode == Keys.Space && _stage is Stage.Bios or Stage.Bsod or Stage.Boot)
            {
                SkipIntro();
                return;
            }

            if (_stage == Stage.Bios && e.KeyCode == Keys.Delete)
            {
                _stage = Stage.Bsod;
                _stageWatch.Restart();
                return;
            }

            // The focused window gets the key first (installer wizard, Snake...).
            if (RouteKeyToWindow(e.KeyCode)) return;

            // On the desktop with nothing open, SPACE opens the installer straight away.
            if (_stage == Stage.Desktop && e.KeyCode == Keys.Space && _windows.Count == 0)
            {
                EnterTerminalStage();
                return;
            }

            if (e.KeyCode == Keys.Escape && !IsBusyPhase) Close();
        }

        // While backups or installs are running, closing would kill them half-done.
        private bool IsBusyPhase =>
            _stage == Stage.Terminal &&
            _wizardPhase is WizardPhase.PreparingBackup or WizardPhase.Installing or WizardPhase.RunningBackup;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (IsBusyPhase && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }


        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _soundPlayer?.Dispose();
            _audio?.Dispose();
            _wallpaper?.Dispose();
            _bootScene?.Dispose();
            _biosLogo?.Dispose();
            _icons?.Dispose();
            _scanlineBrush?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
