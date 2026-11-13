using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Management;
using System.Media;
using System.Reflection;

namespace GamingStackGUI
{
    // The intro: embedded sounds/wallpaper, the fake BIOS (real hardware info), BSOD joke, boot screen.
    public partial class MainForm
    {
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

        // Typewriter sound (see RetroAudio.cs). Created on first use.
        private RetroAudio? _audio;
        private bool _typewriterBellRung;
        private readonly Random _typeRng = new();

        // One key clack per ~2 new letters (at most 4 per frame), each given a random
        // offset inside the ~33 ms frame; roughly one in seven is the deeper space-bar sound.
        private void PlayTypewriterKeys(int newChars)
        {
            try
            {
                _audio ??= new RetroAudio();
                var strikes = Math.Min(4, (newChars + 1) / 2);
                for (int i = 0; i < strikes; i++)
                    _audio.Key(_typeRng.Next(0, 30), space: _typeRng.Next(7) == 0);
            }
            catch { /* sound is optional - never let it break the wizard */ }
        }

        // The wallpaper image is only 1536x1024, so on a big screen it gets enlarged a
        // lot. The default GDI+ filter does that with cheap bilinear sampling, which
        // looks blurry. Instead it is enlarged ONCE, here, with the high-quality bicubic
        // filter ("cover" fit, cropping overflow so it never stretches), then given a
        // light sharpen to win back edge crispness. After that every frame is just a 1:1
        // copy, which is also much cheaper. It can't invent detail that isn't in the
        // image - a native-resolution wallpaper would still look best.
        private void LoadWallpaper()
        {
            try
            {
                using var stream = FindEmbeddedResource("wallpaper.jpg");
                if (stream == null) return;
                using var source = Image.FromStream(stream);

                var bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    var scale = Math.Max(Width / (float)source.Width, Height / (float)source.Height);
                    var w = source.Width * scale;
                    var h = source.Height * scale;
                    g.DrawImage(source, (Width - w) / 2f, (Height - h) / 2f, w, h);
                }
                Sharpen(bmp, 0.45);
                _wallpaper = bmp;
            }
            catch
            {
                // Non-fatal - DrawWallpaper falls back to a plain colour.
            }
        }

        // 3x3 sharpen (centre-weighted, 4-neighbour). amount ~0.3-0.6 is subtle.
        private static void Sharpen(Bitmap bmp, double amount)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int w = data.Width, h = data.Height, stride = data.Stride;
                var src = new byte[stride * h];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, src, 0, src.Length);
                var dst = (byte[])src.Clone();
                var centre = 1 + 4 * amount;
                for (int y = 1; y < h - 1; y++)
                {
                    for (int x = 1; x < w - 1; x++)
                    {
                        var i = y * stride + x * 4;
                        for (int c = 0; c < 3; c++)
                        {
                            var v = src[i + c] * centre
                                    - amount * (src[i + c - 4] + src[i + c + 4] + src[i + c - stride] + src[i + c + stride]);
                            dst[i + c] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
                        }
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(dst, 0, data.Scan0, dst.Length);
            }
            finally { bmp.UnlockBits(data); }
        }

        private long _lastSeekAt = 700;

        // Plays one BIOS sound through the mixer; sound is optional, so never throw.
        private void PlayBios(Action<RetroAudio> play)
        {
            try { _audio ??= new RetroAudio(); play(_audio); }
            catch { /* no sound device - non-fatal */ }
        }


        // ---------- fake BIOS timeline (real hardware) ----------

        private void BuildBiosTimeline()
        {
            _timedLines.Clear();

            void Add(long at, string text, Color color) =>
                _timedLines.Add(new TimedLine { At = at, Text = text, Color = color });

            Add(0, "American Megatrends Inc.", Color.Gainsboro);
            Add(150, "GAMINGSTACK-UEFI BIOS v2.1.0", Color.Gray);

            var board = GetMotherboard();
            Add(700, $"Mainboard: {board}", Color.Gainsboro);

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

            // Read-only reporting, not a tweak - no consent screen needed, same as
            // the disk list above it. Sits here rather than on ConfirmTweaks since
            // nothing changes; it's just telling you what's already true.
            var trim = GetTrimStatus();
            Add(diskCursor, $"  TRIM (delete notify): {trim}", trim == "enabled" ? Color.LightGreen : Color.Gold);
            diskCursor += 300;

            // Its own heading (matching the Graphics Adapter/Storage Devices pattern
            // above) rather than tacking onto the disk list - it was reading as just
            // another storage entry before. Disguised as an ordinary peripheral
            // detection line either way - this is the Konami code's only hint
            // anywhere in the app, hidden in plain sight during POST.
            Add(diskCursor, "Input Devices:", Color.Gainsboro);
            Add(diskCursor + 300, "  Standard 104-Key Keyboard (↑↑↓↓←→←→BA) - OK", Color.LightGreen);
            diskCursor += 600;

            // Not added to the timed reveal - the "Press DEL" prompt is drawn separately
            // in PaintBios, pinned to the bottom of the screen and visible from frame
            // one. _footerAt is still used to time the footer beep below.
            _footerAt = diskCursor + 400;
        }

        private static string GetMotherboard()
        {
            var manufacturer = GetWmiString("Win32_BaseBoard", "Manufacturer");
            var product = GetWmiString("Win32_BaseBoard", "Product");

            if (manufacturer == "Unknown" && product == "Unknown") return "Unknown";
            if (manufacturer == "Unknown") return product;
            if (product == "Unknown") return manufacturer;

            // Some boards report the manufacturer name as part of Product already
            // (e.g. "ASUSTeK COMPUTER INC." + "ROG STRIX Z790-E") - avoid a stutter
            // like "ASUSTeK COMPUTER INC. ASUSTeK COMPUTER INC. ROG STRIX Z790-E".
            return product.Contains(manufacturer, StringComparison.OrdinalIgnoreCase)
                ? product
                : $"{manufacturer} {product}";
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
                // Status is real WMI health data (usually "OK"; a failing drive
                // reports something else, e.g. "Pred Fail") rather than a hardcoded
                // "OK" - this is the read-only storage health half of the NVMe
                // TRIM/storage health check idea from ROADMAP.md.
                using var searcher = new ManagementObjectSearcher("SELECT Model, Size, Status FROM Win32_DiskDrive");
                foreach (var obj in searcher.Get())
                {
                    var model = obj["Model"]?.ToString() ?? "Unknown Disk";
                    var sizeGb = obj["Size"] != null
                        ? (long)Math.Round(Convert.ToInt64(obj["Size"]) / 1024.0 / 1024.0 / 1024.0)
                        : 0;
                    var status = obj["Status"]?.ToString();
                    results.Add($"{model} - {sizeGb}GB - {(string.IsNullOrWhiteSpace(status) ? "OK" : status)}");
                }
            }
            catch
            {
                // Ignored - falls through to the fallback line below.
            }
            if (results.Count == 0) results.Add("Unknown Disk - OK");
            return results;
        }

        /// <summary>
        /// Whether NTFS delete notify (TRIM) is enabled system-wide - read via
        /// `fsutil behavior query DisableDeleteNotify`, the standard, documented way
        /// to check this (there's no WMI class for it). This is read-only reporting,
        /// not a tweak: nothing here changes anything, so it has no consent screen
        /// and doesn't go through InstallerEngine's tweak pattern.
        /// </summary>
        private static string GetTrimStatus()
        {
            try
            {
                var psi = new ProcessStartInfo("fsutil", "behavior query DisableDeleteNotify")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return "unknown";
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);

                var match = System.Text.RegularExpressions.Regex.Match(output, @"DisableDeleteNotify\s*=\s*(\d)");
                if (!match.Success) return "unknown";
                return match.Groups[1].Value == "0" ? "enabled" : "disabled";
            }
            catch
            {
                return "unknown";
            }
        }

        // ---- BIOS ----

        private BiosLogo? _biosLogo;
        private TextureBrush? _scanlineBrush;

        // Whole-number art scale so the pixel-art logo stays crisp: 3 at 1080p, 4 at 1440p+.
        private int BiosArtScale => Math.Max(2, VH / 360);

        // Fine dark lines across the whole screen, like a CRT - subtle enough that the
        // text stays comfortably readable.
        private void DrawScanlines(Graphics g)
        {
            if (_scanlineBrush == null)
            {
                var tile = new Bitmap(1, 3);
                tile.SetPixel(0, 0, Color.FromArgb(0, 0, 0, 0));
                tile.SetPixel(0, 1, Color.FromArgb(0, 0, 0, 0));
                tile.SetPixel(0, 2, Color.FromArgb(46, 0, 0, 0));
                _scanlineBrush = new TextureBrush(tile);
            }
            g.FillRectangle(_scanlineBrush, 0, 0, Width, Height);
        }

        private void PaintBios(Graphics g)
        {
            var elapsed = _stageWatch.ElapsedMilliseconds;
            _biosLogo ??= new BiosLogo();
            var art = BiosArtScale;
            var t = elapsed / 1000.0;

            // Double-line frame, like the boxed-in text screens of 90s firmware.
            using (var framePen = new Pen(Color.FromArgb(70, 90, 140), 2))
            {
                g.DrawRectangle(framePen, 14, 14, VW - 29, VH - 29);
                g.DrawRectangle(Pens.DimGray, 20, 20, VW - 41, VH - 41);
            }

            _biosLogo.DrawLogo(g, 50, 36, art, t);
            _biosLogo.DrawBadge(g, VW - 50 - BiosLogo.BadgeW * art, 36, art, t);

            int x = 60, y = 36 + BiosLogo.LogoH * art + 22;

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
            using var delFont = Ui.Font(15f, FontStyle.Bold);
            const string delPrompt = "Press DEL to enter BIOS Setup... Go on, I dare you!";
            var delSize = g.MeasureString(delPrompt, delFont);
            g.DrawString(delPrompt, delFont, Brushes.Goldenrod, (VW - delSize.Width) / 2, VH - delSize.Height - 30);

            // The classic BIOS ID string along the bottom (made up, in the usual style).
            const string idLine = "11/13/2026-GS-GAMER-X1-1.1.0-00";
            var idSize = g.MeasureString(idLine, _monoSmall);
            g.DrawString(idLine, _monoSmall, Brushes.DimGray, 40, VH - idSize.Height - 36);
            DrawSkipHint(g, 60);
        }

        // ---- Easter egg: press Delete during Bios ----

        private void PaintBsod(Graphics g)
        {
            g.Clear(Color.FromArgb(0, 0, 170));

            using var bigFont = Ui.Font(30f, FontStyle.Bold);
            using var smallFont = Ui.Font(15f, FontStyle.Regular);

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

        // ---- Boot: the artwork lives in BootScene.cs ----

        private void PaintBoot(Graphics g)
        {
            var elapsed = _stageWatch.ElapsedMilliseconds;
            var t = elapsed / 1000.0;

            // The gradient progress bar along the bottom is part of the scene itself.
            var fade = Math.Min(1.0, elapsed / 700.0);
            var pct = Math.Min(1.0, elapsed / (double)BootHoldMs);
            _bootScene ??= new BootScene(Width, Height, BootTheme.ForNow(), BootScene.ClassicByDefault);
            _bootScene.Draw(g, Width, Height, t, fade, pct);

            var saved = g.Save();
            g.ScaleTransform((float)UiScale, (float)UiScale);

            // The classic layout carries its own wordmark; the landscape one gets a title line.
            if (!_bootScene.Classic)
            {
                const string title = "Starting GamingStack 95...";
                var titleSize = g.MeasureString(title, _monoLarge);
                var titleX = (VW - titleSize.Width) / 2;
                var titleY = VH - 130;
                // soft shadow so the text stays readable over the dark hills
                g.DrawString(title, _monoLarge, Brushes.Black, titleX + 2, titleY + 2);
                g.DrawString(title, _monoLarge, Brushes.White, titleX, titleY);
            }
            DrawSkipHint(g, 44);
            g.Restore(saved);
        }
        // ---- Desktop / shared wallpaper ----

        private void DrawWallpaper(Graphics g)
        {
            // The wallpaper was scaled once, at exactly the screen's size, when the app started
            // (see LoadWallpaper), so drawing it is a straight 1:1 copy every frame - sharp and cheap.
            if (_wallpaper != null && _wallpaper.Width == Width && _wallpaper.Height == Height)
            {
                g.DrawImageUnscaled(_wallpaper, 0, 0);
                return;
            }

            // Only reached if the scene somehow failed to build.
            using var bg = new SolidBrush(Color.FromArgb(40, 100, 200));
            g.FillRectangle(bg, 0, 0, Width, Height);
        }

        private void SkipIntro()
        {
            try { _soundPlayer?.Stop(); } catch { /* non-fatal */ }
            EnterTerminalStage();
        }

        private void DrawSkipHint(Graphics g, int bottomMargin, string hint = "Press SPACE to skip the intro")
        {
            var size = g.MeasureString(hint, _monoSmall);
            g.DrawString(hint, _monoSmall, Brushes.Black, VW - size.Width - 38, VH - bottomMargin - size.Height + 1);
            g.DrawString(hint, _monoSmall, Brushes.Gainsboro, VW - size.Width - 40, VH - bottomMargin - size.Height);
        }
    }
}
