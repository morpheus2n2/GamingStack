using System.Management;
using System.Media;
using System.Reflection;

namespace GamingStackGUI
{
    // The installer wizard: its flow (HandleWizardKey) and every screen it draws.
    public partial class MainForm
    {
        // ---------- install wizard ----------

        // Reads the real current value of every tweak (via InstallerEngine.PreviewTweaks -
        // a pure read, nothing changes here) and shows the consent screen built from
        // it. Reached only from ChoosePowerPlan, so the power-plan tweak's preview
        // already knows which plan to show as the target - tweaks never run without
        // this screen being shown first.
        private void GoToConfirmTweaks()
        {
            _tweakPreview = InstallerEngine.PreviewTweaks(CurrentTweakOptions);
            // Ticked debloat items are shown on the consent screen too, so nothing is
            // removed that isn't listed right before the final yes/no.
            foreach (var item in _debloatOffered.Where(d => _debloatTicked.Contains(d.Id)))
                _tweakPreview.Add(new InstallerEngine.TweakPreview($"Remove app: {item.Name}", "installed", "removed (reinstallable)"));
            _wizardPhase = WizardPhase.ConfirmTweaks;
        }

        private void GoToChooseDns()
        {
            _wizardPhase = WizardPhase.ChooseDns;
        }

        // Scan is a quick registry read. If nothing removable is installed the
        // screen is skipped entirely.
        private void GoToChooseDebloatOrConfirm()
        {
            _debloatTicked.Clear();
            _debloatOffered = InstallerEngine.ScanDebloat();
            if (_debloatOffered.Count == 0) GoToConfirmTweaks();
            else _wizardPhase = WizardPhase.ChooseDebloat;
        }

        // One question, asked once, right before ConfirmTweaks: which power plan the
        // "Power plan" tweak row should target. Ultimate Performance is a real,
        // Microsoft-documented hidden plan (same idea as High Performance, just with
        // the last few power-saving throttles removed) - not on by default because it
        // has no benefit on laptops and a small idle-power cost on desktops.
        private void GoToChoosePowerPlan()
        {
            _wizardPhase = WizardPhase.ChoosePowerPlan;
        }

        // First stop once app selection is finalized, before ChoosePowerPlan - the
        // restore point + optional full image backup happen ahead of the tweaks
        // question, since they're a safety net for everything that follows,
        // installs included, not just the tweaks themselves.
        private void GoToConfirmBackup()
        {
            _backupTiming = BackupTiming.Skip;
            _backupDrive = null;
            _noEligibleBackupDrive = false;
            _wizardPhase = WizardPhase.ConfirmBackup;
        }

        // Only reached if ConfirmBackup's answer was "before" or "after" - a fresh
        // read of what's actually plugged in right now (nothing changes here).
        // An empty result skips cleanly with a clear reason shown on the next
        // screen, rather than the wizard getting stuck with nothing to pick.
        private void GoToSelectBackupDriveOrSkip()
        {
            _backupDriveOptions = BackupEngine.FindEligibleDrives();
            if (_backupDriveOptions.Count == 0)
            {
                _noEligibleBackupDrive = true;
                _backupTiming = BackupTiming.Skip;
                GoToPreparingBackup();
            }
            else
            {
                _noEligibleBackupDrive = false;
                _wizardPhase = WizardPhase.SelectBackupDrive;
            }
        }

        // Blocking step: always creates the restore point, then (only if the
        // timing chosen was "before") runs the full image backup to completion
        // before moving on - a backup racing against the installs that follow it
        // would capture a half-changed system, not the clean "before" snapshot the
        // user asked for.
        private void GoToPreparingBackup()
        {
            _wizardPhase = WizardPhase.PreparingBackup;
            _phaseWatch.Restart();
            _ = RunPreparingBackupAsync();
        }

        private async Task RunPreparingBackupAsync()
        {
            await Task.Run(() => _backup.CreateRestorePoint());

            if (_backupTiming == BackupTiming.Before && _backupDrive != null)
                await _backup.RunImageBackupAsync(_backupDrive);

            GoToChoosePowerPlan();
        }

        // The "after" counterpart - only reached from the OnFinished handler once
        // Installing (apps + tweaks) is fully done.
        private async Task RunAfterBackupAsync()
        {
            if (_backupDrive != null)
                await _backup.RunImageBackupAsync(_backupDrive);

            _wizardPhase = WizardPhase.Summary;
            _phaseWatch.Restart();
        }

        private void StartInstall()
        {
            lock (_itemResultLock) { _itemResults.Clear(); }
            // remembered so the first screen can offer "repeat last setup" next time
            ProgressStore.SaveLastSetup(_profile.ToString(), _selectedApps.Select(a => a.FriendlyName));
            _wizardPhase = WizardPhase.Installing;
            _phaseWatch.Restart();
            _ = RunInstallerSafelyAsync();
        }

        private async Task RunInstallerSafelyAsync()
        {
            try
            {
                var context = new RunContext(SetupProfileInfo.Title(_profile), BackupSummary());
                await _installer.RunAsync(_selectedApps, _applyTweaks, CurrentTweakOptions, context);
            }
            catch (Exception ex)
            {
                lock (_terminalLock) { _terminalLines.Add($"FATAL: {ex.Message}"); }
                _wizardPhase = WizardPhase.Farewell;
                _phaseWatch.Restart();
            }
        }

        // 1/2/3 (top row or numpad) as a plain 0-based index - shared by
        // ConfirmBackup's three options and SelectBackupDrive's drive list, since
        // neither of those is a Y/N question.
        private static int? DigitIndex(Keys key) => key switch
        {
            Keys.D1 or Keys.NumPad1 => 0,
            Keys.D2 or Keys.NumPad2 => 1,
            Keys.D3 or Keys.NumPad3 => 2,
            Keys.D4 or Keys.NumPad4 => 3,
            Keys.D5 or Keys.NumPad5 => 4,
            Keys.D6 or Keys.NumPad6 => 5,
            Keys.D7 or Keys.NumPad7 => 6,
            Keys.D8 or Keys.NumPad8 => 7,
            Keys.D9 or Keys.NumPad9 => 8,
            _ => null
        };

        private void HandleWizardKey(Keys key)
        {
            // Any key dismisses the summary - it's a report, not a Y/N question.
            // Leads into the shutdown outro rather than straight to Farewell.
            if (_wizardPhase == WizardPhase.Summary)
            {
                _wizardPhase = WizardPhase.Credits;
                _phaseWatch.Restart();
                return;
            }

            if (_wizardPhase == WizardPhase.ChooseProfile)
            {
                var pick = DigitIndex(key);
                if (pick == 0) SelectProfile(SetupProfile.Gaming);
                else if (pick == 1) SelectProfile(SetupProfile.Office);
                else if (pick == 2) SelectProfile(SetupProfile.Developer);
                else if (key == Keys.R) RepeatLastSetup();
                return;
            }

            // Neither of these is a Y/N question, so they're handled before the
            // Y/N shortcut below intercepts anything that isn't Y or N.
            if (_wizardPhase == WizardPhase.ConfirmBackup)
            {
                var choice = DigitIndex(key);
                if (choice == 0) { _backupTiming = BackupTiming.Before; GoToSelectBackupDriveOrSkip(); }
                else if (choice == 1) { _backupTiming = BackupTiming.After; GoToSelectBackupDriveOrSkip(); }
                else if (choice == 2 || key == Keys.N) { _backupTiming = BackupTiming.Skip; GoToPreparingBackup(); }
                return;
            }

            if (_wizardPhase == WizardPhase.SelectBackupDrive)
            {
                if (key == Keys.N)
                {
                    _backupTiming = BackupTiming.Skip;
                    GoToPreparingBackup();
                    return;
                }
                var index = DigitIndex(key);
                if (index != null && index < _backupDriveOptions.Count)
                {
                    _backupDrive = _backupDriveOptions[index.Value].RootPath;
                    GoToPreparingBackup();
                }
                return;
            }

            if (_wizardPhase == WizardPhase.ChoosePowerPlan)
            {
                var choice = DigitIndex(key);
                if (choice == 0) { _powerPlanChoice = InstallerEngine.PowerPlanChoice.High; GoToChooseDns(); }
                else if (choice == 1) { _powerPlanChoice = InstallerEngine.PowerPlanChoice.Ultimate; GoToChooseDns(); }
                return;
            }

            if (_wizardPhase == WizardPhase.ChooseDns)
            {
                var choice = DigitIndex(key);
                if (choice is >= 0 and <= 3)
                {
                    _dnsChoice = choice switch
                    {
                        1 => DnsChoice.Cloudflare,
                        2 => DnsChoice.Google,
                        3 => DnsChoice.Quad9,
                        _ => DnsChoice.Skip
                    };
                    GoToChooseDebloatOrConfirm();
                }
                return;
            }

            if (_wizardPhase == WizardPhase.ChooseDebloat)
            {
                if (key == Keys.Enter) { GoToConfirmTweaks(); return; }
                var index = DigitIndex(key);
                if (index != null && index < _debloatOffered.Count)
                {
                    var id = _debloatOffered[index.Value].Id;
                    if (!_debloatTicked.Remove(id)) _debloatTicked.Add(id);
                }
                return;
            }

            if (_wizardPhase == WizardPhase.FinalReview)
            {
                if (key == Keys.G && _finalTyped.Length == 0) _finalTyped = "G";
                else if (key == Keys.O && _finalTyped == "G") _finalTyped = "GO";
                else if (key == Keys.Back && _finalTyped.Length > 0) _finalTyped = _finalTyped[..^1];
                else if (key == Keys.Enter && _finalTyped == "GO") StartInstall();
                else if (key == Keys.N || key == Keys.Escape)
                {
                    // back out: nothing has run, so go back to the first question
                    _selectedApps.Clear();
                    _finalTyped = "";
                    _wizardPhase = WizardPhase.ChooseProfile;
                }
                return;
            }

            if (_wizardPhase == WizardPhase.Installing)
            {
                if (key == Keys.C) _installer.RequestCancel();
                return;
            }

            if (_wizardPhase == WizardPhase.Credits)
            {
                // Any key skips the credits.
                _wizardPhase = WizardPhase.ShuttingDown;
                _phaseWatch.Restart();
                return;
            }

            bool? yn = key switch
            {
                Keys.Y => true,
                Keys.N => false,
                _ => null
            };
            if (yn == null) return;
            var answer = yn.Value;

            switch (_wizardPhase)
            {
                case WizardPhase.ConfirmAll:
                    if (answer)
                    {
                        _selectedApps.Clear();
                        _selectedApps.AddRange(_catalog);
                        UnlockAchievement("completionist");
                        GoToConfirmBackup();
                    }
                    else
                    {
                        _wizardPhase = WizardPhase.ChooseIndividually;
                    }
                    break;

                case WizardPhase.ChooseIndividually:
                    if (answer)
                    {
                        _selectedApps.Clear();
                        _perAppIndex = 0;
                        _perAppDecisions = new bool?[_catalog.Count];
                        _wizardPhase = WizardPhase.PerApp;
                    }
                    else
                    {
                        _wizardPhase = WizardPhase.ConfirmQuit;
                    }
                    break;

                case WizardPhase.PerApp:
                    _perAppDecisions[_perAppIndex] = answer;
                    if (answer) _selectedApps.Add(_catalog[_perAppIndex]);
                    _perAppIndex++;
                    if (_perAppIndex >= _catalog.Count)
                        GoToConfirmBackup();
                    break;

                case WizardPhase.ConfirmTweaks:
                    _applyTweaks = answer;
                    _finalTyped = "";
                    _wizardPhase = WizardPhase.FinalReview;
                    break;

                case WizardPhase.ConfirmQuit:
                    if (answer)
                    {
                        Close();
                    }
                    else
                    {
                        _wizardPhase = WizardPhase.EasterEgg;
                        _phaseWatch.Restart();
                    }
                    break;
            }
        }

        // ---- Terminal (opens on top of the desktop, runs the interactive wizard then the installer) ----

        // Content of the installer window (the wizard / installer log / summary...). The
        // window chrome - title bar, buttons, frame - is drawn by the shell.
        private void DrawInstallerClient(Graphics g, Rectangle client)
        {
            var win = _windows.FirstOrDefault(w => w.Id == "installer");
            if (win == null) return;
            var fullX = win.Bounds.X; var fullY = win.Bounds.Y;
            var fullW = win.Bounds.Width; var fullH = win.Bounds.Height;
            var titleHeight = client.Y - win.Bounds.Y;

            switch (_wizardPhase)
            {
                case WizardPhase.ConfirmBackup:
                    DrawConfirmBackup(g, fullX, fullY, fullW, fullH, titleHeight);
                    break;
                case WizardPhase.SelectBackupDrive:
                    DrawSelectBackupDrive(g, fullX, fullY, fullW, fullH, titleHeight);
                    break;
                case WizardPhase.PreparingBackup:
                    DrawBackupProgress(g, fullX, fullY, fullW, fullH, titleHeight,
                        "Preparing your PC - this can take a while, please don't turn off your computer.");
                    break;
                case WizardPhase.ChooseProfile:
                    DrawChooseProfile(g, fullX, fullY, fullW, fullH, titleHeight);
                    break;
                case WizardPhase.ChoosePowerPlan:
                    DrawChoosePowerPlan(g, fullX, fullY, fullW, fullH, titleHeight);
                    break;
                case WizardPhase.ChooseDns:
                    DrawChooseDns(g, fullX, fullY, fullW, fullH, titleHeight);
                    break;
                case WizardPhase.ChooseDebloat:
                    DrawChooseDebloat(g, fullX, fullY, fullW, fullH, titleHeight);
                    break;
                case WizardPhase.Credits:
                    DrawCredits(g, fullX, fullY, fullW, fullH, titleHeight, _phaseWatch.ElapsedMilliseconds);
                    break;
                case WizardPhase.ConfirmTweaks:
                    DrawConfirmTweaks(g, fullX, fullY, fullW, fullH, titleHeight);
                    break;
                case WizardPhase.FinalReview:
                    DrawFinalReview(g, fullX, fullY, fullW, fullH, titleHeight);
                    break;
                case WizardPhase.Installing:
                    DrawInstallerLog(g, fullX, fullY, fullW, fullH, titleHeight, _stageWatch.ElapsedMilliseconds);
                    break;
                case WizardPhase.RunningBackup:
                    DrawBackupProgress(g, fullX, fullY, fullW, fullH, titleHeight,
                        "Running your full disk-image backup - this can take a while, please don't turn off your computer.");
                    break;
                case WizardPhase.Summary:
                    DrawSummary(g, fullX, fullY, fullW, fullH, titleHeight);
                    break;
                case WizardPhase.ShuttingDown:
                    DrawShuttingDown(g, fullX, fullY, fullW, fullH, titleHeight);
                    break;
                case WizardPhase.EasterEgg:
                    DrawEasterEgg(g, fullX, fullY, fullW, fullH, titleHeight, _phaseWatch.ElapsedMilliseconds);
                    break;
                case WizardPhase.Farewell:
                    DrawFarewell(g, fullX, fullY, fullW, fullH, titleHeight);
                    break;
                default:
                    DrawWizard(g, fullX, fullY, fullW, fullH, titleHeight);
                    break;
            }
        }

        // Cached across frames since the catalog never changes at runtime - no point
        // re-grouping and re-bin-packing 30-odd entries on every 33ms paint tick.
        private List<List<(string Category, List<int> Indices)>>? _wizardColumns;

        // Long catalogs (the Gaming list has ~100 apps) get three columns so the list still fits.
        private static int ColumnCountFor(int itemCount) => itemCount > 64 ? 3 : 2;

        // Drives the typewriter tick sound (see Advance()) - tracks how many characters
        // of the wizard's app list have been revealed so far, so a tick only plays when
        // new text has actually appeared rather than once per timer tick regardless.
        private int _wizardCharsRevealed;
        private int? _wizardTotalCharsCache;

        private static bool IsWizardTypingPhase(WizardPhase phase) => phase is
            WizardPhase.ConfirmAll or WizardPhase.ChooseIndividually or
            WizardPhase.PerApp or WizardPhase.ConfirmQuit;

        // Total character count of the wizard's app list, for pacing the typewriter
        // reveal/tick sound. This is deterministic from the catalog alone - the marker
        // ("[x]"/"[ ]"/"-->"/"   ") is always 3 characters regardless of decision state,
        // so per-app selection changes never change this total.
        private int GetWizardTotalChars()
        {
            if (_wizardTotalCharsCache != null) return _wizardTotalCharsCache.Value;

            BuildWizardColumnsIfNeeded();
            var catalog = _catalog;
            var total = 0;
            foreach (var columnGroups in _wizardColumns!)
            {
                foreach (var (category, indices) in columnGroups)
                {
                    total += $"-- {category} --".Length;
                    foreach (var i in indices)
                        total += $"    {catalog[i].FriendlyName}".Length; // 3-char marker + space
                }
            }

            _wizardTotalCharsCache = total;
            return total;
        }

        private void BuildWizardColumnsIfNeeded()
        {
            if (_wizardColumns != null) return;

            var catalog = _catalog;

            // Group by category, preserving the order categories first appear in the
            // catalog (not a sort - so the wizard's on-screen order matches source order).
            var groups = new List<(string Category, List<int> Indices)>();
            for (int i = 0; i < catalog.Count; i++)
            {
                var cat = catalog[i].Category;
                var group = groups.FirstOrDefault(g => g.Category == cat);
                if (group.Indices == null)
                {
                    group = (cat, new List<int>());
                    groups.Add(group);
                }
                group.Indices.Add(i);
            }

            // Greedily bin-pack whole categories (never split one across columns), always
            // dropping the next category into whichever column is currently shortest - keeps
            // the columns roughly the same height even though categories vary in size.
            var count = ColumnCountFor(catalog.Count);
            var columns = Enumerable.Range(0, count).Select(_ => new List<(string, List<int>)>()).ToList();
            var heights = new int[count];
            foreach (var group in groups)
            {
                var blockHeight = 3 + group.Indices.Count; // heading + spacer + items + trailing spacer (estimate, for balancing)
                var target = Array.IndexOf(heights, heights.Min());
                columns[target].Add(group);
                heights[target] += blockHeight;
            }

            _wizardColumns = columns;
        }

        // How fast the wizard's app list "types" itself out, in characters per second.
        // Fast enough that even the ~100-app Gaming list is fully typed in about 5 seconds.
        private int WizardTypeCharsPerSecond => Math.Max(150, GetWizardTotalChars() / 5);

        private readonly struct WizardLine
        {
            public readonly string Text;
            public readonly Font Font;
            public readonly Brush Brush;
            public WizardLine(string text, Font font, Brush brush) { Text = text; Font = font; Brush = brush; }
        }

        // The disclosure screen itself: every tweak, its real current value, and
        // what it would become, drawn in full immediately (no typewriter effect -
        // by this point the catalog's already fully typed, and this is a short
        // consent screen rather than a big reveal). Nothing here has changed
        // anything on the machine yet - that only happens in InstallerEngine's
        // ApplyTweaks(), and only if the user answers Y to the prompt at the bottom.
        // "Before" and "after" are asked as a single three-way choice rather than a
        // Y/N gate per option, since exactly one of the three applies - matches
        // how the choice actually works instead of forcing three separate
        // yes/no questions to express one decision.
        private void DrawConfirmBackup(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var contentX = fullX + 16;
            const int lineHeight = 18;

            var headingTop = fullY + titleHeight + 10 + lineHeight;
            g.DrawString("A System Restore point is always created before anything changes - no need to ask about that.",
                _monoSmall, Brushes.Gainsboro, contentX, headingTop);
            g.DrawString("On top of that, GamingStack can also do a full disk-image backup, if you'd like one:",
                _monoSmall, Brushes.Gainsboro, contentX, headingTop + lineHeight);

            var listTop = headingTop + lineHeight * 2 + 14;
            var needed = BackupEngine.EstimatedNeededBytes();
            g.DrawString($"[1] Before anything is installed or changed (a clean, \"virgin machine\" backup)",
                _monoSmall, Brushes.Gold, contentX, listTop);
            g.DrawString($"[2] After everything is installed and tweaked (a \"gaming ready\" backup)",
                _monoSmall, Brushes.Gold, contentX, listTop + lineHeight);
            g.DrawString($"[3] Skip the image backup - just the restore point above",
                _monoSmall, Brushes.Gold, contentX, listTop + lineHeight * 2);

            var noteTop = listTop + lineHeight * 3 + 14;
            g.DrawString($"A full backup needs roughly {BackupEngine.FormatBytes(needed)} free on another drive, and can take a",
                _monoSmall, Brushes.DimGray, contentX, noteTop);
            g.DrawString("while - the next screen shows what's available so you can judge for yourself.",
                _monoSmall, Brushes.DimGray, contentX, noteTop + lineHeight);

            var promptTop = noteTop + lineHeight * 2 + 16;
            using var promptFont = Ui.Font(14f, FontStyle.Bold);
            g.DrawString("Choose 1, 2 or 3", promptFont, Brushes.Gold, contentX, promptTop);
        }

        private void DrawSelectBackupDrive(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var contentX = fullX + 16;
            const int lineHeight = 18;

            var headingTop = fullY + titleHeight + 10 + lineHeight;
            var timingLabel = _backupTiming == BackupTiming.Before ? "before installing anything" : "after everything's installed";
            g.DrawString($"Pick a drive for the backup ({timingLabel}):", _monoSmall, Brushes.Gainsboro, contentX, headingTop);

            var needed = BackupEngine.EstimatedNeededBytes();
            var listTop = headingTop + lineHeight + 10;

            if (_backupDriveOptions.Count == 0)
            {
                g.DrawString("No eligible drives detected right now (needs a non-system, NTFS-formatted drive).",
                    _monoSmall, Brushes.Gold, contentX, listTop);
            }
            else
            {
                for (int i = 0; i < _backupDriveOptions.Count; i++)
                {
                    var d = _backupDriveOptions[i];
                    var enoughRoom = needed <= 0 || d.FreeBytes >= needed;
                    var brush = enoughRoom ? Brushes.LightGreen : Brushes.OrangeRed;
                    var roomNote = enoughRoom ? "" : "  (may not be enough room)";
                    g.DrawString($"[{i + 1}] {d.Label} - {BackupEngine.FormatBytes(d.FreeBytes)} free{roomNote}",
                        _monoSmall, brush, contentX, listTop + i * lineHeight);
                }
            }

            var promptTop = listTop + Math.Max(1, _backupDriveOptions.Count) * lineHeight + 16;
            using var promptFont = Ui.Font(14f, FontStyle.Bold);
            var prompt = _backupDriveOptions.Count == 0
                ? "Press [N] to continue without an image backup"
                : $"Choose 1-{_backupDriveOptions.Count}, or [N] to skip the image backup";
            g.DrawString(prompt, promptFont, Brushes.Gold, contentX, promptTop);
        }

        // Shared by PreparingBackup (restore point + optional "before" image
        // backup) and RunningBackup (the "after" image backup) - both are the same
        // "don't touch anything, this takes a while" blocking screen, just with a
        // different message and a different Stopwatch driving the elapsed time.
        private void DrawBackupProgress(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight, string message)
        {
            var contentX = fullX + 16;
            const int lineHeight = 18;

            var headingTop = fullY + titleHeight + 10 + lineHeight;
            foreach (var line in WrapTextToWidth(g, message, _monoSmall, fullW - 32))
            {
                g.DrawString(line, _monoSmall, Brushes.Gainsboro, contentX, headingTop);
                headingTop += lineHeight;
            }

            if (_noEligibleBackupDrive)
            {
                g.DrawString("(No eligible backup drive was found - continuing with just the restore point.)",
                    _monoSmall, Brushes.Gold, contentX, headingTop + 6);
                headingTop += lineHeight;
            }

            var elapsed = TimeSpan.FromMilliseconds(_phaseWatch.ElapsedMilliseconds);
            g.DrawString($"Elapsed: {elapsed:mm\\:ss}", _monoSmall, Brushes.DimGray, contentX, headingTop + 14);
        }

        private void DrawChoosePowerPlan(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var contentX = fullX + 16;
            const int lineHeight = 18;

            var headingTop = fullY + titleHeight + 10 + lineHeight;
            g.DrawString("One of the tweaks coming up sets the active power plan. Which one?",
                _monoSmall, Brushes.Gainsboro, contentX, headingTop);

            var listTop = headingTop + lineHeight + 14;
            g.DrawString("[1] High performance - Windows' own built-in plan (recommended)",
                _monoSmall, Brushes.Gold, contentX, listTop);
            g.DrawString("[2] Ultimate Performance - a hidden Microsoft plan with the last few",
                _monoSmall, Brushes.Gold, contentX, listTop + lineHeight);
            g.DrawString("    power-saving throttles removed on top of High performance",
                _monoSmall, Brushes.Gold, contentX, listTop + lineHeight * 2);

            var noteTop = listTop + lineHeight * 3 + 14;
            g.DrawString("Ultimate Performance is real and Microsoft-documented, just hidden by default since it",
                _monoSmall, Brushes.DimGray, contentX, noteTop);
            g.DrawString("has no benefit on a laptop and a small idle-power cost on a desktop that's plugged in.",
                _monoSmall, Brushes.DimGray, contentX, noteTop + lineHeight);

            var promptTop = noteTop + lineHeight * 2 + 16;
            using var promptFont = Ui.Font(14f, FontStyle.Bold);
            g.DrawString("Choose 1 or 2", promptFont, Brushes.Gold, contentX, promptTop);
        }

        private const int CreditsAutoAdvanceMs = 12000;

        private void DrawChooseProfile(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var contentX = fullX + 16;
            const int lineHeight = 18;
            var top = fullY + titleHeight + 10 + lineHeight;

            // The heading font is 14pt, so each choice gets a full 3 lines of room: the
            // title, a gap, then its description underneath without touching it.
            g.DrawString("What kind of PC are we setting up?", _mono, Brushes.Gainsboro, contentX, top);
            top += lineHeight * 2 + 10;

            var choices = new[]
            {
                ("[1] Gaming / Streaming PC", "launchers, GPU + monitoring tools, capture and streaming, RGB, gaming tweaks"),
                ("[2] Office computer", "browsers, Microsoft 365, PDFs, video calls, password manager, everyday utilities"),
                ("[3] Developer / Home lab", "Git, VS Code, Docker, WSL, languages, network and server tools, developer tweaks"),
            };
            foreach (var (title, desc) in choices)
            {
                g.DrawString(title, _mono, Brushes.Gold, contentX, top);
                g.DrawString(desc, _monoSmall, Brushes.Gray, contentX + 30, top + lineHeight + 10);
                top += lineHeight * 3 + 16;
            }

            if (Enum.TryParse<SetupProfile>(ProgressStore.LastProfile, out var lastProfile) && lastProfile != SetupProfile.None
                && ProgressStore.LastApps.Count > 0)
            {
                g.DrawString($"[R] Repeat last setup  ({SetupProfileInfo.Title(lastProfile)}, {ProgressStore.LastApps.Count} apps)", _mono, Brushes.LightGreen, contentX, top);
                top += lineHeight * 2;
            }

            top += 6;
            g.DrawString("Nothing is installed or changed yet - next you'll see exactly what it would install,", _monoSmall, Brushes.DimGray, contentX, top);
            g.DrawString("and you can still say no to any of it.", _monoSmall, Brushes.DimGray, contentX, top + lineHeight + 4);

            using var promptFont = Ui.Font(14f, FontStyle.Bold);
            g.DrawString("Choose 1, 2 or 3   (or double-click a Gaming / Office / Developer icon)", promptFont, Brushes.Gold, contentX, top + lineHeight * 3 + 8);
        }

        // "Repeat last setup": same setup type and the same apps as last time (any that no
        // longer exist in the catalog are simply skipped), straight on to the backup question.
        private void RepeatLastSetup()
        {
            if (!Enum.TryParse<SetupProfile>(ProgressStore.LastProfile, out var profile) || profile == SetupProfile.None) return;
            SelectProfile(profile);
            var names = new HashSet<string>(ProgressStore.LastApps);
            var chosen = _catalog.Where(a => names.Contains(a.FriendlyName)).ToList();
            if (chosen.Count == 0) return;                  // nothing usable - fall back to the normal questions
            _selectedApps.Clear();
            _selectedApps.AddRange(chosen);
            GoToConfirmBackup();
        }

        // Called once the user picks a profile: swaps in that profile's catalog and
        // restarts the typewriter so the list types itself out fresh.
        private void SelectProfile(SetupProfile profile)
        {
            _profile = profile;
            _catalog = InstallerEngine.CatalogFor(profile);
            _selectedApps.Clear();
            _perAppDecisions = Array.Empty<bool?>();
            _perAppIndex = 0;
            _wizardColumns = null;
            _wizardTotalCharsCache = null;
            _wizardCharsRevealed = 0;
            _typewriterBellRung = false;
            _typingStartMs = _stageWatch.ElapsedMilliseconds + 250;
            _wizardPhase = WizardPhase.ConfirmAll;
        }

        private void DrawChooseDns(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var contentX = fullX + 16;
            const int lineHeight = 18;
            var top = fullY + titleHeight + 10 + lineHeight;

            g.DrawString("Optional: switch your DNS servers (the \"phone book\" your PC uses to find websites).",
                _monoSmall, Brushes.Gainsboro, contentX, top);
            top += lineHeight + 14;
            g.DrawString("[1] Keep my current DNS - change nothing (recommended if unsure)", _monoSmall, Brushes.Gold, contentX, top);
            g.DrawString("[2] Cloudflare   1.1.1.1 / 1.0.0.1", _monoSmall, Brushes.Gold, contentX, top + lineHeight);
            g.DrawString("[3] Google       8.8.8.8 / 8.8.4.4", _monoSmall, Brushes.Gold, contentX, top + lineHeight * 2);
            g.DrawString("[4] Quad9        9.9.9.9 / 149.112.112.112", _monoSmall, Brushes.Gold, contentX, top + lineHeight * 3);

            top += lineHeight * 4 + 14;
            g.DrawString("Only your active adapter's IPv4 DNS changes. Your old setting is saved to the", _monoSmall, Brushes.DimGray, contentX, top);
            g.DrawString("undo script, so it can be put back exactly (automatic or your own servers).", _monoSmall, Brushes.DimGray, contentX, top + lineHeight);

            using var promptFont = Ui.Font(14f, FontStyle.Bold);
            g.DrawString("Choose 1, 2, 3 or 4", promptFont, Brushes.Gold, contentX, top + lineHeight * 2 + 16);
        }

        private void DrawChooseDebloat(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var contentX = fullX + 16;
            const int lineHeight = 18;
            var top = fullY + titleHeight + 10 + lineHeight;

            g.DrawString("Optional: these preinstalled apps were found on your PC. Tick any you want removed.",
                _monoSmall, Brushes.Gainsboro, contentX, top);
            top += lineHeight + 10;

            for (int i = 0; i < _debloatOffered.Count; i++)
            {
                var item = _debloatOffered[i];
                var ticked = _debloatTicked.Contains(item.Id);
                var brush = ticked ? Brushes.LimeGreen : Brushes.Gold;
                g.DrawString($"[{i + 1}] [{(ticked ? "x" : " ")}] {item.Name} - {item.Why}", _monoSmall, brush, contentX, top + i * lineHeight);
            }

            top += _debloatOffered.Count * lineHeight + 12;
            g.DrawString("Nothing is ticked by default. Every removal can be undone: the undo script reinstalls", _monoSmall, Brushes.DimGray, contentX, top);
            g.DrawString("each app from the Microsoft Store.", _monoSmall, Brushes.DimGray, contentX, top + lineHeight);

            using var promptFont = Ui.Font(14f, FontStyle.Bold);
            g.DrawString("Press a number to tick/untick, ENTER when done", promptFont, Brushes.Gold, contentX, top + lineHeight * 2 + 12);
        }

        private void DrawCredits(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight, long elapsedMs)
        {
            string[] lines =
            {
                "G A M I N G S T A C K   v1.0",
                "",
                "Idea, taste and testing ........ Paul",
                "Code and late-night typing ..... Claude",
                "Installs ....................... winget",
                "Sound effects .................. one very patient speaker",
                "",
                "No Windows were harmed in the making of this program.",
                "(They were all asked first.)",
                "",
                "Press any key to carry on"
            };
            const int lineHeight = 22;
            var contentX = fullX + 16;
            // Scrolls upward from the bottom edge of the window.
            var startY = fullY + fullH - (int)(elapsedMs / 18);
            using var clip = new Region(new Rectangle(fullX + 1, fullY + titleHeight, fullW - 2, fullH - titleHeight - 2));
            var oldClip = g.Clip;
            g.Clip = clip;
            for (int i = 0; i < lines.Length; i++)
            {
                var y = startY + i * lineHeight;
                var size = Math.Max(8, g.MeasureString(lines[i], _mono).Width);
                var x = fullX + (fullW - size) / 2;
                g.DrawString(lines[i], _mono, i == 0 ? Brushes.Gold : Brushes.Gainsboro, Math.Max(contentX, x), y);
            }
            g.Clip = oldClip;
        }

        private void DrawConfirmTweaks(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var contentX = fullX + 16;
            // Shrinks the line spacing on short windows so the list + the Y/N prompt
            // always fit inside the box, instead of running off the bottom.
            var rows = _tweakPreview.Count + 9;
            var lineHeight = Math.Clamp((fullH - titleHeight - 30) / Math.Max(1, rows), 12, 18);

            var headingTop = fullY + titleHeight + 10 + lineHeight;
            g.DrawString("A few optional tweaks are recommended - nothing is applied without your OK:",
                _monoSmall, Brushes.Gainsboro, contentX, headingTop);

            var listTop = headingTop + lineHeight + 10;
            var nameWidth = _tweakPreview.Count == 0
                ? 0f
                : _tweakPreview.Max(t => g.MeasureString(t.Name, _monoSmall).Width) + 24;

            for (int i = 0; i < _tweakPreview.Count; i++)
            {
                var t = _tweakPreview[i];
                var ly = listTop + i * lineHeight;
                g.DrawString(t.Name, _monoSmall, Brushes.Gainsboro, contentX, ly);
                g.DrawString($"currently: {t.Current}  ->  {t.NewValue}", _monoSmall, Brushes.Gold, contentX + nameWidth, ly);
            }

            var noteTop = listTop + _tweakPreview.Count * lineHeight + 14;
            g.DrawString("Saying yes writes an undo script (revert-tweaks.cmd) before changing anything,",
                _monoSmall, Brushes.DimGray, contentX, noteTop);
            g.DrawString("so every change here can be put back exactly as it was.",
                _monoSmall, Brushes.DimGray, contentX, noteTop + lineHeight);

            var promptTop = noteTop + lineHeight * 2 + 16;
            using var promptFont = Ui.Font(14f, FontStyle.Bold);
            g.DrawString("Apply these tweaks?   [Y] Yes    [N] No, skip tweaks", promptFont, Brushes.Gold, contentX, promptTop);
        }

        private void DrawWizard(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var catalog = _catalog;
            var contentX = fullX + 16;
            const int headerLine = 18;

            // Dropped one extra line below the title bar so the heading doesn't sit
            // flush under it.
            var headingTop = fullY + titleHeight + 10 + headerLine;
            g.DrawString($"GamingStack will install the following ({SetupProfileInfo.Title(_profile)}):",
                _mono, Brushes.Gainsboro, contentX, headingTop);
            var listTop = headingTop + headerLine + 8;

            BuildWizardColumnsIfNeeded();
            var columnGroups = _wizardColumns!;
            var colCount = columnGroups.Count;
            var colWidth = (fullW - 32) / colCount;

            // Fit the list to the window. Some profiles have a long catalog, so: use the
            // roomy layout (a blank row around each heading, 18px rows) when it fits,
            // tighten the rows (down to 15px) when it doesn't, and drop the blank rows
            // as a last resort. The room kept free at the bottom is for the Y/N prompt.
            var available = fullY + fullH - listTop - 64;
            int RowsNeeded(bool spacers) =>
                columnGroups.Max(col => col.Sum(grp => (spacers ? 3 : 1) + grp.Indices.Count));
            var useSpacers = RowsNeeded(true) * 15 <= available;
            var lineHeight = Math.Clamp(available / Math.Max(1, RowsNeeded(useSpacers)), 15, 20);
            var tight = lineHeight < 19;     // a 12px font needs ~19px rows; below that use the smaller font
            var rowFont = tight ? _monoTiny : _monoSmall;

            // Per-frame line content (text/font/brush per row, per column) - unlike the
            // category grouping above (cached; the catalog never changes) this has to be
            // rebuilt every frame since the wizard's own selection state does. A `null`
            // entry is a blank spacer row (used after each category heading, so the
            // heading doesn't visually blend into the list under it).
            var colLines = new List<WizardLine?>[colCount];
            for (int col = 0; col < colCount; col++)
            {
                var lines = new List<WizardLine?>();
                foreach (var (category, indices) in columnGroups[col])
                {
                    lines.Add(new WizardLine($"-- {category} --", tight ? _headingTiny : _headingFont, Brushes.DeepSkyBlue));
                    if (useSpacers) lines.Add(null);

                    foreach (var i in indices)
                    {
                        var marker = "   ";
                        var brush = Brushes.Gainsboro;

                        if (i < _perAppDecisions.Length && _perAppDecisions[i].HasValue)
                        {
                            var got = _perAppDecisions[i]!.Value;
                            marker = got ? "[x]" : "[ ]";
                            brush = got ? Brushes.LightGreen : Brushes.Gray;
                        }
                        else if (_wizardPhase == WizardPhase.PerApp && i == _perAppIndex)
                        {
                            marker = "-->";
                            brush = Brushes.Gold;
                        }

                        lines.Add(new WizardLine($"{marker} {catalog[i].FriendlyName}", rowFont, brush));
                    }

                    // Trailing blank line too, so a category's list doesn't run straight
                    // into the next category's heading - matches the spacer already
                    // added right after the heading above.
                    if (useSpacers) lines.Add(null);
                }
                colLines[col] = lines;
            }

            var maxRowsUsed = colLines.Max(c => c.Count);
            var totalChars = colLines.SelectMany(c => c).Where(l => l != null).Sum(l => l!.Value.Text.Length);

            // Typewriter reveal: starts once the terminal window has finished opening,
            // and (since _stageWatch never resets while still in the Terminal stage)
            // naturally stays fully revealed forever afterwards, including across
            // WizardPhase changes - it only ever plays once.
            var typingElapsedMs = Math.Max(0, _stageWatch.ElapsedMilliseconds - _typingStartMs);
            var typedBudget = (int)(typingElapsedMs / 1000.0 * WizardTypeCharsPerSecond);
            var fullyTyped = typedBudget >= totalChars;
            var remaining = typedBudget;

            (float X, float Y, string Shown, Font Font)? cursor = null;

            for (int row = 0; row < maxRowsUsed; row++)
            {
                for (int col = 0; col < colCount; col++)
                {
                    if (row >= colLines[col].Count) continue;
                    var line = colLines[col][row];
                    if (line == null) continue;

                    var text = line.Value.Text;
                    string shown;
                    if (remaining <= 0) shown = "";
                    else if (remaining >= text.Length) { shown = text; remaining -= text.Length; }
                    else { shown = text[..remaining]; remaining = 0; }

                    if (shown.Length == 0) continue;

                    var lx = contentX + col * colWidth;
                    var ly = listTop + row * lineHeight;
                    g.DrawString(shown, line.Value.Font, line.Value.Brush, lx, ly);

                    if (shown.Length < text.Length)
                        cursor = (lx, ly, shown, line.Value.Font);
                }
            }

            // Blinking cursor at wherever typing currently stands, mid-reveal.
            if (cursor != null && (typingElapsedMs / 500) % 2 == 0)
            {
                var cx = cursor.Value.X + g.MeasureString(cursor.Value.Shown, cursor.Value.Font).Width;
                g.FillRectangle(Brushes.Gainsboro, cx, cursor.Value.Y, 8, 14);
            }

            if (!fullyTyped) return;

            var promptTop = listTop + maxRowsUsed * lineHeight + 16;
            string prompt = _wizardPhase switch
            {
                WizardPhase.ConfirmAll => "Install everything shown above?   [Y] Yes    [N] No",
                WizardPhase.ChooseIndividually => "Would you like to choose what's installed?   [Y] Yes    [N] No",
                WizardPhase.PerApp => $"Install \"{catalog[_perAppIndex].FriendlyName}\"?   [Y] Yes    [N] Skip",
                WizardPhase.ConfirmQuit => "No changes made yet. Just want to quit?   [Y] Yes    [N] No",
                _ => ""
            };

            using var promptFont = Ui.Font(14f, FontStyle.Bold);
            g.DrawString(prompt, promptFont, Brushes.Gold, contentX, promptTop);
        }

        private void DrawInstallerLog(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight, long elapsed)
        {
            string[] linesSnapshot;
            lock (_terminalLock) { linesSnapshot = _terminalLines.ToArray(); }

            // status strip: what it is doing right now, how far along, and how to stop
            var headerTop = fullY + titleHeight + 8;
            var done = _installer.ItemsDone; var total = Math.Max(1, _installer.ItemsTotal);
            var activity = _installer.CurrentActivity ?? (_installer.CancelRequested ? "stopping..." : "working...");
            g.DrawString($"Working on: {activity}", _mono, Brushes.White, fullX + 16, headerTop);
            var bar = new Rectangle(fullX + 16, headerTop + 28, Math.Max(100, fullW - 32), 14);
            g.DrawRectangle(Pens.DimGray, bar);
            using (var fill = new SolidBrush(Color.FromArgb(0, 200, 230)))
                g.FillRectangle(fill, bar.X + 1, bar.Y + 1, (bar.Width - 2) * done / total, bar.Height - 2);
            var hint = _installer.CancelRequested
                ? "Stopping after the current item - nothing else will be installed or changed."
                : $"{done} of {_installer.ItemsTotal} done.   Press C to stop after the current item.   (Closing is blocked while this runs.)";
            g.DrawString(hint, _monoSmall, _installer.CancelRequested ? Brushes.Gold : Brushes.Gray, fullX + 16, bar.Bottom + 4);

            const int lineHeight = 20;
            var contentTop = bar.Bottom + 4 + _monoSmall.Height + 10;
            var contentBottom = fullY + fullH - 10;
            var maxLines = Math.Max(1, (contentBottom - contentTop) / lineHeight);

            var visible = linesSnapshot.Length > maxLines
                ? linesSnapshot[^maxLines..]
                : linesSnapshot;

            var ty = contentTop;
            foreach (var line in visible)
            {
                g.DrawString(line, _mono, Brushes.LimeGreen, fullX + 16, ty);
                ty += lineHeight;
            }

            var blink = (elapsed / 500) % 2 == 0;
            if (blink)
            {
                float cursorX = fullX + 16;
                var cursorY = ty - lineHeight;
                if (visible.Length > 0)
                    cursorX += g.MeasureString(visible[^1], _mono).Width;
                else
                    cursorY = ty;

                g.FillRectangle(Brushes.LimeGreen, cursorX, cursorY, 10, 16);
            }
        }

        // One line describing the backup choice, for the log header and the review screen.
        private string BackupSummary() => _backupTiming switch
        {
            BackupTiming.Before => $"restore point + full disk image BEFORE changes to {_backupDrive}",
            BackupTiming.After => $"restore point + full disk image AFTER setup to {_backupDrive}",
            _ => "restore point only (no disk image)"
        };

        // The point of no return. Everything that is about to happen, in plain words, with the
        // real numbers - and it only goes ahead when GO is typed and Enter is pressed, so it can
        // never be done by accident with one stray key.
        private void DrawFinalReview(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var x = fullX + 16;
            var y = fullY + titleHeight + 14;
            using var head = Ui.Font(14f, FontStyle.Bold);
            g.DrawString("FINAL CHECK - read this before you start", head, Brushes.Gold, x, y);
            y += head.Height + 12;

            void Line(string text, Brush brush, Font? font = null)
            {
                var f = font ?? _monoSmall;
                foreach (var l in WrapTextToWidth(g, text, f, fullW - 40))
                {
                    g.DrawString(l, f, brush, x, y);
                    y += f.Height + 3;
                }
            }

            var winget = _selectedApps.Count(a => a.Kind is AppKind.Winget or AppKind.Bundle);
            var links = _selectedApps.Count(a => a.Kind == AppKind.Link);
            var manual = _selectedApps.Count(a => a.Kind == AppKind.Manual);
            var removals = _applyTweaks ? _debloatTicked.Count : 0;

            Line($"Setup type: {SetupProfileInfo.Title(_profile)}", Brushes.White);
            Line($"Safety net: {BackupSummary()}. A System Restore point is always made first.", Brushes.LightGreen);
            y += 6;
            Line($"APPS: {_selectedApps.Count} selected", Brushes.White, _mono);
            Line($"  {winget} install silently through winget (no windows will pop up, it can take a long time)", Brushes.Gainsboro);
            if (manual > 0) Line($"  {manual} are downloaded from the vendor's website and run (the log records what was downloaded)", Brushes.Gainsboro);
            if (links > 0) Line($"  {links} just open the vendor's download page in your browser - you finish those yourself", Brushes.Gainsboro);
            y += 6;
            if (_applyTweaks)
            {
                Line($"CHANGES TO WINDOWS: YES - {_tweakPreview.Count} settings change (you saw each one on the last screen)", Brushes.Orange, _mono);
                if (_dnsChoice != DnsChoice.Skip) Line($"  Your network DNS servers will change to {_dnsChoice}.", Brushes.Gainsboro);
                if (removals > 0) Line($"  {removals} preinstalled app(s) will be REMOVED (they can be reinstalled from the Store).", Brushes.OrangeRed);
                Line("  An undo script is saved so every change can be reversed.", Brushes.Gainsboro);
            }
            else
            {
                Line("CHANGES TO WINDOWS: none - you said no to tweaks, so only apps are installed.", Brushes.LightGreen, _mono);
            }
            y += 6;
            Line($"Everything is recorded in a log you can read afterwards: {InstallerEngine.LogsFolder}", Brushes.Gray);
            Line("You can press C during the install to stop after the current item.", Brushes.Gray);
            y += 12;

            var typed = _finalTyped.PadRight(2, '_');
            Line($"To begin, type  G  O  then press Enter     [ {typed[0]} {typed[1]} ]", Brushes.Gold, head);
            Line("Press N to go back to the start - nothing has been changed yet.", Brushes.Gray);
        }

        // Greedy word-wrap a string to fit within maxWidth pixels for the given font -
        // used for the summary screen's failure/skip reasons, which are free text of
        // unpredictable length and used to just run straight off the edge of their
        // column into whatever the neighboring column was drawing.
        private static List<string> WrapTextToWidth(Graphics g, string text, Font font, float maxWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                lines.Add(text ?? "");
                return lines;
            }

            var current = "";
            foreach (var word in text.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && g.MeasureString(candidate, font).Width > maxWidth)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }
            if (current.Length > 0 || lines.Count == 0) lines.Add(current);
            return lines;
        }

        // Bin-packs only the attempted categories/items into height-balanced
        // columns, same idea as the wizard's own BuildWizardColumnsIfNeeded but built
        // fresh each run since which items were attempted changes every time. Needs
        // each item's already-wrapped detail line count (see WrapTextToWidth above) to
        // balance accurately - a long reason wrapping to 3 lines has to count as 3 rows
        // here, or the two columns drift out of sync with what's actually drawn and end
        // up overlapping again exactly like before.
        private List<(string Category, List<int> Indices)>[] BuildSummaryColumns(
            ItemResult[] results, Dictionary<AppEntry, List<string>> wrappedDetails)
        {
            var catalog = _catalog;

            var groups = new List<(string Category, List<int> Indices)>();
            for (int i = 0; i < catalog.Count; i++)
            {
                if (!results.Any(r => r.Entry == catalog[i])) continue;
                var cat = catalog[i].Category;
                var group = groups.FirstOrDefault(g => g.Category == cat);
                if (group.Indices == null)
                {
                    group = (cat, new List<int>());
                    groups.Add(group);
                }
                group.Indices.Add(i);
            }

            var count = ColumnCountFor(results.Length);
            var columns = Enumerable.Range(0, count).Select(_ => new List<(string, List<int>)>()).ToArray();
            var heights = new int[count];
            foreach (var group in groups)
            {
                // heading + spacer-after-heading + trailing spacer, plus one row per
                // item and however many wrapped lines its detail text actually takes.
                var blockHeight = 3 + group.Indices.Sum(i =>
                    1 + (wrappedDetails.TryGetValue(catalog[i], out var lines) ? lines.Count : 0));
                var target = Array.IndexOf(heights, heights.Min());
                columns[target].Add(group);
                heights[target] += blockHeight;
            }

            return columns;
        }
    }
}
