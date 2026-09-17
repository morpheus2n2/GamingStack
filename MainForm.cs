using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Management;
using System.Media;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GamingStackGUI
{
    /// <summary>
    /// Flow: Bios -> Boot (original animation) -> Desktop (placeholder) -> Terminal
    /// (interactive install wizard, then the real installer, via InstallerEngine).
    /// Pressing Delete during the Bios stage detours into a joke Bsod stage for a few
    /// seconds, then carries on into Boot as normal.
    /// </summary>
    public class MainForm : Form
    {
        private enum Stage { Bios, Bsod, Boot, Desktop, Terminal }

        // The interactive Q&A inside the Terminal stage, before/instead of the real
        // install run. See HandleWizardKey for the full flow between these.
        private enum WizardPhase
        {
            ConfirmAll,          // "Install everything shown? Y/N"
            ChooseIndividually,  // "Want to choose what's installed? Y/N"
            PerApp,              // asking Y/N for one catalog entry at a time
            ConfirmQuit,         // "Just want to quit? Y/N"
            EasterEgg,           // declined everything - a little joke, then Farewell
            Installing,          // InstallerEngine is actually running
            Farewell             // final thank-you screen (reached from Installing or EasterEgg)
        }

        private const int BiosMinHoldMs = 13000;
        private const int BsodHoldMs = 7000;
        private const int BootHoldMs = 10000;
        private const int SoundTriggerMs = 8000;
        private const int MemCountDurationMs = 3000;
        private const int DesktopHoldMs = 7000;
        private const int TerminalOpenMs = 400;
        private const int EasterEggHoldMs = 5000;
        private const double CloudSpeedMultiplier = 1.8;

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
        private Image? _wallpaper;

        private readonly Font _mono = new("Consolas", 14f, FontStyle.Regular);
        private readonly Font _monoSmall = new("Consolas", 12f, FontStyle.Regular);
        private readonly Font _headingFont = new("Consolas", 12f, FontStyle.Bold | FontStyle.Underline);
        private readonly Font _monoLarge = new("Consolas", 22f, FontStyle.Bold);

        // ---- installer + wizard ----
        private readonly InstallerEngine _installer = new();
        private readonly List<string> _terminalLines = new();
        private readonly object _terminalLock = new();

        private WizardPhase _wizardPhase = WizardPhase.ConfirmAll;
        private readonly List<AppEntry> _selectedApps = new();
        private int _perAppIndex;
        private bool?[] _perAppDecisions = Array.Empty<bool?>();

        // ---- terminal window: draggable/resizable ----
        // Null until PaintTerminal first runs, which seeds these from the original
        // fixed 0.7/0.62-of-screen defaults; from then on dragging/resizing just
        // mutates these directly instead of recomputing from Width/Height every frame.
        private int? _termX, _termY, _termW, _termH;
        private bool _draggingTerm, _resizingTerm;
        private Point _dragMouseStart;
        private Rectangle _dragTermStart;
        private const int TermResizeGrip = 18;
        private const int TermMinWidth = 480;
        private const int TermMinHeight = 280;

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
            _installer.OnFinished += () =>
            {
                _wizardPhase = WizardPhase.Farewell;
                _phaseWatch.Restart();
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
            LoadEmbeddedWallpaper();
            BuildBiosTimeline();
            _stageWatch.Restart();
            _timer.Start();
        }

        // ---------- embedded assets ----------

        private static Stream? FindEmbeddedResource(string suffix)
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            return name == null ? null : asm.GetManifestResourceStream(name);
        }

        private void LoadEmbeddedSound()
        {
            try
            {
                using var stream = FindEmbeddedResource("win95.wav");
                if (stream == null) return;

                // SoundPlayer needs to own a seekable stream for the lifetime of playback,
                // so copy it out of the resource stream rather than holding that one open.
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                _soundPlayer = new SoundPlayer(ms);
                _soundPlayer.Load();
            }
            catch
            {
                // Non-fatal - the boot animation still runs, just without sound.
            }
        }

        private void LoadEmbeddedWallpaper()
        {
            try
            {
                using var stream = FindEmbeddedResource("wallpaper.jpg");
                if (stream == null) return;

                var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                _wallpaper = Image.FromStream(ms);
            }
            catch
            {
                // Non-fatal - falls back to the placeholder fill.
            }
        }

        private static void PlayBeepAsync(int freq, int durationMs)
        {
            Task.Run(() =>
            {
                try { Console.Beep(freq, durationMs); }
                catch { /* no speaker / unsupported - non-fatal */ }
            });
        }

        // ---------- fake BIOS timeline (real hardware) ----------

        private void BuildBiosTimeline()
        {
            _timedLines.Clear();

            void Add(long at, string text, Color color) =>
                _timedLines.Add(new TimedLine { At = at, Text = text, Color = color });

            Add(0, "American Megatrends Inc.", Color.Gainsboro);
            Add(150, "GAMINGSTACK-UEFI BIOS v2.1.0", Color.Gray);

            var cpu = GetWmiString("Win32_Processor", "Name");
            Add(1200, $"CPU: {cpu}", Color.Gainsboro);
            Add(1700, "CPU: OK", Color.LightGreen);

            Add(2300, "Memory Test:", Color.Gainsboro);
            _memCountAt = 2600;
            _targetRamGb = GetTotalRamGb();
            var memCountEndAt = _memCountAt + MemCountDurationMs;

            var afterMem = memCountEndAt + 400;
            Add(afterMem, "Graphics Adapter:", Color.Gainsboro);
            var gpu = GetWmiString("Win32_VideoController", "Name");
            Add(afterMem + 400, $"  {gpu} - OK", Color.LightGreen);

            var disksStart = afterMem + 900;
            Add(disksStart, "Storage Devices:", Color.Gainsboro);
            long diskCursor = disksStart + 300;
            foreach (var disk in GetDisks())
            {
                Add(diskCursor, $"  {disk}", Color.LightGreen);
                diskCursor += 300;
            }

            // Not added to the timed reveal - the "Press DEL" prompt is drawn separately
            // in PaintBios, pinned to the bottom of the screen and visible from frame
            // one. _footerAt is still used to time the footer beep below.
            _footerAt = diskCursor + 400;
        }

        private static string GetWmiString(string wmiClass, string property)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
                foreach (var obj in searcher.Get())
                    return obj[property]?.ToString()?.Trim() ?? "Unknown";
            }
            catch
            {
                // Ignored - falls through to "Unknown" below.
            }
            return "Unknown";
        }

        private static long GetTotalRamGb()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    var bytes = Convert.ToInt64(obj["TotalPhysicalMemory"]);
                    return (long)Math.Round(bytes / 1024.0 / 1024.0 / 1024.0);
                }
            }
            catch
            {
                // Ignored - falls through to 0 below.
            }
            return 0;
        }

        private static IEnumerable<string> GetDisks()
        {
            var results = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Model, Size FROM Win32_DiskDrive");
                foreach (var obj in searcher.Get())
                {
                    var model = obj["Model"]?.ToString() ?? "Unknown Disk";
                    var sizeGb = obj["Size"] != null
                        ? (long)Math.Round(Convert.ToInt64(obj["Size"]) / 1024.0 / 1024.0 / 1024.0)
                        : 0;
                    results.Add($"{model} - {sizeGb}GB - OK");
                }
            }
            catch
            {
                // Ignored - falls through to the fallback line below.
            }
            if (results.Count == 0) results.Add("Unknown Disk - OK");
            return results;
        }

        // ---------- stage progression ----------

        private void Advance()
        {
            switch (_stage)
            {
                case Stage.Bios:
                    var elapsed = _stageWatch.ElapsedMilliseconds;
                    var memCountEndAt = _memCountAt + MemCountDurationMs;

                    if (!_postBeepPlayed)
                    {
                        _postBeepPlayed = true;
                        PlayBeepAsync(600, 120);
                    }

                    if (elapsed >= _memCountAt && elapsed < memCountEndAt)
                    {
                        if (elapsed - _lastMemBeepAt >= 180)
                        {
                            _lastMemBeepAt = elapsed;
                            PlayBeepAsync(1200, 25);
                        }
                    }
                    else if (elapsed >= memCountEndAt && !_memBeepDone)
                    {
                        _memBeepDone = true;
                        PlayBeepAsync(800, 150);
                    }

                    if (elapsed >= _footerAt && !_footerBeepPlayed)
                    {
                        _footerBeepPlayed = true;
                        PlayBeepAsync(900, 80);
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
                    if (_stageWatch.ElapsedMilliseconds >= DesktopHoldMs)
                    {
                        _stage = Stage.Terminal;
                        _stageWatch.Restart();
                    }
                    break;

                case Stage.Terminal:
                    if (_wizardPhase == WizardPhase.EasterEgg &&
                        _phaseWatch.ElapsedMilliseconds >= EasterEggHoldMs)
                    {
                        _wizardPhase = WizardPhase.Farewell;
                        _phaseWatch.Restart();
                    }
                    break;
            }
        }

        // ---------- install wizard ----------

        private void StartInstall()
        {
            _wizardPhase = WizardPhase.Installing;
            _phaseWatch.Restart();
            _ = RunInstallerSafelyAsync();
        }

        private async Task RunInstallerSafelyAsync()
        {
            try
            {
                await _installer.RunAsync(_selectedApps);
            }
            catch (Exception ex)
            {
                lock (_terminalLock) { _terminalLines.Add($"FATAL: {ex.Message}"); }
                _wizardPhase = WizardPhase.Farewell;
                _phaseWatch.Restart();
            }
        }

        private void HandleWizardKey(Keys key)
        {
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
                        _selectedApps.AddRange(InstallerEngine.Catalog);
                        StartInstall();
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
                        _perAppDecisions = new bool?[InstallerEngine.Catalog.Count];
                        _wizardPhase = WizardPhase.PerApp;
                    }
                    else
                    {
                        _wizardPhase = WizardPhase.ConfirmQuit;
                    }
                    break;

                case WizardPhase.PerApp:
                    _perAppDecisions[_perAppIndex] = answer;
                    if (answer) _selectedApps.Add(InstallerEngine.Catalog[_perAppIndex]);
                    _perAppIndex++;
                    if (_perAppIndex >= InstallerEngine.Catalog.Count)
                        StartInstall();
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

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.Clear(Color.Black);

            switch (_stage)
            {
                case Stage.Bios: PaintBios(g); break;
                case Stage.Bsod: PaintBsod(g); break;
                case Stage.Boot: PaintBoot(g); break;
                case Stage.Desktop: PaintDesktop(g); break;
                case Stage.Terminal: PaintTerminal(g); break;
            }
        }

        // ---- BIOS ----

        private void DrawBiosLogo(Graphics g)
        {
            // Original chip-style mark - not a reproduction of any real BIOS vendor logo.
            const int x = 40, y = 30, size = 140;

            using var chipPen = new Pen(Color.Cyan, 4);
            g.DrawRectangle(chipPen, x, y, size, size);

            using var pinPen = new Pen(Color.Gray, 3);
            for (int i = 0; i < 4; i++)
            {
                int offset = (int)(size / 5.0 * (i + 1));
                g.DrawLine(pinPen, x - 16, y + offset, x, y + offset);
                g.DrawLine(pinPen, x + size, y + offset, x + size + 16, y + offset);
            }

            using var monoFont = new Font("Consolas", 48f, FontStyle.Bold);
            var monoSize = g.MeasureString("GS", monoFont);
            g.DrawString("GS", monoFont, Brushes.Magenta,
                x + (size - monoSize.Width) / 2, y + (size - monoSize.Height) / 2);

            using var wordFont = new Font("Consolas", 20f, FontStyle.Regular);
            var wordSize = g.MeasureString("GAMINGSTACK SYSTEMS", wordFont);
            g.DrawString("GAMINGSTACK SYSTEMS", wordFont, Brushes.Gainsboro,
                x + size + 30, y + (size - wordSize.Height) / 2);
        }

        private void PaintBios(Graphics g)
        {
            DrawBiosLogo(g);

            var elapsed = _stageWatch.ElapsedMilliseconds;
            int x = 60, y = 200;

            foreach (var line in _timedLines)
            {
                if (elapsed < line.At) continue;

                using var brush = new SolidBrush(line.Color);
                g.DrawString(line.Text, _mono, brush, x, y);
                y += 26;

                if (line.Text == "Memory Test:" && elapsed >= _memCountAt)
                {
                    var progress = Math.Clamp((elapsed - _memCountAt) / (double)MemCountDurationMs, 0.0, 1.0);
                    var displayedMb = (long)(progress * _targetRamGb * 1024);
                    var done = progress >= 1.0;
                    using var memBrush = new SolidBrush(done ? Color.LightGreen : Color.Gainsboro);
                    var suffix = done ? " OK" : "";
                    g.DrawString($"  {displayedMb:N0}MB{suffix}", _mono, memBrush, x, y);
                    y += 26;
                }
            }

            // "Press DEL" prompt: pinned to the bottom of the screen and visible from
            // the first frame (not part of the timed reveal above) - the whole point is
            // for it to be impossible to miss.
            using var delFont = new Font("Consolas", 15f, FontStyle.Bold);
            const string delPrompt = "Press DEL to enter BIOS Setup... Go on, I dare you!";
            var delSize = g.MeasureString(delPrompt, delFont);
            g.DrawString(delPrompt, delFont, Brushes.Goldenrod, (Width - delSize.Width) / 2, Height - delSize.Height - 30);
        }

        // ---- Easter egg: press Delete during Bios ----

        private void PaintBsod(Graphics g)
        {
            g.Clear(Color.FromArgb(0, 0, 170));

            using var bigFont = new Font("Consolas", 30f, FontStyle.Bold);
            using var smallFont = new Font("Consolas", 15f, FontStyle.Regular);

            var blocks = new (string Text, Font Font)[]
            {
                (":(", bigFont),
                ("Why would you press that,\nyou have just broken your nice new PC.", bigFont),
                ("Don't worry, we probably weren't using that RAM anyway.\nRebooting in a totally normal, definitely-fine way...", smallFont)
            };

            const int gap = 24;
            var sizes = blocks.Select(b => g.MeasureString(b.Text, b.Font)).ToArray();
            var totalHeight = sizes.Sum(s => s.Height) + gap * (blocks.Length - 1);
            var maxWidth = sizes.Max(s => s.Width);

            // Centered as a block on screen; each line left-justified within that block.
            var blockLeft = (Width - maxWidth) / 2f;
            var y = (Height - totalHeight) / 2f;

            for (int i = 0; i < blocks.Length; i++)
            {
                g.DrawString(blocks[i].Text, blocks[i].Font, Brushes.White, blockLeft, y);
                y += sizes[i].Height + gap;
            }
        }

        // ---- Boot: original retro animation (sky, clouds, waving flag) ----

        private void DrawSky(Graphics g)
        {
            // Clear daytime blue rather than a dusky purple - reads more like an actual
            // sky (and matches the desktop wallpaper's palette) for the clouds to move
            // across.
            using var sky = new LinearGradientBrush(
                new Rectangle(0, 0, Width, Height),
                Color.FromArgb(70, 130, 220),
                Color.FromArgb(190, 225, 250),
                LinearGradientMode.Vertical);
            g.FillRectangle(sky, 0, 0, Width, Height);
        }

        private void DrawClouds(Graphics g, double t)
        {
            DrawCloud(g, 90, 60, 18 * CloudSpeedMultiplier, t);
            DrawCloud(g, 220, 40, 26 * CloudSpeedMultiplier, t);
            DrawCloud(g, 400, 90, 34 * CloudSpeedMultiplier, t);

            // A few extra, quicker clouds - phase-shifted (t + offset) so they don't
            // all line up identically with the three above, for a busier sky.
            DrawCloud(g, 140, 30, 44 * CloudSpeedMultiplier, t + 0.6);
            DrawCloud(g, 300, 22, 50 * CloudSpeedMultiplier, t + 1.3);
            DrawCloud(g, 60, 45, 40 * CloudSpeedMultiplier, t + 2.1);
        }

        private void DrawCloud(Graphics g, int baseY, int size, double speedPxPerSec, double t)
        {
            var cloudWidth = size * 3;
            var distance = (t * speedPxPerSec) % (Width + cloudWidth);
            var x = Width - distance;

            using var brush = new SolidBrush(Color.FromArgb(230, 235, 245));
            g.FillEllipse(brush, (float)x, baseY, size * 1.4f, size * 0.8f);
            g.FillEllipse(brush, (float)x + size * 0.6f, baseY - size * 0.2f, size * 1.2f, size * 0.9f);
            g.FillEllipse(brush, (float)x + size * 1.3f, baseY, size * 1.1f, size * 0.7f);
        }

        private void DrawWavingFlag(Graphics g, double t)
        {
            const int flagWidth = 460, flagHeight = 190, stripCount = 72;
            const int reservedBottom = 200;   // leave room for the title + progress bar below
            const int poleHeight = 320;

            int poleX = (Width - flagWidth) / 2;
            int poleTopY = Math.Max(40, (Height - reservedBottom - poleHeight) / 2);

            using var polePen = new Pen(Color.Silver, 6);
            g.DrawLine(polePen, poleX, poleTopY, poleX, poleTopY + poleHeight);
            g.FillEllipse(Brushes.Silver, poleX - 9, poleTopY - 18, 18, 18);

            int flagX = poleX;
            int flagY = poleTopY + 18;
            float stripW = flagWidth / (float)stripCount;
            float third = flagHeight / 3f;

            using var bandTop = new SolidBrush(Color.FromArgb(0, 210, 230));
            using var bandMid = new SolidBrush(Color.FromArgb(18, 16, 36));
            using var bandBot = new SolidBrush(Color.FromArgb(255, 0, 170));

            for (int i = 0; i < stripCount; i++)
            {
                float localX = i * stripW;
                float wave = (float)(Math.Sin((localX * 0.06) + t * 2.1) * 18.0);
                float attachFactor = Math.Min(1f, localX / 40f);
                wave *= attachFactor;

                float sx = flagX + localX;
                float sy = flagY + wave;

                g.FillRectangle(bandTop, sx, sy, stripW + 1, third);
                g.FillRectangle(bandMid, sx, sy + third, stripW + 1, third);
                g.FillRectangle(bandBot, sx, sy + third * 2, stripW + 1, third);
            }

            using var flagFont = new Font("Consolas", 26f, FontStyle.Bold);
            const string label = "GAMINGSTACK";
            var labelSize = g.MeasureString(label, flagFont);
            g.DrawString(label, flagFont, Brushes.White,
                flagX + (flagWidth - labelSize.Width) / 2, flagY + flagHeight / 2 - labelSize.Height / 2);
        }

        private void PaintBoot(Graphics g)
        {
            var elapsed = _stageWatch.ElapsedMilliseconds;
            var t = elapsed / 1000.0;

            DrawSky(g);
            DrawClouds(g, t);
            DrawWavingFlag(g, t);

            const string title = "Starting GamingStack 95...";
            var titleSize = g.MeasureString(title, _monoLarge);
            g.DrawString(title, _monoLarge, Brushes.White, (Width - titleSize.Width) / 2, Height - 160);

            // Full-width, segmented block style (like the real Win9x boot progress bar)
            // instead of a flat continuous fill in one static color - a run of discrete
            // blocks, cycling through a gradient, reads much less "stuck" than one solid
            // color creeping across the screen.
            var pct = Math.Min(1.0, elapsed / (double)BootHoldMs);
            const int barMargin = 60;
            const int barHeight = 26;
            const int blockCount = 44;
            const int blockGap = 3;

            var barWidth = Width - barMargin * 2;
            var barX = barMargin;
            var barY = Height - 110;

            g.DrawRectangle(Pens.Gainsboro, barX, barY, barWidth, barHeight);

            var innerWidth = barWidth - 4;
            var blockWidth = (innerWidth - blockGap * (blockCount - 1)) / (float)blockCount;
            var filledBlocks = (int)(pct * blockCount);

            for (int i = 0; i < filledBlocks; i++)
            {
                var hueT = i / (float)(blockCount - 1);
                using var blockBrush = new SolidBrush(LerpColor(Color.FromArgb(0, 210, 230), Color.FromArgb(255, 0, 170), hueT));
                var bx = barX + 2 + i * (blockWidth + blockGap);
                g.FillRectangle(blockBrush, bx, barY + 2, blockWidth, barHeight - 4);
            }
        }

        private static Color LerpColor(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        // ---- Desktop / shared wallpaper ----

        private void PaintDesktop(Graphics g)
        {
            DrawWallpaper(g);
            DrawDesktopIcons(g);
            DrawTaskbar(g);
        }

        private void DrawWallpaper(Graphics g)
        {
            if (_wallpaper == null)
            {
                using var bg = new SolidBrush(Color.FromArgb(0, 90, 90));
                g.FillRectangle(bg, 0, 0, Width, Height);

                using var font = new Font("Consolas", 16f, FontStyle.Italic);
                const string msg = "[ Placeholder wallpaper - drop your own image in here later ]";
                var size = g.MeasureString(msg, font);
                g.DrawString(msg, font, Brushes.White, (Width - size.Width) / 2, (Height - size.Height) / 2);
                return;
            }

            // "Cover" fit - scale to fill the screen without distorting, cropping overflow,
            // since the wallpaper's aspect ratio won't always match the monitor's.
            float scale = Math.Max(Width / (float)_wallpaper.Width, Height / (float)_wallpaper.Height);
            var drawW = _wallpaper.Width * scale;
            var drawH = _wallpaper.Height * scale;
            var drawX = (Width - drawW) / 2f;
            var drawY = (Height - drawH) / 2f;
            g.DrawImage(_wallpaper, drawX, drawY, drawW, drawH);
        }

        private void DrawDesktopIcons(Graphics g)
        {
            DrawIcon(g, 40, 40, "System", DrawDriveGlyph);
            DrawIcon(g, 40, 150, "Files", DrawFolderGlyph);
        }

        private void DrawIcon(Graphics g, int x, int y, string label, Action<Graphics, int, int> drawGlyph)
        {
            drawGlyph(g, x, y);

            var labelSize = g.MeasureString(label, _mono);
            var labelX = x + 24 - labelSize.Width / 2;
            var labelY = y + 46;

            using var shadowBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
            g.FillRectangle(shadowBrush, labelX - 2, labelY - 1, labelSize.Width + 4, labelSize.Height + 2);
            g.DrawString(label, _mono, Brushes.White, labelX, labelY);
        }

        private static void DrawFolderGlyph(Graphics g, int x, int y)
        {
            using var tabBrush = new SolidBrush(Color.FromArgb(255, 205, 60));
            g.FillRectangle(tabBrush, x, y + 4, 20, 8);

            using var bodyBrush = new SolidBrush(Color.FromArgb(255, 190, 40));
            g.FillRectangle(bodyBrush, x, y + 10, 48, 30);

            using var pen = new Pen(Color.FromArgb(160, 110, 20), 1);
            g.DrawRectangle(pen, x, y + 10, 48, 30);
            g.DrawRectangle(pen, x, y + 4, 20, 8);
        }

        private static void DrawDriveGlyph(Graphics g, int x, int y)
        {
            using var bodyBrush = new SolidBrush(Color.FromArgb(210, 210, 220));
            g.FillRectangle(bodyBrush, x, y, 48, 36);

            using var pen = new Pen(Color.FromArgb(90, 90, 100), 1);
            g.DrawRectangle(pen, x, y, 48, 36);

            using var slotBrush = new SolidBrush(Color.FromArgb(40, 40, 50));
            g.FillRectangle(slotBrush, x + 6, y + 6, 36, 6);

            using var ledBrush = new SolidBrush(Color.FromArgb(0, 220, 90));
            g.FillEllipse(ledBrush, x + 6, y + 22, 8, 8);
        }

        private readonly Font _clockFont = new("Consolas", 10f, FontStyle.Regular);

        private void DrawTaskbar(Graphics g)
        {
            const int barHeight = 44;
            using var barBrush = new SolidBrush(Color.FromArgb(192, 192, 192));
            g.FillRectangle(barBrush, 0, Height - barHeight, Width, barHeight);

            // Start button: sized around the actual text width instead of a fixed 80px,
            // and the text is centered both ways within it - a fixed-width button with
            // left-anchored text meant "Start" was never actually centered in it.
            using var btnBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
            var startText = "Start";
            var startTextSize = g.MeasureString(startText, _mono);
            var startWidth = (int)startTextSize.Width + 36;
            var startRect = new Rectangle(6, Height - barHeight + 5, startWidth, barHeight - 10);
            g.FillRectangle(btnBrush, startRect);
            g.DrawRectangle(Pens.Gray, startRect);
            g.DrawString(startText, _mono, Brushes.Black,
                startRect.X + (startRect.Width - startTextSize.Width) / 2,
                startRect.Y + (startRect.Height - startTextSize.Height) / 2);

            // Clock: time on top, date underneath. Anchored from the right edge (not
            // computed via a left position + width) and using a fixed, small per-line
            // height rather than MeasureString's height (which pads more than a 10pt
            // font actually needs) - together these were letting the date creep past
            // both the right edge and the bottom of the screen on some setups.
            const int clockLineHeight = 14;
            const int clockRightMargin = 20;
            var timeStr = DateTime.Now.ToString("HH:mm");
            var dateStr = DateTime.Now.ToString("dd/MM/yyyy");
            var timeSize = g.MeasureString(timeStr, _clockFont);
            var dateSize = g.MeasureString(dateStr, _clockFont);
            var clockRight = Width - clockRightMargin;
            var clockBlockHeight = clockLineHeight * 2;
            var clockTop = Height - barHeight + (barHeight - clockBlockHeight) / 2;
            g.DrawString(timeStr, _clockFont, Brushes.Black, clockRight - timeSize.Width, clockTop);
            g.DrawString(dateStr, _clockFont, Brushes.Black, clockRight - dateSize.Width, clockTop + clockLineHeight);
        }

        // ---- Terminal (opens on top of the desktop, runs the interactive wizard then the installer) ----

        private void PaintTerminal(Graphics g)
        {
            DrawWallpaper(g);
            DrawDesktopIcons(g);
            DrawTaskbar(g);

            var elapsed = _stageWatch.ElapsedMilliseconds;
            var openProgress = Math.Clamp(elapsed / (double)TerminalOpenMs, 0.0, 1.0);
            var eased = 1 - Math.Pow(1 - openProgress, 3);

            if (_termW == null)
            {
                _termW = (int)(Width * 0.7);
                _termH = (int)(Height * 0.62);
                _termX = (Width - _termW.Value) / 2;
                _termY = (Height - _termH.Value) / 2;
            }
            var fullW = _termW!.Value;
            var fullH = _termH!.Value;
            var fullX = _termX!.Value;
            var fullY = _termY!.Value;

            var w = (int)(fullW * eased);
            var h = (int)(fullH * eased);
            if (w <= 0 || h <= 0) return;
            var x = fullX + (fullW - w) / 2;
            var y = fullY + (fullH - h) / 2;

            g.FillRectangle(Brushes.Black, x, y, w, h);
            g.DrawRectangle(Pens.Gainsboro, x, y, w, h);

            if (eased < 0.98) return;

            var titleHeight = _titleBarHeight;
            using var titleBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
            g.FillRectangle(titleBrush, fullX, fullY, fullW, titleHeight);
            var titleTextSize = g.MeasureString("GamingStack Installer", _mono);
            g.DrawString("GamingStack Installer", _mono, Brushes.Gainsboro,
                fullX + 16, fullY + (titleHeight - titleTextSize.Height) / 2);

            // Resize grip, bottom-right corner (drag it to resize); drag the title bar
            // to move the window. Both handled in OnMouseDown/Move/Up below.
            using (var gripPen = new Pen(Color.Gray, 2))
            {
                var gripX = fullX + fullW - TermResizeGrip;
                var gripY = fullY + fullH - TermResizeGrip;
                for (int i = 6; i <= TermResizeGrip; i += 6)
                    g.DrawLine(gripPen, gripX + TermResizeGrip - i, gripY + TermResizeGrip, gripX + TermResizeGrip, gripY + TermResizeGrip - i);
            }

            switch (_wizardPhase)
            {
                case WizardPhase.Installing:
                    DrawInstallerLog(g, fullX, fullY, fullW, fullH, titleHeight, elapsed);
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
        private List<(string Category, List<int> Indices)>? _wizardColumn0;
        private List<(string Category, List<int> Indices)>? _wizardColumn1;

        private void BuildWizardColumnsIfNeeded()
        {
            if (_wizardColumn0 != null) return;

            var catalog = InstallerEngine.Catalog;

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

            // Greedily bin-pack whole categories (never split one across columns) into
            // two columns, always dropping the next category into whichever column is
            // currently shorter - keeps the two columns roughly the same height even
            // though categories vary in size.
            var col0 = new List<(string, List<int>)>();
            var col1 = new List<(string, List<int>)>();
            var height0 = 0;
            var height1 = 0;
            foreach (var group in groups)
            {
                var blockHeight = 3 + group.Indices.Count; // heading + spacer + items + trailing spacer
                if (height0 <= height1)
                {
                    col0.Add(group);
                    height0 += blockHeight;
                }
                else
                {
                    col1.Add(group);
                    height1 += blockHeight;
                }
            }

            _wizardColumn0 = col0;
            _wizardColumn1 = col1;
        }

        // How fast the wizard's app list "types" itself out, in characters/second.
        private const int WizardTypeCharsPerSecond = 420;

        private readonly struct WizardLine
        {
            public readonly string Text;
            public readonly Font Font;
            public readonly Brush Brush;
            public WizardLine(string text, Font font, Brush brush) { Text = text; Font = font; Brush = brush; }
        }

        private void DrawWizard(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var catalog = InstallerEngine.Catalog;
            var contentX = fullX + 16;
            const int lineHeight = 18;

            // Dropped one extra line below the title bar so the heading doesn't sit
            // flush under it.
            var headingTop = fullY + titleHeight + 10 + lineHeight;
            g.DrawString("GamingStack will install the following:", _mono, Brushes.Gainsboro, contentX, headingTop);
            var listTop = headingTop + lineHeight + 8;

            var colWidth = (fullW - 32) / 2;

            BuildWizardColumnsIfNeeded();
            var columnGroups = new[] { _wizardColumn0!, _wizardColumn1! };

            // Per-frame line content (text/font/brush per row, per column) - unlike the
            // category grouping above (cached; the catalog never changes) this has to be
            // rebuilt every frame since the wizard's own selection state does. A `null`
            // entry is a blank spacer row (used after each category heading, so the
            // heading doesn't visually blend into the list under it).
            var colLines = new List<WizardLine?>[2];
            for (int col = 0; col < 2; col++)
            {
                var lines = new List<WizardLine?>();
                foreach (var (category, indices) in columnGroups[col])
                {
                    lines.Add(new WizardLine($"-- {category} --", _headingFont, Brushes.DeepSkyBlue));
                    lines.Add(null);

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

                        lines.Add(new WizardLine($"{marker} {catalog[i].FriendlyName}", _monoSmall, brush));
                    }

                    // Trailing blank line too, so a category's list doesn't run straight
                    // into the next category's heading - matches the spacer already
                    // added right after the heading above.
                    lines.Add(null);
                }
                colLines[col] = lines;
            }

            var maxRowsUsed = Math.Max(colLines[0].Count, colLines[1].Count);
            var totalChars = colLines[0].Concat(colLines[1]).Where(l => l != null).Sum(l => l!.Value.Text.Length);

            // Typewriter reveal: starts once the terminal window has finished opening,
            // and (since _stageWatch never resets while still in the Terminal stage)
            // naturally stays fully revealed forever afterwards, including across
            // WizardPhase changes - it only ever plays once.
            var typingElapsedMs = Math.Max(0, _stageWatch.ElapsedMilliseconds - TerminalOpenMs);
            var typedBudget = (int)(typingElapsedMs / 1000.0 * WizardTypeCharsPerSecond);
            var fullyTyped = typedBudget >= totalChars;
            var remaining = typedBudget;

            (float X, float Y, string Shown, Font Font)? cursor = null;

            for (int row = 0; row < maxRowsUsed; row++)
            {
                for (int col = 0; col < 2; col++)
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

            using var promptFont = new Font("Consolas", 14f, FontStyle.Bold);
            g.DrawString(prompt, promptFont, Brushes.Gold, contentX, promptTop);
        }

        private void DrawInstallerLog(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight, long elapsed)
        {
            string[] linesSnapshot;
            lock (_terminalLock) { linesSnapshot = _terminalLines.ToArray(); }

            const int lineHeight = 20;
            var contentTop = fullY + titleHeight + 10;
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

        // A little original mascot for the "didn't want to choose, didn't want to quit
        // either" branch - a wobbling floppy disk with a speech bubble. Fully original
        // shapes (rectangles/ellipses), not a reproduction of any real character.
        private void DrawEasterEgg(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight, long elapsedMs)
        {
            var contentTop = fullY + titleHeight;
            var contentHeight = fullH - titleHeight;
            var centerX = fullX + fullW / 2;
            var bob = (float)Math.Sin(elapsedMs / 250.0) * 6f;

            const int diskW = 90, diskH = 100;
            var diskX = centerX - diskW / 2;
            var diskY = contentTop + contentHeight / 2 - diskH / 2 + 30 + (int)bob;

            // body
            using var bodyBrush = new SolidBrush(Color.FromArgb(40, 40, 200));
            g.FillRectangle(bodyBrush, diskX, diskY, diskW, diskH);
            g.DrawRectangle(Pens.Gainsboro, diskX, diskY, diskW, diskH);

            // metal shutter
            using var shutterBrush = new SolidBrush(Color.FromArgb(200, 200, 210));
            g.FillRectangle(shutterBrush, diskX + 14, diskY, diskW - 28, 26);

            // label
            using var labelBrush = new SolidBrush(Color.FromArgb(235, 235, 245));
            g.FillRectangle(labelBrush, diskX + 10, diskY + 36, diskW - 20, 36);

            // face on the label
            using var eyePen = new Pen(Color.Black, 3);
            g.FillEllipse(Brushes.Black, diskX + 24, diskY + 46, 6, 6);
            g.FillEllipse(Brushes.Black, diskX + diskW - 30, diskY + 46, 6, 6);
            g.DrawArc(eyePen, diskX + 22, diskY + 52, diskW - 44, 16, 0, 180);

            // speech bubble
            using var bubbleFont = new Font("Consolas", 13f, FontStyle.Regular);
            const string joke = "Big decisions, huh?\nI'm just a floppy disk and\neven I can commit to 1.44MB.";
            var jokeSize = g.MeasureString(joke, bubbleFont);
            var bubbleW = jokeSize.Width + 30;
            var bubbleH = jokeSize.Height + 24;
            var bubbleX = centerX - bubbleW / 2;
            var bubbleY = diskY - bubbleH - 30;

            using var bubbleBrush = new SolidBrush(Color.White);
            g.FillRectangle(bubbleBrush, bubbleX, bubbleY, bubbleW, bubbleH);
            g.DrawRectangle(Pens.Black, bubbleX, bubbleY, bubbleW, bubbleH);

            // little pointer triangle from bubble down toward the disk
            var tipX = centerX;
            var tipY = bubbleY + bubbleH;
            var tri = new[]
            {
                new PointF(tipX - 10, tipY),
                new PointF(tipX + 10, tipY),
                new PointF(tipX, tipY + 14)
            };
            g.FillPolygon(Brushes.White, tri);
            g.DrawPolygon(Pens.Black, tri);

            g.DrawString(joke, bubbleFont, Brushes.Black, bubbleX + 15, bubbleY + 12);
        }

        // The sign-off screen, styled like an old text-mode "installation complete"
        // box - reached either after a real install finishes or after the easter egg.
        private void DrawFarewell(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            using var textFont = new Font("Consolas", 13f, FontStyle.Regular);
            using var signFont = new Font("Consolas", 13f, FontStyle.Bold);

            var lines = new[]
            {
                ("Thank you for using my Installer stack on your", textFont, Brushes.LimeGreen),
                ("Shiny PC, I hope you have lots of fun playing.", textFont, Brushes.LimeGreen),
                ("", textFont, Brushes.LimeGreen),
                ("Yours, Morpheus2n2", signFont, Brushes.Gold)
            };

            const int lineHeight = 24;
            var maxTextWidth = lines.Max(l => g.MeasureString(l.Item1, l.Item2).Width);

            const int glyphAreaHeight = 60;
            var boxWidth = Math.Min(fullW - 60, (int)maxTextWidth + 60);
            var boxHeight = lines.Length * lineHeight + glyphAreaHeight + 30;

            var boxX = fullX + (fullW - boxWidth) / 2;
            var boxY = fullY + titleHeight + Math.Max(10, (fullH - titleHeight - boxHeight) / 2);

            using var borderPen = new Pen(Color.Cyan, 2);
            g.DrawRectangle(borderPen, boxX, boxY, boxWidth, boxHeight);
            // small corner accents, ASCII-box-drawing style
            const int accent = 16;
            g.DrawLine(borderPen, boxX, boxY, boxX + accent, boxY);
            g.DrawLine(borderPen, boxX, boxY, boxX, boxY + accent);
            g.DrawLine(borderPen, boxX + boxWidth, boxY, boxX + boxWidth - accent, boxY);
            g.DrawLine(borderPen, boxX + boxWidth, boxY, boxX + boxWidth, boxY + accent);
            g.DrawLine(borderPen, boxX, boxY + boxHeight, boxX + accent, boxY + boxHeight);
            g.DrawLine(borderPen, boxX, boxY + boxHeight, boxX, boxY + boxHeight - accent);
            g.DrawLine(borderPen, boxX + boxWidth, boxY + boxHeight, boxX + boxWidth - accent, boxY + boxHeight);
            g.DrawLine(borderPen, boxX + boxWidth, boxY + boxHeight, boxX + boxWidth, boxY + boxHeight - accent);

            DrawPixelHeart(g, boxX + boxWidth / 2 - 12, boxY + 16);

            var ty = boxY + glyphAreaHeight;
            foreach (var (text, font, brush) in lines)
            {
                if (text.Length > 0)
                {
                    var w = g.MeasureString(text, font).Width;
                    g.DrawString(text, font, brush, boxX + (boxWidth - w) / 2, ty);
                }
                ty += lineHeight;
            }
        }

        // A small original pixel-art heart - "have fun playing" needed a friendly
        // little glyph and this is generic/geometric enough to carry no resemblance
        // to any specific character or brand.
        private static void DrawPixelHeart(Graphics g, int x, int y)
        {
            using var brush = new SolidBrush(Color.FromArgb(255, 90, 140));
            const int p = 4; // pixel size
            int[,] shape =
            {
                {0,1,1,0,1,1,0},
                {1,1,1,1,1,1,1},
                {1,1,1,1,1,1,1},
                {0,1,1,1,1,1,0},
                {0,0,1,1,1,0,0},
                {0,0,0,1,0,0,0}
            };
            for (int row = 0; row < shape.GetLength(0); row++)
                for (int col = 0; col < shape.GetLength(1); col++)
                    if (shape[row, col] == 1)
                        g.FillRectangle(brush, x + col * p, y + row * p, p, p);
        }

        // ---------- misc ----------

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (_stage == Stage.Bios && e.KeyCode == Keys.Delete)
            {
                _stage = Stage.Bsod;
                _stageWatch.Restart();
                return;
            }

            if (_stage == Stage.Terminal) HandleWizardKey(e.KeyCode);

            if (e.KeyCode == Keys.Escape) Close();
        }

        // ---- terminal window drag/resize ----
        // The "window" is just a rectangle we draw, not a real child window, so moving
        // and resizing it is hand-rolled: hit-test the title bar / grip corner on mouse
        // down, then just mutate _termX/Y/W/H on move. Only active during the Terminal
        // stage, and only once the open animation has actually finished positioning it
        // (_termX etc. are seeded the first time PaintTerminal runs).

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_stage != Stage.Terminal || _termX == null) return;

            var fullX = _termX!.Value;
            var fullY = _termY!.Value;
            var fullW = _termW!.Value;
            var fullH = _termH!.Value;

            var gripRect = new Rectangle(fullX + fullW - TermResizeGrip, fullY + fullH - TermResizeGrip, TermResizeGrip, TermResizeGrip);
            var titleRect = new Rectangle(fullX, fullY, fullW, _titleBarHeight);

            if (gripRect.Contains(e.Location))
            {
                _resizingTerm = true;
                _dragMouseStart = e.Location;
                _dragTermStart = new Rectangle(fullX, fullY, fullW, fullH);
            }
            else if (titleRect.Contains(e.Location))
            {
                _draggingTerm = true;
                _dragMouseStart = e.Location;
                _dragTermStart = new Rectangle(fullX, fullY, fullW, fullH);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_draggingTerm)
            {
                var dx = e.X - _dragMouseStart.X;
                var dy = e.Y - _dragMouseStart.Y;
                _termX = Math.Clamp(_dragTermStart.X + dx, 0, Math.Max(0, Width - _termW!.Value));
                _termY = Math.Clamp(_dragTermStart.Y + dy, 0, Math.Max(0, Height - _termH!.Value));
            }
            else if (_resizingTerm)
            {
                var dx = e.X - _dragMouseStart.X;
                var dy = e.Y - _dragMouseStart.Y;
                _termW = Math.Max(TermMinWidth, Math.Min(Width - _termX!.Value, _dragTermStart.Width + dx));
                _termH = Math.Max(TermMinHeight, Math.Min(Height - _termY!.Value, _dragTermStart.Height + dy));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _draggingTerm = false;
            _resizingTerm = false;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _soundPlayer?.Dispose();
            _wallpaper?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
