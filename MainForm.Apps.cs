using System.Management;

namespace GamingStackGUI
{
    /// <summary>
    /// The little apps that live on the fake desktop: the installer itself, My Computer,
    /// the Trophy Room, the Arcade (Snake) and the Recycle Bin.
    /// </summary>
    public partial class MainForm
    {
        // ------------------------------------------------------------ installer

        // Moves from the desktop to the installer stage (idempotent) and opens its window.
        private void EnterTerminalStage()
        {
            if (_stage != Stage.Terminal)
            {
                _stage = Stage.Terminal;
                _stageWatch.Restart();
            }
            OpenInstaller();
        }

        private void OpenInstaller()
        {
            var existing = _windows.FirstOrDefault(w => w.Id == "installer");
            if (existing != null) { FocusWindow(existing); return; }

            _icons ??= new DesktopIcons();
            var areaH = VH - TaskbarH;
            var bw = (int)(VW * 0.78);
            var bh = (int)(areaH * 0.88);
            OpenWindow(new RetroWindow
            {
                Id = "installer",
                Title = "GamingStack Installer",
                Icon = _icons.Developer,
                Bounds = new Rectangle((VW - bw) / 2, (areaH - bh) / 2, bw, bh),
                MinSize = new Size(480, 280),
                Background = Color.Black,
                OpenAnimMs = TerminalOpenMs,
                DrawClient = DrawInstallerClient,
                OnKey = k =>
                {
                    if (k == Keys.Escape) return false;          // Esc still leaves the app
                    if (_stage == Stage.Terminal) HandleWizardKey(k);
                    return true;
                },
                CanClose = () => !IsBusyPhase,
            });
        }

        // ------------------------------------------------------------ helpers

        private readonly HashSet<string> _openedApps = new();

        // "Desktop Explorer" trophy: open every desktop app.
        private void NoteAppOpened(string id)
        {
            if (id == "installer") return;
            _openedApps.Add(id);
            if (_openedApps.IsSupersetOf(new[] { "mycomputer", "trophies", "arcade", "recycle", "notes", "paint", "cmd", "defrag", "display" }))
                UnlockAchievement("explorer");
        }

        // New windows cascade down and right so they never open exactly on top of each other.
        private Rectangle Cascade(int w, int h)
        {
            var n = _windows.Count % 6;
            var x = Math.Min(Math.Max(0, VW - w), 180 + n * 34);
            var y = Math.Min(Math.Max(0, VH - TaskbarH - h), 60 + n * 34);
            return new Rectangle(x, y, Math.Min(w, VW), Math.Min(h, VH - TaskbarH));
        }

        // ------------------------------------------------------------ My Computer

        private List<(string Label, string Value)>? _sysInfo;
        private List<(string Name, long Total, long Free)>? _volumes;
        private bool _sysInfoLoading;

        private void OpenMyComputer()
        {
            if (_sysInfo == null && !_sysInfoLoading)
            {
                _sysInfoLoading = true;
                Task.Run(() =>
                {
                    try { _volumes = ReadVolumes(); _sysInfo = ReadSystemInfo(); }
                    finally { _sysInfoLoading = false; }
                });
            }
            _icons ??= new DesktopIcons();
            OpenWindow(new RetroWindow
            {
                Id = "mycomputer",
                Title = "My Computer",
                Icon = _icons.Computer,
                Bounds = Cascade(820, 600),
                MinSize = new Size(480, 340),
                Background = Color.FromArgb(192, 192, 192),
                DrawClient = DrawMyComputer,
                OnKey = k => { if (k != Keys.Escape) return false; CloseById("mycomputer"); return true; },
            });
        }

        private void CloseById(string id)
        {
            var w = _windows.FirstOrDefault(x => x.Id == id);
            if (w != null) CloseWindow(w);
        }

        private string Info(string label) => _sysInfo?.FirstOrDefault(x => x.Label == label).Value ?? "";

        // A control-panel style page: a banner with a big computer icon, then group boxes that
        // flow into one or two columns. Text and spacing scale with the window, so maximising
        // it makes everything bigger rather than leaving it small in a big window.
        private void DrawMyComputer(Graphics g, Rectangle c)
        {
            var k = Math.Clamp(Math.Min(c.Width / 780.0, c.Height / 560.0), 1.0, 2.2);
            using var titleFont = Ui.Font((float)(17 * k), FontStyle.Bold);
            using var subFont = Ui.Font((float)(10.5 * k));
            using var headFont = Ui.Font((float)(10.5 * k), FontStyle.Bold);
            using var labelFont = Ui.Font((float)(10 * k));
            using var valueFont = Ui.Font((float)(10.5 * k));

            // banner
            var bandH = (int)(96 * k);
            var band = new Rectangle(c.X, c.Y, c.Width, bandH);
            using (var grad = new System.Drawing.Drawing2D.LinearGradientBrush(band, Color.FromArgb(10, 36, 106), Color.FromArgb(70, 150, 230),
                       System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                g.FillRectangle(grad, band);
            _icons ??= new DesktopIcons();
            var iconScale = Math.Max(3, (int)Math.Round(4 * k));
            var isize = DesktopIcons.ArtSize * iconScale;
            DesktopIcons.Draw(g, _icons.Computer, c.X + 18, c.Y + (bandH - isize) / 2, iconScale);
            var tx = c.X + 18 + isize + 20;
            g.DrawString(Environment.MachineName, titleFont, Brushes.White, tx, c.Y + bandH * 0.22f);
            var os = Info("Windows");
            g.DrawString(string.IsNullOrEmpty(os) ? "Reading your hardware..." : os, subFont, Brushes.Gainsboro, tx, c.Y + bandH * 0.22f + titleFont.Height + 2);
            if (_sysInfo == null) return;

            // group boxes
            var pad = (int)(14 * k);
            var cols = c.Width >= 600 ? 2 : 1;
            var colW = (c.Width - pad * (cols + 1)) / cols;
            var colY = new int[cols];
            for (int i = 0; i < cols; i++) colY[i] = c.Y + bandH + pad;
            var lineH = (int)(valueFont.Height * 1.25);

            void Group(string title, Func<Rectangle, int> draw, int contentH)
            {
                var col = colY[0] <= colY[cols - 1] ? 0 : cols - 1;
                if (cols == 2) col = colY[0] <= colY[1] ? 0 : 1;
                var x = c.X + pad + col * (colW + pad);
                var h = contentH + headFont.Height + (int)(18 * k);
                if (colY[col] + h > c.Bottom) return;                    // never draw half a box off the bottom
                var box = new Rectangle(x, colY[col], colW, h);
                var etched = new Rectangle(box.X, box.Y + headFont.Height / 2, box.Width, box.Height - headFont.Height / 2);
                g.DrawRectangle(Pens.White, etched.X + 1, etched.Y + 1, etched.Width - 1, etched.Height - 1);
                g.DrawRectangle(Pens.DimGray, etched);
                var ts = g.MeasureString(title, headFont);
                using (var face = new SolidBrush(Color.FromArgb(192, 192, 192))) g.FillRectangle(face, box.X + 10, box.Y, ts.Width + 8, headFont.Height);
                g.DrawString(title, headFont, Brushes.Black, box.X + 14, box.Y - 1);
                draw(new Rectangle(box.X + 14, box.Y + headFont.Height + (int)(6 * k), box.Width - 28, contentH));
                colY[col] += h + pad;
            }

            // helper for simple "label  value" sections, wrapping long values
            void Rows(string title, params (string Label, string Value)[] rows)
            {
                rows = rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).ToArray();
                if (rows.Length == 0) return;
                var labelW = rows.Max(r => r.Label.Length == 0 ? 0 : (int)g.MeasureString(r.Label, labelFont).Width + 12);
                var wrapped = rows.Select(r => WrapTextToWidth(g, r.Value, valueFont, colW - 28 - labelW)).ToList();
                var lines = wrapped.Sum(w => Math.Max(1, w.Count));
                Group(title, area =>
                {
                    var y = area.Y;
                    for (int i = 0; i < rows.Length; i++)
                    {
                        if (rows[i].Label.Length > 0) g.DrawString(rows[i].Label, labelFont, Brushes.DimGray, area.X, y + 1);
                        foreach (var ln in wrapped[i]) { g.DrawString(ln, valueFont, Brushes.Black, area.X + labelW, y); y += lineH; }
                    }
                    return y;
                }, lines * lineH);
            }

            var coresThreads = Info("Cores / threads");
            Rows("Processor", ("", Info("Processor")), ("Cores / threads", coresThreads));
            Rows("Memory", ("Installed", Info("Memory")));
            Rows("Graphics", ("", Info("Graphics")), ("Driver", Info("Graphics driver")));
            Rows("Motherboard", ("", Info("Motherboard")));

            // storage: one line + a usage bar per volume
            var vols = _volumes ?? new();
            if (vols.Count > 0)
            {
                var rowH = lineH + (int)(16 * k);
                Group("Storage", area =>
                {
                    var y = area.Y;
                    foreach (var (name, total, free) in vols)
                    {
                        var used = total - free;
                        var txt = $"{name}   {FormatBytes(free)} free of {FormatBytes(total)}";
                        g.DrawString(txt, valueFont, Brushes.Black, area.X, y);
                        var bar = new Rectangle(area.X, y + lineH, area.Width, (int)(11 * k));
                        Raised(g, bar, true, Color.White);
                        var frac = total <= 0 ? 0 : used / (double)total;
                        var fill = frac > 0.9 ? Color.FromArgb(220, 50, 50) : frac > 0.75 ? Color.FromArgb(230, 170, 30) : Color.FromArgb(40, 120, 220);
                        using var fb = new SolidBrush(fill);
                        g.FillRectangle(fb, bar.X + 2, bar.Y + 2, (int)((bar.Width - 4) * frac), bar.Height - 4);
                        y += rowH;
                    }
                    return y;
                }, vols.Count * rowH);
            }

            var disks = _sysInfo.Where(x => x.Label == "Drive").Select(x => ("", x.Value)).ToArray();
            if (disks.Length > 0) Rows("Disks", disks);
            Rows("This session", ("Up for", Info("Up for")), ("Runtime", Info("Runtime")), ("GamingStack", Info("GamingStack")));
        }

        private static string FormatBytes(long b) =>
            b >= 1L << 40 ? $"{b / (double)(1L << 40):0.#} TB" : $"{b / (double)(1L << 30):0} GB";

        private static List<(string, long, long)> ReadVolumes()
        {
            var list = new List<(string, long, long)>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel;
                    list.Add(($"{d.Name.TrimEnd('\\')} ({label})", d.TotalSize, d.AvailableFreeSpace));
                }
            }
            catch { /* a drive can vanish mid-read - skip it */ }
            return list;
        }

        private static List<(string, string)> ReadSystemInfo()
        {
            var list = new List<(string, string)>();
            string First(string query, string prop)
            {
                try
                {
                    using var s = new ManagementObjectSearcher(query);
                    foreach (var o in s.Get()) return o[prop]?.ToString()?.Trim() ?? "";
                }
                catch { /* WMI can fail on locked-down machines */ }
                return "";
            }
            void Add(string label, string value) { if (!string.IsNullOrWhiteSpace(value)) list.Add((label, value)); }

            Add("Computer", Environment.MachineName);
            Add("Windows", First("SELECT Caption FROM Win32_OperatingSystem", "Caption") + "  (build " +
                           First("SELECT BuildNumber FROM Win32_OperatingSystem", "BuildNumber") + ")");
            Add("Processor", First("SELECT Name FROM Win32_Processor", "Name"));
            var cores = First("SELECT NumberOfCores FROM Win32_Processor", "NumberOfCores");
            var threads = First("SELECT NumberOfLogicalProcessors FROM Win32_Processor", "NumberOfLogicalProcessors");
            if (cores != "") Add("Cores / threads", $"{cores} / {threads}");
            if (long.TryParse(First("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", "TotalPhysicalMemory"), out var ram))
                Add("Memory", $"{Math.Round(ram / 1024.0 / 1024 / 1024)} GB");
            Add("Graphics", First("SELECT Name FROM Win32_VideoController", "Name"));
            Add("Graphics driver", First("SELECT DriverVersion FROM Win32_VideoController", "DriverVersion"));
            Add("Motherboard", (First("SELECT Manufacturer FROM Win32_BaseBoard", "Manufacturer") + " " +
                                First("SELECT Product FROM Win32_BaseBoard", "Product")).Trim());
            foreach (var d in GetDisks()) Add("Drive", d);
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            Add("Up for", $"{(int)up.TotalDays}d {up.Hours}h {up.Minutes}m");
            Add("Runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Add("GamingStack", typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "");
            return list;
        }

        // ------------------------------------------------------------ Trophies

        private void OpenTrophies()
        {
            _icons ??= new DesktopIcons();
            OpenWindow(new RetroWindow
            {
                Id = "trophies",
                Title = "Trophy Room",
                Icon = _icons.Trophy,
                Bounds = Cascade(1060, 780),
                MinSize = new Size(480, 360),
                Background = Color.FromArgb(18, 20, 40),
                DrawClient = DrawTrophies,
                OnKey = k => { if (k != Keys.Escape) return false; CloseById("trophies"); return true; },
            });
        }

        // The Trophy Room: a glossy "display cabinet" - chrome title, a gradient progress
        // bar, little stat badges, and one bevelled card per trophy with a pixel-art cup
        // (gold and sparkling when earned, a dark silhouette when still locked).
        private Bitmap? _trophyTitle;

        // Secret trophies (never listed in the install summary) - they only appear in the Trophy Room.
        private static readonly Achievement[] SecretTrophies =
        {
            new("secret_konami", "Cheat Code Accepted", "Konami code entered. It does not do anything - but you found it."),
        };

        private static Achievement[] TrophyList => AllAchievements.Concat(SecretTrophies).ToArray();

        private void DrawTrophies(Graphics g, Rectangle c)
        {
            // backdrop: deep blue-to-purple with a faint grid, like a 90s game menu
            using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(c, Color.FromArgb(8, 10, 38), Color.FromArgb(44, 16, 74),
                       System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                g.FillRectangle(bg, c);
            using (var grid = new Pen(Color.FromArgb(22, 130, 150, 255)))
            {
                for (int x = c.X; x < c.Right; x += 24) g.DrawLine(grid, x, c.Y, x, c.Bottom);
                for (int y = c.Y; y < c.Bottom; y += 24) g.DrawLine(grid, c.X, y, c.Right, y);
            }

            var trophies = TrophyList;
            var earned = trophies.Count(a => ProgressStore.IsUnlocked(a.Id));
            var total = trophies.Length;

            // chrome pixel title (built once)
            if (_trophyTitle == null)
            {
                const string text = "TROPHY ROOM";
                var pix = new PixBuf(PixelFont.Width(text, 3) + 8, 30);
                BiosLogo.BuildChromeText(pix, text, 2, 2, 3);
                _trophyTitle = pix.ToBitmap();
            }
            var pi = g.InterpolationMode; var po = g.PixelOffsetMode;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            var tw = _trophyTitle.Width * 2; var th = _trophyTitle.Height * 2;
            g.DrawImage(_trophyTitle, new Rectangle(c.X + 16, c.Y + 8, tw, th), 0, 0, _trophyTitle.Width, _trophyTitle.Height, GraphicsUnit.Pixel);
            g.InterpolationMode = pi; g.PixelOffsetMode = po;

            // progress bar with the app's cyan -> magenta gradient
            using var bold = Ui.Font(12f, FontStyle.Bold);
            using var small = Ui.Font(10f, FontStyle.Regular);
            var barX = c.X + 16 + tw + 24;
            var bar = new Rectangle(barX, c.Y + 24, Math.Max(120, c.Right - 16 - barX), 26);
            Raised(g, bar, true, Color.FromArgb(6, 8, 24));
            var fillW = total == 0 ? 0 : (bar.Width - 4) * earned / total;
            if (fillW > 0)
                using (var fill = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(bar.X + 2, bar.Y + 2, Math.Max(2, fillW), bar.Height - 4),
                           Color.FromArgb(0, 214, 235), Color.FromArgb(255, 0, 170), System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                    g.FillRectangle(fill, bar.X + 2, bar.Y + 2, fillW, bar.Height - 4);
            var label = $"{earned} / {total}";
            var ls = g.MeasureString(label, bold);
            g.DrawString(label, bold, Brushes.White, bar.X + (bar.Width - ls.Width) / 2, bar.Y + (bar.Height - ls.Height) / 2);

            // stat badges
            var first = ProgressStore.FirstRun?.ToString("d MMM yyyy") ?? "today";
            var stats = new[]
            {
                $"OPENED {ProgressStore.Runs}x", $"STREAK {ProgressStore.Streak}d (best {ProgressStore.BestStreak})",
                $"INSTALLED {ProgressStore.TotalInstalled}", $"SINCE {first.ToUpperInvariant()}",
            };
            var bx = c.X + 16; var by = c.Y + 66;
            foreach (var s in stats)
            {
                var ss = g.MeasureString(s, small);
                var pill = new Rectangle(bx, by, (int)ss.Width + 18, 24);
                Raised(g, pill, false, Color.FromArgb(40, 44, 100));
                g.DrawString(s, small, Brushes.Gold, pill.X + 9, pill.Y + (pill.Height - ss.Height) / 2);
                bx += pill.Width + 8;
            }

            // trophy cards
            var cols = c.Width >= 940 ? 3 : (c.Width >= 620 ? 2 : 1);
            const int gap = 10, cardH = 92, top = 104;
            var cardW = (c.Width - 32 - gap * (cols - 1)) / cols;
            _icons ??= new DesktopIcons();
            using var titleFont = Ui.Font(12f, FontStyle.Bold);
            using var subFont = Ui.Font(10f, FontStyle.Regular);
            using var lockedAttr = new System.Drawing.Imaging.ImageAttributes();
            lockedAttr.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[]
            {
                new[] { .18f, .18f, .18f, 0f, 0f }, new[] { .18f, .18f, .20f, 0f, 0f }, new[] { .22f, .22f, .30f, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f }, new[] { .02f, .02f, .06f, 0f, 1f },
            }));
            var tick = Environment.TickCount64;

            for (int i = 0; i < trophies.Length; i++)
            {
                var a = trophies[i];
                var col = i % cols; var row = i / cols;
                var card = new Rectangle(c.X + 16 + col * (cardW + gap), c.Y + top + row * (cardH + gap), cardW, cardH);
                if (card.Bottom > c.Bottom) break;
                var got = ProgressStore.IsUnlocked(a.Id);

                // bevelled card; earned ones glow cyan, locked ones sit dark
                Raised(g, card, false, got ? Color.FromArgb(34, 40, 110) : Color.FromArgb(16, 18, 44));
                using (var edge = new Pen(got ? Color.FromArgb(0, 214, 235) : Color.FromArgb(50, 56, 100)))
                    g.DrawRectangle(edge, card.X + 2, card.Y + 2, card.Width - 5, card.Height - 5);

                // trophy cup (24x24 art, drawn at 3x)
                var cup = new Rectangle(card.X + 12, card.Y + (card.Height - 72) / 2, 72, 72);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                var art = _icons.Trophy;
                if (got) g.DrawImage(art, cup, 0, 0, art.Width, art.Height, GraphicsUnit.Pixel);
                else g.DrawImage(art, cup, 0, 0, art.Width, art.Height, GraphicsUnit.Pixel, lockedAttr);
                g.InterpolationMode = pi; g.PixelOffsetMode = po;

                if (got)
                {
                    // two twinkling sparkles, out of phase per card
                    for (int k = 0; k < 2; k++)
                    {
                        var tw2 = 0.5 + 0.5 * Math.Sin(tick / 260.0 + i * 1.7 + k * 2.4);
                        var alpha = (int)(255 * tw2 * tw2);
                        var sx = cup.X + (k == 0 ? 8 : cup.Width - 16); var sy = cup.Y + (k == 0 ? 14 : 30);
                        using var sb = new SolidBrush(Color.FromArgb(alpha, 255, 255, 220));
                        g.FillRectangle(sb, sx - 1, sy - 4, 2, 8); g.FillRectangle(sb, sx - 4, sy - 1, 8, 2);
                    }
                }
                else
                {
                    var q = "?"; var qs = g.MeasureString(q, titleFont);
                    g.DrawString(q, titleFont, Brushes.DimGray, cup.X + (cup.Width - qs.Width) / 2, cup.Y + (cup.Height - qs.Height) / 2 + 6);
                }

                // text
                var tx = cup.Right + 12; var textW = card.Right - tx - 10;
                var secret = a.Id.StartsWith("secret_");
                var heading = got ? a.Title : (secret ? "Secret trophy" : "Locked");
                while (heading.Length > 4 && g.MeasureString(heading, titleFont).Width > textW) heading = heading[..^1];
                g.DrawString(heading, titleFont, got ? Brushes.White : Brushes.Gray, tx, card.Y + 12);
                var lines = WrapTextToWidth(g, !got && secret ? "Some codes are legendary." : a.Subtitle, subFont, textW).Take(3).ToList();
                for (int l = 0; l < lines.Count; l++)
                    g.DrawString(lines[l], subFont, got ? Brushes.LightSteelBlue : Brushes.DimGray, tx, card.Y + 34 + l * 17);
            }
        }

        // ------------------------------------------------------------ Arcade (Snake)

        private SnakeGame? _snake;

        private void OpenSnake()
        {
            if (_snake == null)
            {
                _snake = new SnakeGame();
                _snake.Scored += s => { if (s >= 10) UnlockAchievement("snake_10"); };
                _snake.Died += s => ProgressStore.SetSnakeBest(s);
            }
            _icons ??= new DesktopIcons();
            OpenWindow(new RetroWindow
            {
                Id = "snake",
                Title = "Arcade - Snake",
                Icon = _icons.Arcade,
                Bounds = Cascade(700, 540),
                MinSize = new Size(480, 360),
                Background = Color.FromArgb(12, 18, 36),
                DrawClient = (g, c) => _snake!.Draw(g, c, _mono, _monoLarge, ProgressStore.SnakeBest),
                OnTick = dt => _snake!.Tick(dt),
                OnKey = k =>
                {
                    if (k == Keys.Escape) { CloseById("snake"); return true; }
                    if (k == Keys.Space) { if (_snake!.Mode != SnakeGame.State.Playing) _snake.Start(); return true; }
                    _snake!.Turn(k);
                    return true;
                },
            });
        }

        // ------------------------------------------------------------ Recycle Bin

        private void OpenRecycleBin()
        {
            _icons ??= new DesktopIcons();
            OpenWindow(new RetroWindow
            {
                Id = "recycle",
                Title = "Recycle Bin",
                Icon = _icons.Recycle,
                Bounds = Cascade(520, 260),
                MinSize = new Size(360, 200),
                Background = Color.White,
                DrawClient = (g, c) =>
                {
                    using var f = Ui.Font(13f, FontStyle.Bold);
                    using var s = Ui.Font(12f, FontStyle.Regular);
                    g.DrawString("The Recycle Bin is empty.", f, Brushes.Black, c.X + 20, c.Y + 22);
                    g.DrawString("Nothing here has ever been thrown away.", s, Brushes.DimGray, c.X + 20, c.Y + 58);
                    g.DrawString("(Your restore point is the real safety net.)", s, Brushes.DimGray, c.X + 20, c.Y + 82);
                },
                OnKey = k => { if (k != Keys.Escape) return false; CloseById("recycle"); return true; },
            });
        }
    }
}
