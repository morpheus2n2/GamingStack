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
    /// (real installer, via InstallerEngine).
    /// Pressing Delete during the Bios stage detours into a joke Bsod stage for a few
    /// seconds, then carries on into Boot as normal.
    /// </summary>
    public class MainForm : Form
    {
        private enum Stage { Bios, Bsod, Boot, Desktop, Terminal }

        private const int BiosMinHoldMs = 13000;
        private const int BsodHoldMs = 7000;
        private const int BootHoldMs = 10000;
        private const int SoundTriggerMs = 8000;
        private const int MemCountDurationMs = 3000;
        private const int DesktopHoldMs = 7000;
        private const int TerminalOpenMs = 400;
        private const double CloudSpeedMultiplier = 1.8;

        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
        private readonly Stopwatch _stageWatch = new();

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
        private readonly Font _monoLarge = new("Consolas", 22f, FontStyle.Bold);

        // ---- installer ----
        private readonly InstallerEngine _installer = new();
        private readonly List<string> _terminalLines = new();
        private readonly object _terminalLock = new();
        private bool _installStarted;

        public MainForm()
        {
            Text = "GamingStack";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.Black;
            DoubleBuffered = true;

            _installer.OnLog += line =>
            {
                lock (_terminalLock)
                {
                    _terminalLines.Add(line);
                    if (_terminalLines.Count > 300) _terminalLines.RemoveAt(0);
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

            // Maximized+borderless can size itself to the work area (or misbehave on
            // scaled/multi-monitor setups) instead of the real screen bounds, which is
            // what was clipping the taskbar off the edge. Set explicit bounds to the
            // actual physical screen instead of relying on WindowState.Maximized.
            var screen = Screen.FromControl(this);
            StartPosition = FormStartPosition.Manual;
            WindowState = FormWindowState.Normal;
            Bounds = screen.Bounds;

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

            _footerAt = diskCursor + 400;
            Add(_footerAt, "Press DEL to enter BIOS Setup...", Color.Goldenrod);
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
                    if (!_installStarted && _stageWatch.ElapsedMilliseconds >= TerminalOpenMs)
                    {
                        _installStarted = true;
                        _ = RunInstallerSafelyAsync();
                    }
                    break;
            }
        }

        private async Task RunInstallerSafelyAsync()
        {
            try
            {
                await _installer.RunAsync();
            }
            catch (Exception ex)
            {
                lock (_terminalLock) { _terminalLines.Add($"FATAL: {ex.Message}"); }
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
            using var sky = new LinearGradientBrush(
                new Rectangle(0, 0, Width, Height),
                Color.FromArgb(20, 10, 40),
                Color.FromArgb(70, 15, 90),
                LinearGradientMode.Vertical);
            g.FillRectangle(sky, 0, 0, Width, Height);
        }

        private void DrawClouds(Graphics g, double t)
        {
            DrawCloud(g, 90, 60, 18 * CloudSpeedMultiplier, t);
            DrawCloud(g, 220, 40, 26 * CloudSpeedMultiplier, t);
            DrawCloud(g, 400, 90, 34 * CloudSpeedMultiplier, t);
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
                float wave = (float)(Math.Sin((localX * 0.06) + t * 3.0) * 18.0);
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

            var pct = Math.Min(1.0, elapsed / (double)BootHoldMs);
            const int barWidth = 500;
            var barX = (Width - barWidth) / 2;
            var barY = Height - 110;
            g.DrawRectangle(Pens.Gainsboro, barX, barY, barWidth, 24);
            g.FillRectangle(Brushes.Magenta, barX + 2, barY + 2, (int)((barWidth - 4) * pct), 20);
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

        private void DrawTaskbar(Graphics g)
        {
            const int barHeight = 36;
            using var barBrush = new SolidBrush(Color.FromArgb(192, 192, 192));
            g.FillRectangle(barBrush, 0, Height - barHeight, Width, barHeight);

            using var btnBrush = new SolidBrush(Color.FromArgb(220, 220, 220));
            var startRect = new Rectangle(6, Height - barHeight + 4, 80, barHeight - 8);
            g.FillRectangle(btnBrush, startRect);
            g.DrawRectangle(Pens.Gray, startRect);
            g.DrawString("Start", _mono, Brushes.Black, startRect.X + 14, startRect.Y + 4);

            var clock = DateTime.Now.ToString("HH:mm");
            var clockSize = g.MeasureString(clock, _mono);
            g.DrawString(clock, _mono, Brushes.Black, Width - clockSize.Width - 20, Height - barHeight + 8);
        }

        // ---- Terminal (opens on top of the desktop, runs the real installer) ----

        private void PaintTerminal(Graphics g)
        {
            DrawWallpaper(g);
            DrawDesktopIcons(g);
            DrawTaskbar(g);

            var elapsed = _stageWatch.ElapsedMilliseconds;
            var openProgress = Math.Clamp(elapsed / (double)TerminalOpenMs, 0.0, 1.0);
            var eased = 1 - Math.Pow(1 - openProgress, 3);

            var fullW = (int)(Width * 0.7);
            var fullH = (int)(Height * 0.6);
            var fullX = (Width - fullW) / 2;
            var fullY = (Height - fullH) / 2;

            var w = (int)(fullW * eased);
            var h = (int)(fullH * eased);
            if (w <= 0 || h <= 0) return;
            var x = fullX + (fullW - w) / 2;
            var y = fullY + (fullH - h) / 2;

            g.FillRectangle(Brushes.Black, x, y, w, h);
            g.DrawRectangle(Pens.Gainsboro, x, y, w, h);

            if (eased < 0.98) return;

            const int titleHeight = 26;
            using var titleBrush = new SolidBrush(Color.FromArgb(30, 30, 30));
            g.FillRectangle(titleBrush, fullX, fullY, fullW, titleHeight);
            g.DrawString("GamingStack Installer", _mono, Brushes.Gainsboro, fullX + 8, fullY + 4);

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
                var cursorX = fullX + 16;
                var cursorY = ty - lineHeight;
                if (visible.Length > 0)
                    cursorX += g.MeasureString(visible[^1], _mono).Width;
                else
                    cursorY = ty;

                g.FillRectangle(Brushes.LimeGreen, cursorX, cursorY, 10, 16);
            }
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

            if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _soundPlayer?.Dispose();
            _wallpaper?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
