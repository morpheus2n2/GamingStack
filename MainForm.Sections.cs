using System.Management;

namespace GamingStackGUI
{
    // The guided setup, part 2 (1.8.0): hardware makers, one page per section, the "are you sure"
    // review, the per-tweak tick list and the Captures folder question. Nothing here changes the PC -
    // that only ever happens after the final check (FinalReview) in MainForm.Wizard.cs.
    public partial class MainForm
    {
        // ======================================================= hardware makers

        // Graphics card makers and processor makers are separate questions: an NVIDIA card with a Ryzen
        // processor needs the Ryzen tools but not AMD's graphics software.
        private bool _hwNvidiaGpu, _hwAmdGpu, _hwIntelGpu, _hwAmdCpu, _hwIntelCpu;
        private string _hwDetected = "";

        // Reads the graphics card(s) and processor name via WMI (pure read) and pre-ticks the matching
        // makers. If a kind is not recognised, every maker of that kind is ticked so nothing is hidden by accident.
        private void DetectHardware()
        {
            var gpus = new List<string>();
            var cpu = "";
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (var o in s.Get()) { var n = o["Name"]?.ToString(); if (!string.IsNullOrWhiteSpace(n)) gpus.Add(n!); }
            }
            catch { /* shows "not detected" below */ }
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (var o in s.Get()) { cpu = o["Name"]?.ToString()?.Trim() ?? ""; break; }
            }
            catch { /* shows "not detected" below */ }

            bool Gpu(string text) => gpus.Any(n => n.Contains(text, StringComparison.OrdinalIgnoreCase));
            _hwNvidiaGpu = Gpu("NVIDIA") || Gpu("GeForce");
            _hwAmdGpu = Gpu("AMD") || Gpu("Radeon");
            _hwIntelGpu = Gpu("Intel");
            _hwAmdCpu = cpu.Contains("AMD", StringComparison.OrdinalIgnoreCase);
            _hwIntelCpu = cpu.Contains("Intel", StringComparison.OrdinalIgnoreCase);
            if (!_hwNvidiaGpu && !_hwAmdGpu && !_hwIntelGpu) _hwNvidiaGpu = _hwAmdGpu = _hwIntelGpu = true;
            if (!_hwAmdCpu && !_hwIntelCpu) _hwAmdCpu = _hwIntelCpu = true;

            _hwDetected = $"Graphics: {(gpus.Count == 0 ? "not detected" : string.Join(", ", gpus))}   |   Processor: {(cpu.Length == 0 ? "not detected" : cpu)}";
        }

        private bool HardwareAllowed(AppEntry a) => a.Hardware switch
        {
            HardwareTag.NvidiaGpu => _hwNvidiaGpu,
            HardwareTag.AmdGpu => _hwAmdGpu,
            HardwareTag.IntelGpu => _hwIntelGpu,
            HardwareTag.AmdCpu => _hwAmdCpu,
            HardwareTag.IntelCpu => _hwIntelCpu,
            HardwareTag.IntelAny => _hwIntelGpu || _hwIntelCpu,
            _ => true
        };

        // After the hardware question (or straight away if this setup type has no hardware-specific apps).
        private void ApplyHardwareAndShowList()
        {
            _catalog = InstallerEngine.CatalogFor(_profile).Where(HardwareAllowed).ToList();
            _wizardColumns = null;
            _wizardTotalCharsCache = null;
            _wizardCharsRevealed = 0;
            _typewriterBellRung = false;
            _typingStartMs = _stageWatch.ElapsedMilliseconds + 250;
            _wizardPhase = WizardPhase.ConfirmAll;
        }

        private void HandleChooseHardwareKey(Keys key)
        {
            if (key == Keys.N) _hwNvidiaGpu = !_hwNvidiaGpu;
            else if (key == Keys.A) _hwAmdGpu = !_hwAmdGpu;
            else if (key == Keys.I) _hwIntelGpu = !_hwIntelGpu;
            else if (key == Keys.R) _hwAmdCpu = !_hwAmdCpu;
            else if (key == Keys.C) _hwIntelCpu = !_hwIntelCpu;
            else if (key == Keys.Enter) ApplyHardwareAndShowList();
        }

        private void DrawChooseHardware(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var x = fullX + 16;
            var y = fullY + titleHeight + 14;
            g.DrawString("Which hardware do you have?", _mono, Brushes.Gainsboro, x, y);
            y += _mono.Height + 8;
            foreach (var l in WrapTextToWidth(g, "Apps for hardware you don't have (for example AMD graphics software on an NVIDIA card) are hidden, so you only see what is useful for this PC.", _monoSmall, fullW - 40))
            { g.DrawString(l, _monoSmall, Brushes.Gray, x, y); y += _monoSmall.Height + 3; }
            y += 8;
            foreach (var l in WrapTextToWidth(g, "Detected: " + _hwDetected, _monoSmall, fullW - 40))
            { g.DrawString(l, _monoSmall, Brushes.DeepSkyBlue, x, y); y += _monoSmall.Height + 3; }
            y += 12;

            void Row(string key, string name, bool on)
            {
                g.DrawString($"[{key}] [{(on ? "x" : " ")}] {name}", _mono, on ? Brushes.LimeGreen : Brushes.Gold, x, y);
                y += _mono.Height + 6;
            }
            g.DrawString("Graphics card", _monoSmall, Brushes.DeepSkyBlue, x, y); y += _monoSmall.Height + 4;
            Row("N", "NVIDIA GeForce", _hwNvidiaGpu);
            Row("A", "AMD Radeon", _hwAmdGpu);
            Row("I", "Intel Arc / Intel graphics", _hwIntelGpu);
            y += 6;
            g.DrawString("Processor", _monoSmall, Brushes.DeepSkyBlue, x, y); y += _monoSmall.Height + 4;
            Row("R", "AMD Ryzen", _hwAmdCpu);
            Row("C", "Intel Core", _hwIntelCpu);

            y += 4;
            foreach (var l in WrapTextToWidth(g, "Not sure? Leave them ticked and you will see everything. You can tick more than one (a PC can have two graphics chips).", _monoSmall, fullW - 40))
            { g.DrawString(l, _monoSmall, Brushes.DimGray, x, y); y += _monoSmall.Height + 3; }
            y += 10;
            using var prompt = Ui.Font(14f, FontStyle.Bold);
            g.DrawString("Press N, A, I, R or C to tick/untick, ENTER when done", prompt, Brushes.Gold, x, y);
        }

        // ======================================================= one page per section

        private List<(string Category, List<AppEntry> Items)> _sections = new();
        private int _secIndex, _secCursor, _secScroll;
        private readonly HashSet<AppEntry> _picked = new();
        private string _secNotice = "";
        private int _reviewScroll;

        // Called when the user says yes to "Start the guided setup?" on the full list.
        private void StartSections()
        {
            _sections = new();
            foreach (var a in _catalog)
            {
                var idx = _sections.FindIndex(s => s.Category == a.Category);
                if (idx < 0) { _sections.Add((a.Category, new List<AppEntry>())); idx = _sections.Count - 1; }
                _sections[idx].Items.Add(a);
            }
            _picked.Clear();
            _secIndex = 0; _secCursor = 0; _secScroll = 0; _secNotice = "";
            _wizardPhase = WizardPhase.PickSection;
        }

        private void GoToSection(int index)
        {
            _secIndex = Math.Clamp(index, 0, _sections.Count - 1);
            _secCursor = 0; _secScroll = 0; _secNotice = "";
            _wizardPhase = WizardPhase.PickSection;
        }

        private void TogglePick(AppEntry item)
        {
            if (_picked.Remove(item)) { _secNotice = ""; return; }
            _picked.Add(item);
            _secNotice = "";
            if (item.ExclusiveGroup == null) return;
            var removed = _picked.Where(p => p != item && p.ExclusiveGroup == item.ExclusiveGroup).ToList();
            foreach (var r in removed) _picked.Remove(r);
            if (removed.Count > 0)
                _secNotice = $"Only one lighting program at a time: \"{removed[0].FriendlyName}\" was unticked because they fight over the same LEDs.";
        }

        private void HandlePickSectionKey(Keys key)
        {
            var items = _sections[_secIndex].Items;
            switch (key)
            {
                case Keys.Up: _secCursor = Math.Max(0, _secCursor - 1); break;
                case Keys.Down: _secCursor = Math.Min(items.Count - 1, _secCursor + 1); break;
                case Keys.PageUp: _secCursor = Math.Max(0, _secCursor - 8); break;
                case Keys.PageDown: _secCursor = Math.Min(items.Count - 1, _secCursor + 8); break;
                case Keys.Home: _secCursor = 0; break;
                case Keys.End: _secCursor = items.Count - 1; break;
                case Keys.Space: TogglePick(items[_secCursor]); break;
                case Keys.A:
                    foreach (var i in items.Where(i => i.ExclusiveGroup == null)) _picked.Add(i);
                    _secNotice = items.Any(i => i.ExclusiveGroup != null) ? "Lighting programs are pick-one, so \"all\" skips them - tick one with SPACE." : "";
                    break;
                case Keys.N:
                    foreach (var i in items) _picked.Remove(i);
                    _secNotice = "";
                    break;
                case Keys.Enter:
                case Keys.Right:
                case Keys.Tab:
                    if (_secIndex >= _sections.Count - 1) { _reviewScroll = 0; _wizardPhase = WizardPhase.ReviewSelection; }
                    else GoToSection(_secIndex + 1);
                    break;
                case Keys.Back:
                case Keys.Left:
                    if (_secIndex > 0) GoToSection(_secIndex - 1);
                    break;
            }
        }

        private static string HowInstalls(AppEntry a) => a.Kind switch
        {
            AppKind.Winget => "Installs silently through winget.",
            AppKind.Bundle => $"Installs {a.BundleWingetIds?.Length ?? 0} packages silently through winget.",
            AppKind.Link => "Opens the vendor's download page in your browser - you finish that install yourself.",
            _ => "Downloaded from the vendor's website and run (the log records its size, checksum and signer)."
        };

        private static string ShortHow(AppEntry a) => a.Kind switch
        {
            AppKind.Winget => "winget",
            AppKind.Bundle => "winget x" + (a.BundleWingetIds?.Length ?? 0),
            AppKind.Link => "opens page",
            _ => "download"
        };

        private static string SectionNote(string category) => category switch
        {
            "RGB & Peripheral Control" =>
                "Pick ONE lighting program. Several together fight over the same LEDs and are hard to remove afterwards, so ticking one unticks the others. Apps that are not about lighting (like Logitech Options+) can be ticked as well.",
            "Redistributables" =>
                "Recommended for almost everyone: they stop \"missing .dll\" errors in games and apps.",
            "Console Emulators (bring your own games)" =>
                "Emulators are legal to install. You supply your own games and BIOS files, from consoles you own - GamingStack never downloads games.",
            "Uninstall & Clean-up Tools" =>
                "Deep uninstallers remove a program and the leftovers it hides in folders and the registry. You rarely need more than one.",
            "Drivers & Vendor Software" =>
                "Only the makers you chose earlier are shown. These open the vendor's own page - nothing is downloaded for you.",
            "Monitoring & Performance" =>
                "Some tools are for one maker only - they are marked NVIDIA, AMD or Intel.",
            _ => ""
        };

        private static string HardwareLabel(HardwareTag t) => t switch
        {
            HardwareTag.NvidiaGpu => "NVIDIA graphics only",
            HardwareTag.AmdGpu => "AMD graphics only",
            HardwareTag.IntelGpu => "Intel graphics only",
            HardwareTag.AmdCpu => "AMD Ryzen only",
            HardwareTag.IntelCpu => "Intel processor only",
            HardwareTag.IntelAny => "Intel hardware only",
            _ => ""
        };

        private void DrawPickSection(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var (category, items) = _sections[_secIndex];
            var x = fullX + 16;
            var y = fullY + titleHeight + 10;
            var bottom = fullY + fullH - 12;

            g.DrawString($"Section {_secIndex + 1} of {_sections.Count}: {category}", _headingFont, Brushes.DeepSkyBlue, x, y);
            var picked = $"Picked so far: {_picked.Count}";
            var pw = g.MeasureString(picked, _monoSmall).Width;
            g.DrawString(picked, _monoSmall, Brushes.LimeGreen, fullX + fullW - pw - 16, y + 2);
            y += _headingFont.Height + 4;

            var note = SectionNote(category);
            if (note.Length > 0)
                foreach (var l in WrapTextToWidth(g, note, _monoSmall, fullW - 40))
                { g.DrawString(l, _monoSmall, Brushes.Gray, x, y); y += _monoSmall.Height + 2; }
            y += 6;

            // footer first, so the list knows how much room it has
            using var prompt = Ui.Font(11f, FontStyle.Bold);
            var footerH = prompt.Height * 2 + 10;
            var detailLines = new List<string>();
            var cur = items[_secCursor];
            if (!string.IsNullOrEmpty(cur.Note)) detailLines.AddRange(WrapTextToWidth(g, cur.Note!, _monoSmall, fullW - 56));
            detailLines.AddRange(WrapTextToWidth(g, HowInstalls(cur), _monoSmall, fullW - 56));
            if (cur.Hardware != HardwareTag.Any) detailLines.Add($"Shown because you said you have this hardware ({HardwareLabel(cur.Hardware).Replace(" only", "")}).");
            if (_secNotice.Length > 0) detailLines.AddRange(WrapTextToWidth(g, _secNotice, _monoSmall, fullW - 56));
            var detailH = detailLines.Count * (_monoSmall.Height + 2) + 14;

            var rowH = _monoSmall.Height + 5;
            var visible = Math.Max(3, (int)((bottom - footerH - detailH - y) / rowH));
            if (_secCursor < _secScroll) _secScroll = _secCursor;
            if (_secCursor >= _secScroll + visible) _secScroll = _secCursor - visible + 1;
            _secScroll = Math.Clamp(_secScroll, 0, Math.Max(0, items.Count - visible));

            for (int i = _secScroll; i < Math.Min(items.Count, _secScroll + visible); i++)
            {
                var item = items[i];
                var on = _picked.Contains(item);
                var isCur = i == _secCursor;
                if (isCur) g.FillRectangle(Brushes.DarkSlateGray, x - 6, y - 1, fullW - 20, rowH);
                var tag = $"{(isCur ? "-->" : "   ")} [{(on ? "x" : " ")}] ";
                g.DrawString(tag + item.FriendlyName, _monoSmall, on ? Brushes.LimeGreen : (isCur ? Brushes.White : Brushes.Gainsboro), x, y);
                var hint = item.Hardware != HardwareTag.Any ? $"{HardwareLabel(item.Hardware)} - {ShortHow(item)}" : ShortHow(item);
                var hw = g.MeasureString(hint, _monoTiny).Width;
                g.DrawString(hint, _monoTiny, Brushes.DimGray, fullX + fullW - hw - 18, y + 2);
                y += rowH;
            }
            if (items.Count > visible)
                g.DrawString($"(showing {_secScroll + 1}-{Math.Min(items.Count, _secScroll + visible)} of {items.Count} - UP/DOWN scrolls)", _monoTiny, Brushes.DimGray, x, y);

            // detail box for the highlighted app
            var dy = bottom - footerH - detailH + 4;
            g.DrawLine(Pens.DimGray, x - 6, dy - 4, fullX + fullW - 16, dy - 4);
            foreach (var l in detailLines)
            {
                g.DrawString(l, _monoSmall, l == _secNotice || (_secNotice.Length > 0 && _secNotice.Contains(l)) ? Brushes.Orange : Brushes.Gray, x, dy);
                dy += _monoSmall.Height + 2;
            }

            var fy = bottom - footerH + 6;
            g.DrawString("UP/DOWN move   SPACE tick/untick   A tick all   N untick all", prompt, Brushes.Gold, x, fy);
            g.DrawString(_secIndex >= _sections.Count - 1 ? "ENTER finish and review   BACKSPACE previous section" : "ENTER next section   BACKSPACE previous section", prompt, Brushes.Gold, x, fy + prompt.Height + 2);
        }

        // ======================================================= "are you sure" review

        private List<AppEntry> PickedInCatalogOrder() => _catalog.Where(_picked.Contains).ToList();

        private void HandleReviewKey(Keys key)
        {
            switch (key)
            {
                case Keys.Up: _reviewScroll = Math.Max(0, _reviewScroll - 1); break;
                case Keys.Down: _reviewScroll++; break;
                case Keys.PageUp: _reviewScroll = Math.Max(0, _reviewScroll - 10); break;
                case Keys.PageDown: _reviewScroll += 10; break;
                case Keys.Home: _reviewScroll = 0; break;
                case Keys.Y:
                    _selectedApps.Clear();
                    _selectedApps.AddRange(PickedInCatalogOrder());
                    if (_selectedApps.Count > 0 && _selectedApps.Count == _catalog.Count) UnlockAchievement("completionist");
                    GoToConfirmBackup();
                    break;
                case Keys.B:
                case Keys.Back:
                case Keys.Left:
                    GoToSection(_sections.Count - 1);
                    break;
                case Keys.N:
                    _wizardPhase = WizardPhase.ConfirmQuit;
                    break;
            }
        }

        private void DrawReview(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var apps = PickedInCatalogOrder();
            var x = fullX + 16;
            var y = fullY + titleHeight + 10;
            var bottom = fullY + fullH - 12;

            var winget = apps.Count(a => a.Kind is AppKind.Winget or AppKind.Bundle);
            var links = apps.Count(a => a.Kind == AppKind.Link);
            var manual = apps.Count(a => a.Kind == AppKind.Manual);
            g.DrawString($"REVIEW: you picked {apps.Count} app(s)", _headingFont, Brushes.Gold, x, y);
            y += _headingFont.Height + 4;
            var summary = $"{winget} install silently through winget, {manual} are downloaded from a vendor website, {links} open a download page for you to finish.";
            foreach (var l in WrapTextToWidth(g, summary, _monoSmall, fullW - 40))
            { g.DrawString(l, _monoSmall, Brushes.Gray, x, y); y += _monoSmall.Height + 2; }
            y += 6;

            using var prompt = Ui.Font(12f, FontStyle.Bold);
            var footerH = prompt.Height * 2 + _monoSmall.Height + 16;

            // flat list: a heading per section, then its picked apps
            var rows = new List<(string Text, Brush Brush, bool Heading)>();
            string? last = null;
            foreach (var a in apps)
            {
                if (a.Category != last) { rows.Add(($"-- {a.Category} --", Brushes.DeepSkyBlue, true)); last = a.Category; }
                rows.Add(($"   [x] {a.FriendlyName}   ({ShortHow(a)})", Brushes.LimeGreen, false));
            }
            if (rows.Count == 0) rows.Add(("   (nothing picked - only the Windows settings steps will follow)", Brushes.Gray, false));

            var rowH = _monoSmall.Height + 3;
            var visible = Math.Max(3, (int)((bottom - footerH - y) / rowH));
            _reviewScroll = Math.Clamp(_reviewScroll, 0, Math.Max(0, rows.Count - visible));
            for (int i = _reviewScroll; i < Math.Min(rows.Count, _reviewScroll + visible); i++)
            {
                g.DrawString(rows[i].Text, _monoSmall, rows[i].Brush, x, y);
                y += rowH;
            }
            if (rows.Count > visible)
                g.DrawString($"(showing {_reviewScroll + 1}-{Math.Min(rows.Count, _reviewScroll + visible)} of {rows.Count} - UP/DOWN/PAGE scrolls)", _monoTiny, Brushes.DimGray, x, y);

            var fy = bottom - footerH + 6;
            g.DrawString("Nothing has been installed or changed yet. Next come the restore point, the Windows settings", _monoSmall, Brushes.Gray, x, fy);
            fy += _monoSmall.Height + 4;
            g.DrawString("Are you sure you want to continue with these?   [Y] Yes, continue", prompt, Brushes.Gold, x, fy);
            g.DrawString("[B] Back and change   [N] Quit", prompt, Brushes.Gold, x, fy + prompt.Height + 2);
        }

        // ======================================================= per-tweak tick list

        private readonly HashSet<string> _tweakOff = new();
        private int _tweakCursor, _tweakScroll;

        // Rows that were already chosen on their own screen earlier (DNS, cleanup, app removal):
        // they stay in the list so the whole picture is visible, but are not ticked here.
        private static bool IsLockedTweak(string name) =>
            name.StartsWith("Cleanup:", StringComparison.Ordinal) ||
            name.StartsWith("Remove app:", StringComparison.Ordinal) ||
            name.StartsWith("DNS servers", StringComparison.Ordinal);

        private void HandleTweakKey(Keys key)
        {
            var n = _tweakPreview.Count;
            switch (key)
            {
                case Keys.Up: _tweakCursor = Math.Max(0, _tweakCursor - 1); break;
                case Keys.Down: _tweakCursor = Math.Min(Math.Max(0, n - 1), _tweakCursor + 1); break;
                case Keys.PageUp: _tweakCursor = Math.Max(0, _tweakCursor - 8); break;
                case Keys.PageDown: _tweakCursor = Math.Min(Math.Max(0, n - 1), _tweakCursor + 8); break;
                case Keys.Space:
                    if (n > 0 && !IsLockedTweak(_tweakPreview[_tweakCursor].Name))
                    {
                        var name = _tweakPreview[_tweakCursor].Name;
                        if (!_tweakOff.Remove(name)) _tweakOff.Add(name);
                    }
                    break;
                case Keys.A: _tweakOff.Clear(); break;
                case Keys.N:
                    foreach (var t in _tweakPreview.Where(t => !IsLockedTweak(t.Name))) _tweakOff.Add(t.Name);
                    break;
                case Keys.Enter:
                    // Only what is still ticked stays on the list (the final check counts it).
                    _tweakPreview.RemoveAll(t => _tweakOff.Contains(t.Name));
                    _applyTweaks = _tweakPreview.Count > 0;
                    _finalTyped = "";
                    _wizardPhase = WizardPhase.FinalReview;
                    break;
            }
        }

        private void DrawConfirmTweaks(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var x = fullX + 16;
            var y = fullY + titleHeight + 10;
            var bottom = fullY + fullH - 12;

            g.DrawString("Windows settings: choose the ones you want", _headingFont, Brushes.Gold, x, y);
            y += _headingFont.Height + 4;
            foreach (var l in WrapTextToWidth(g, "Nothing is applied without your OK. Untick any you don't want. Each one is saved to an undo script before it changes.", _monoSmall, fullW - 40))
            { g.DrawString(l, _monoSmall, Brushes.Gray, x, y); y += _monoSmall.Height + 2; }
            y += 6;

            using var prompt = Ui.Font(11f, FontStyle.Bold);
            var footerH = prompt.Height * 2 + 10;
            var cur = _tweakPreview.Count > 0 ? _tweakPreview[_tweakCursor] : default;
            var detail = new List<string>();
            if (_tweakPreview.Count > 0)
            {
                detail.AddRange(WrapTextToWidth(g, $"Now: {cur.Current}", _monoSmall, fullW - 56));
                detail.AddRange(WrapTextToWidth(g, $"Will become: {cur.NewValue}", _monoSmall, fullW - 56));
                if (IsLockedTweak(cur.Name)) detail.Add("(chosen on an earlier screen - go back to the start to change it)");
            }
            var detailH = detail.Count * (_monoSmall.Height + 2) + 14;

            var rowH = _monoSmall.Height + 4;
            var visible = Math.Max(3, (int)((bottom - footerH - detailH - y) / rowH));
            _tweakCursor = Math.Clamp(_tweakCursor, 0, Math.Max(0, _tweakPreview.Count - 1));
            if (_tweakCursor < _tweakScroll) _tweakScroll = _tweakCursor;
            if (_tweakCursor >= _tweakScroll + visible) _tweakScroll = _tweakCursor - visible + 1;
            _tweakScroll = Math.Clamp(_tweakScroll, 0, Math.Max(0, _tweakPreview.Count - visible));

            for (int i = _tweakScroll; i < Math.Min(_tweakPreview.Count, _tweakScroll + visible); i++)
            {
                var t = _tweakPreview[i];
                var locked = IsLockedTweak(t.Name);
                var on = locked || !_tweakOff.Contains(t.Name);
                var isCur = i == _tweakCursor;
                if (isCur) g.FillRectangle(Brushes.DarkSlateGray, x - 6, y - 1, fullW - 20, rowH);
                g.DrawString($"{(isCur ? "-->" : "   ")} [{(on ? "x" : " ")}] {t.Name}", _monoSmall,
                    locked ? Brushes.Gray : (on ? Brushes.LimeGreen : Brushes.Gold), x, y);
                y += rowH;
            }
            if (_tweakPreview.Count > visible)
                g.DrawString($"(showing {_tweakScroll + 1}-{Math.Min(_tweakPreview.Count, _tweakScroll + visible)} of {_tweakPreview.Count} - UP/DOWN scrolls)", _monoTiny, Brushes.DimGray, x, y);
            if (_tweakPreview.Count == 0)
                g.DrawString("No Windows settings are on offer for this setup - press ENTER to continue.", _monoSmall, Brushes.Gray, x, y);

            var dy = bottom - footerH - detailH + 4;
            g.DrawLine(Pens.DimGray, x - 6, dy - 4, fullX + fullW - 16, dy - 4);
            foreach (var l in detail) { g.DrawString(l, _monoSmall, Brushes.Gainsboro, x, dy); dy += _monoSmall.Height + 2; }

            var on2 = _tweakPreview.Count(t => IsLockedTweak(t.Name) || !_tweakOff.Contains(t.Name));
            var fy = bottom - footerH + 6;
            g.DrawString($"UP/DOWN move   SPACE tick/untick   A all   N none     ({on2} of {_tweakPreview.Count} will be applied)", prompt, Brushes.Gold, x, fy);
            g.DrawString("ENTER continue to the final check", prompt, Brushes.Gold, x, fy + prompt.Height + 2);
        }

        // ======================================================= Captures folder

        private string? _capturesPath;
        private List<CaptureLocation> _captureOptions = new();

        // Gaming PCs only (that is who uses Game Bar clips). Skipped on Office / Developer setups.
        private void GoToChooseCapturesOrNext()
        {
            _capturesPath = null;
            if (_profile != SetupProfile.Gaming) { GoToChooseCleanupOrNext(); return; }
            _captureOptions = CapturesFolder.Suggestions().Take(7).ToList();
            _wizardPhase = WizardPhase.ChooseCaptures;
        }

        private void HandleCapturesKey(Keys key)
        {
            if (key == Keys.D1 || key == Keys.NumPad1) { _capturesPath = null; GoToChooseCleanupOrNext(); return; }
            var index = DigitIndex(key);
            if (index != null && index >= 1 && index - 1 < _captureOptions.Count)
            {
                _capturesPath = _captureOptions[index.Value - 1].Path;
                GoToChooseCleanupOrNext();
                return;
            }
            if (key == Keys.B)
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description = "Choose where your Captures folder should live (a new folder is fine).",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    var chosen = dlg.SelectedPath;
                    // a bare drive like D:\ becomes D:\Captures
                    if (Path.GetPathRoot(chosen)?.TrimEnd('\\').Equals(chosen.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == true)
                        chosen = Path.Combine(chosen, "Captures");
                    _capturesPath = chosen;
                    GoToChooseCleanupOrNext();
                }
            }
        }

        private void DrawChooseCaptures(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var x = fullX + 16;
            var y = fullY + titleHeight + 12;
            g.DrawString("Optional: where should game clips and screenshots be saved?", _mono, Brushes.Gainsboro, x, y);
            y += _mono.Height + 8;

            var capNow = CapturesFolder.GetPath(CapturesFolder.CapturesId) ?? "unknown";
            var shotNow = CapturesFolder.GetPath(CapturesFolder.ScreenshotsId) ?? "unknown";
            void Info(string text, Brush b) { foreach (var l in WrapTextToWidth(g, text, _monoSmall, fullW - 40)) { g.DrawString(l, _monoSmall, b, x, y); y += _monoSmall.Height + 2; } }
            Info($"Right now, Xbox Game Bar clips go to:  {capNow}", Brushes.DeepSkyBlue);
            Info($"and Win+PrtScn screenshots go to:  {shotNow}", Brushes.DeepSkyBlue);
            if (capNow.Contains("OneDrive", StringComparison.OrdinalIgnoreCase) || shotNow.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
                Info("Part of that is inside OneDrive, so big video clips are uploaded and use your cloud storage.", Brushes.Orange);
            y += 8;

            g.DrawString("[1] Keep them where they are (recommended if unsure)", _monoSmall, Brushes.Gold, x, y);
            y += _monoSmall.Height + 5;
            for (int i = 0; i < _captureOptions.Count; i++)
            {
                g.DrawString($"[{i + 2}] {_captureOptions[i].Label}", _monoSmall, Brushes.Gold, x, y);
                y += _monoSmall.Height + 5;
            }
            g.DrawString("[B] Browse for a different folder...", _monoSmall, Brushes.Gold, x, y);
            y += _monoSmall.Height + 12;

            Info("Only where NEW clips go changes: nothing is moved or deleted. Screenshots go in a \"Screenshots\" folder inside it. You can untick either on the Windows settings screen, and the undo script puts the old folders back (sign out and in afterwards).", Brushes.DimGray);
            y += 8;
            using var prompt = Ui.Font(14f, FontStyle.Bold);
            g.DrawString("Choose a number, or B to browse", prompt, Brushes.Gold, x, y);
        }
    }
}
