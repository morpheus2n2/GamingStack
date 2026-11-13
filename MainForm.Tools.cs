using static GamingStackGUI.Px;

namespace GamingStackGUI
{
    /// <summary>
    /// More things to do on the desktop: the screensaver, Display Properties, the Arcade
    /// game picker (Minesweeper, Pong, Breakout), Notes, Paint, Disk Defragmenter and a
    /// Command Prompt full of easter eggs. None of these touch Windows settings - Notes
    /// and the saved display choices live in %APPDATA%\GamingStack.
    /// </summary>
    public partial class MainForm
    {
        // ====================================================================== screensaver

        private bool _screensaverOn;
        private long _lastInputTick = Environment.TickCount64;
        private (float X, float Y, float Z)[] _stars = Array.Empty<(float, float, float)>();
        private PointF _ssPos;
        private PointF _ssVel;
        private Bitmap? _ssLogo;
        private long _ssLastTick;
        private long _ssFlashUntil;
        private readonly Random _ssRng = new();

        // Returns true if the screensaver was running (so the key/click that woke it is ignored).
        private bool WakeFromScreensaver()
        {
            _lastInputTick = Environment.TickCount64;
            if (!_screensaverOn) return false;
            _screensaverOn = false;
            return true;
        }

        private void CheckScreensaver()
        {
            var minutes = ProgressStore.ScreensaverMinutes;
            if (minutes <= 0 || _screensaverOn || !InShell || IsBusyPhase) return;
            if (Environment.TickCount64 - _lastInputTick < minutes * 60_000L) return;
            StartScreensaver();
        }

        private void StartScreensaver()
        {
            if (_ssLogo == null)
            {
                var text = "GAMINGSTACK";
                var pix = new PixBuf(PixelFont.Width(text, 3) + 8, 30);
                BiosLogo.BuildChromeText(pix, text, 2, 2, 3);
                _ssLogo = pix.ToBitmap();
            }
            _stars = Enumerable.Range(0, 260)
                .Select(_ => ((float)(_ssRng.NextDouble() * 2 - 1), (float)(_ssRng.NextDouble() * 2 - 1), (float)(_ssRng.NextDouble() * 0.95 + 0.05)))
                .ToArray();
            _ssPos = new PointF(Width / 3f, Height / 3f);
            var speed = Math.Max(160f, Height / 5f);
            _ssVel = new PointF(speed, speed * 0.7f);
            _ssLastTick = Environment.TickCount64;
            _screensaverOn = true;
        }

        private void DrawScreensaver(Graphics g)
        {
            var now = Environment.TickCount64;
            var dt = Math.Min(80, now - _ssLastTick) / 1000f;
            _ssLastTick = now;
            g.Clear(Color.Black);

            // starfield: stars rush outward from the centre
            var cx = Width / 2f; var cy = Height / 2f;
            for (int i = 0; i < _stars.Length; i++)
            {
                var (x, y, z) = _stars[i];
                z -= dt * 0.35f;
                if (z <= 0.02f) { x = (float)(_ssRng.NextDouble() * 2 - 1); y = (float)(_ssRng.NextDouble() * 2 - 1); z = 1f; }
                _stars[i] = (x, y, z);
                var sx = cx + x / z * cx * 0.5f; var sy = cy + y / z * cy * 0.5f;
                var px = cx + x / (z + 0.04f) * cx * 0.5f; var py = cy + y / (z + 0.04f) * cy * 0.5f;
                var a = (int)Math.Clamp((1 - z) * 255, 30, 255);
                using var pen = new Pen(Color.FromArgb(a, 200, 220, 255), Math.Max(1f, (1 - z) * 3f));
                g.DrawLine(pen, px, py, sx, sy);
            }

            // bouncing logo - the corner is the prize
            if (_ssLogo != null)
            {
                var k = Math.Max(2, Height / 270);
                var lw = _ssLogo.Width * k; var lh = _ssLogo.Height * k;
                _ssPos = new PointF(_ssPos.X + _ssVel.X * dt, _ssPos.Y + _ssVel.Y * dt);
                var hitX = false; var hitY = false;
                if (_ssPos.X < 0) { _ssPos.X = 0; _ssVel.X = Math.Abs(_ssVel.X); hitX = true; }
                if (_ssPos.X + lw > Width) { _ssPos.X = Width - lw; _ssVel.X = -Math.Abs(_ssVel.X); hitX = true; }
                if (_ssPos.Y < 0) { _ssPos.Y = 0; _ssVel.Y = Math.Abs(_ssVel.Y); hitY = true; }
                if (_ssPos.Y + lh > Height) { _ssPos.Y = Height - lh; _ssVel.Y = -Math.Abs(_ssVel.Y); hitY = true; }
                if (hitX && hitY)
                {
                    _ssFlashUntil = now + 2500;
                    UnlockAchievement("perfect_corner");
                }
                var prevI = g.InterpolationMode; var prevO = g.PixelOffsetMode;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(_ssLogo, new Rectangle((int)_ssPos.X, (int)_ssPos.Y, lw, lh), 0, 0, _ssLogo.Width, _ssLogo.Height, GraphicsUnit.Pixel);
                g.InterpolationMode = prevI; g.PixelOffsetMode = prevO;
            }

            using var f = Ui.Font(12f * (float)UiScale);
            if (now < _ssFlashUntil)
            {
                const string wow = "PERFECT CORNER!";
                var s = g.MeasureString(wow, f);
                g.DrawString(wow, f, Brushes.Gold, (Width - s.Width) / 2, Height * 0.8f);
            }
            const string hint = "Move the mouse or press a key";
            var hs = g.MeasureString(hint, f);
            g.DrawString(hint, f, Brushes.DimGray, (Width - hs.Width) / 2, Height - hs.Height - 24);
        }

        // ====================================================================== Display Properties

        private readonly List<(Rectangle Rect, Action Act)> _displayButtons = new();

        private void OpenDisplay()
        {
            _icons ??= new DesktopIcons();
            OpenWindow(new RetroWindow
            {
                Id = "display",
                Title = "Display Properties",
                Icon = _icons.Display,
                Bounds = Cascade(760, 520),
                MinSize = new Size(560, 420),
                Background = Color.FromArgb(192, 192, 192),
                DrawClient = DrawDisplay,
                OnClientClick = p => { foreach (var (r, a) in _displayButtons.ToArray()) if (r.Contains(p)) { a(); break; } },
                OnKey = k => { if (k != Keys.Escape) return false; CloseById("display"); return true; },
            });
        }

        private void DrawDisplay(Graphics g, Rectangle c)
        {
            _displayButtons.Clear();
            using var head = Ui.Font(12f, FontStyle.Bold);
            using var norm = Ui.Font(12f, FontStyle.Regular);
            using var note = Ui.Font(11f, FontStyle.Italic);

            var y = 10;
            void Section(string title, string[] labels, string[] values, Func<string> current, Action<string> set, string? hint = null)
            {
                g.DrawString(title, head, Brushes.Black, c.X + 14, c.Y + y);
                y += 24;
                var x = 14;
                for (int i = 0; i < labels.Length; i++)
                {
                    var ts = g.MeasureString(labels[i], norm);
                    var w = (int)ts.Width + 28;
                    var rect = new Rectangle(x, y, w, 30);
                    var selected = string.Equals(current(), values[i], StringComparison.OrdinalIgnoreCase);
                    var abs = new Rectangle(c.X + rect.X, c.Y + rect.Y, rect.Width, rect.Height);
                    Raised(g, abs, selected, selected ? Color.FromArgb(150, 190, 255) : null);
                    g.DrawString(labels[i], norm, Brushes.Black, abs.X + 14 + (selected ? 1 : 0), abs.Y + (abs.Height - ts.Height) / 2 + (selected ? 1 : 0));
                    var v = values[i];
                    _displayButtons.Add((rect, () => set(v)));
                    x += w + 8;
                }
                y += 38;
                if (hint != null) { g.DrawString(hint, note, Brushes.DimGray, c.X + 14, c.Y + y); y += 22; }
                y += 12;
            }

            Section("Boot screen colours", new[] { "Time of day", "Dawn", "Day", "Evening", "Night" },
                new[] { "auto", "dawn", "day", "evening", "night" },
                () => ProgressStore.BootTheme, v => ProgressStore.BootTheme = v,
                "Takes effect the next time GamingStack starts.");
            Section("Boot screen style", new[] { "Classic sky", "Landscape" }, new[] { "classic", "landscape" },
                () => ProgressStore.BootStyle, v => ProgressStore.BootStyle = v,
                "Takes effect the next time GamingStack starts.");
            Section("Screen size (UI scale)", new[] { "100% (default)", "125%", "150%", "200%", "250%" }, new[] { "auto", "1.25", "1.5", "2", "2.5" },
                () => ProgressStore.UiScaleSetting, v => SetUiScale(v),
                $"Now {UiScale * 100:0}%. (Windows says your display is at {DeviceDpi * 100 / 96}%.) Applies straight away.");
            Section("Screensaver", new[] { "Off", "1 min", "3 min", "5 min", "10 min" }, new[] { "0", "1", "3", "5", "10" },
                () => ProgressStore.ScreensaverMinutes.ToString(), v => ProgressStore.ScreensaverMinutes = int.Parse(v),
                "Starts after this long with no mouse or keyboard use.");
        }

        // ====================================================================== Arcade picker + games

        private MinesweeperGame? _mines;
        private PongGame? _pong;
        private BreakoutGame? _breakout;

        private void OpenArcade()
        {
            _icons ??= new DesktopIcons();
            var games = new (string Name, string Blurb, Func<string> Best, Action Open)[]
            {
                ("Snake", "Eat, grow, don't crash.", () => $"best {ProgressStore.SnakeBest}", OpenSnake),
                ("Minesweeper", "9x9, ten mines. Think first.", () => $"fastest {(ProgressStore.GetScore("mines_fast") == 0 ? "-" : (999 - ProgressStore.GetScore("mines_fast")) + "s")}", OpenMinesweeper),
                ("Pong", "First to seven beats the computer.", () => $"wins {ProgressStore.GetScore("pong_wins")}", OpenPong),
                ("Breakout", "Smash every brick. Three lives.", () => $"best {ProgressStore.GetScore("breakout_best")}", OpenBreakout),
            };
            var rects = new List<Rectangle>();
            OpenWindow(new RetroWindow
            {
                Id = "arcade",
                Title = "Arcade",
                Icon = _icons.Arcade,
                Bounds = Cascade(560, 430),
                MinSize = new Size(440, 360),
                Background = Color.FromArgb(14, 12, 40),
                DrawClient = (g, c) =>
                {
                    rects.Clear();
                    using var head = Ui.Font(18f, FontStyle.Bold);
                    using var name = Ui.Font(15f, FontStyle.Bold);
                    using var small = Ui.Font(11f, FontStyle.Regular);
                    g.DrawString("Pick a game", head, Brushes.Gold, c.X + 18, c.Y + 12);
                    for (int i = 0; i < games.Length; i++)
                    {
                        var r = new Rectangle(c.X + 18, c.Y + 58 + i * 82, c.Width - 36, 72);
                        var hover = r.Contains(_mouse);
                        using (var b = new SolidBrush(hover ? Color.FromArgb(50, 60, 140) : Color.FromArgb(28, 30, 80))) g.FillRectangle(b, r);
                        g.DrawRectangle(hover ? Pens.Cyan : Pens.SteelBlue, r);
                        g.DrawString(games[i].Name, name, Brushes.White, r.X + 16, r.Y + 10);
                        g.DrawString(games[i].Blurb, small, Brushes.LightSteelBlue, r.X + 16, r.Y + 40);
                        var best = games[i].Best();
                        var bs = g.MeasureString(best, small);
                        g.DrawString(best, small, Brushes.Cyan, r.Right - bs.Width - 14, r.Y + 26);
                        rects.Add(new Rectangle(r.X - c.X, r.Y - c.Y, r.Width, r.Height));
                    }
                },
                OnClientClick = p => { for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) { games[i].Open(); break; } },
                OnKey = k => { if (k != Keys.Escape) return false; CloseById("arcade"); return true; },
            });
        }

        private void OpenMinesweeper()
        {
            if (_mines == null)
            {
                _mines = new MinesweeperGame();
                _mines.Won += secs =>
                {
                    UnlockAchievement("mine_sweeper");
                    ProgressStore.SetBestScore("mines_fast", 999 - Math.Min(999, secs));   // stored so "bigger is better"
                };
            }
            _icons ??= new DesktopIcons();
            OpenWindow(new RetroWindow
            {
                Id = "minesweeper",
                Title = "Minesweeper",
                Icon = _icons.Arcade,
                Bounds = Cascade(460, 540),
                MinSize = new Size(360, 440),
                Background = Color.FromArgb(192, 192, 192),
                DrawClient = (g, c) =>
                {
                    using var f = Ui.Font(12f);
                    using var b = Ui.Font(14f, FontStyle.Bold);
                    _mines!.Draw(g, c, f, b);
                },
                OnClientClick = p => _mines!.Click(p, false),
                OnClientRightClick = p => _mines!.Click(p, true),
                OnKey = k => { if (k != Keys.Escape) return false; CloseById("minesweeper"); return true; },
            });
        }

        private void OpenPong()
        {
            if (_pong == null)
            {
                _pong = new PongGame();
                _pong.PlayerWon += () =>
                {
                    UnlockAchievement("pong_champ");
                    ProgressStore.SetBestScore("pong_wins", ProgressStore.GetScore("pong_wins") + 1);
                };
            }
            _icons ??= new DesktopIcons();
            OpenWindow(new RetroWindow
            {
                Id = "pong",
                Title = "Pong",
                Icon = _icons.Arcade,
                Bounds = Cascade(680, 460),
                MinSize = new Size(480, 340),
                Background = Color.FromArgb(6, 8, 20),
                DrawClient = (g, c) => _pong!.Draw(g, c, _mono, _monoLarge),
                OnTick = dt => _pong!.Tick(dt),
                OnKey = k =>
                {
                    if (k == Keys.Escape) { CloseById("pong"); return true; }
                    if (k == Keys.Space) { if (_pong!.Mode != PongGame.State.Playing) _pong.Start(); return true; }
                    _pong!.Key(k);
                    return true;
                },
            });
        }

        private void OpenBreakout()
        {
            if (_breakout == null)
            {
                _breakout = new BreakoutGame();
                _breakout.Cleared += () => UnlockAchievement("brick_breaker");
            }
            _icons ??= new DesktopIcons();
            OpenWindow(new RetroWindow
            {
                Id = "breakout",
                Title = "Breakout",
                Icon = _icons.Arcade,
                Bounds = Cascade(680, 560),
                MinSize = new Size(480, 400),
                Background = Color.FromArgb(8, 6, 24),
                DrawClient = (g, c) =>
                {
                    ProgressStore.SetBestScore("breakout_best", _breakout!.Score);
                    _breakout.Draw(g, c, _mono, _monoLarge);
                },
                OnTick = dt => _breakout!.Tick(dt),
                OnKey = k =>
                {
                    if (k == Keys.Escape) { CloseById("breakout"); return true; }
                    if (k == Keys.Space) { if (_breakout!.Mode != BreakoutGame.State.Playing) _breakout.Start(); return true; }
                    _breakout!.Key(k);
                    return true;
                },
            });
        }

        // ====================================================================== Notes

        private readonly System.Text.StringBuilder _notes = new();
        private bool _notesLoaded, _notesDirty;
        private long _notesSavedAt;
        private static string NotesPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingStack", "notes.txt");

        private void SaveNotes()
        {
            if (!_notesDirty) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(NotesPath)!);
                File.WriteAllText(NotesPath, _notes.ToString());
                _notesDirty = false;
            }
            catch { /* notes are a convenience - never crash over them */ }
        }

        private void OpenNotes()
        {
            if (!_notesLoaded)
            {
                _notesLoaded = true;
                try { if (File.Exists(NotesPath)) _notes.Append(File.ReadAllText(NotesPath)); } catch { }
            }
            _icons ??= new DesktopIcons();
            OpenWindow(new RetroWindow
            {
                Id = "notes",
                Title = "Notes - notes.txt",
                Icon = _icons.Notes,
                Bounds = Cascade(680, 480),
                MinSize = new Size(360, 240),
                Background = Color.White,
                DrawClient = DrawNotes,
                OnChar = ch =>
                {
                    if (ch == '\b') { if (_notes.Length > 0) _notes.Length--; }
                    else if (ch == '\r' || ch == '\n') _notes.Append('\n');
                    else if (ch >= ' ') _notes.Append(ch);
                    else return;
                    _notesDirty = true;
                },
                OnTick = _ => { if (_notesDirty && Environment.TickCount64 - _notesSavedAt > 4000) { _notesSavedAt = Environment.TickCount64; SaveNotes(); } },
                OnClosed = SaveNotes,
                OnKey = k => { if (k != Keys.Escape) return false; CloseById("notes"); return true; },
            });
        }

        private void DrawNotes(Graphics g, Rectangle c)
        {
            using var f = Ui.Font(13f);
            var lineH = (int)f.GetHeight(g) + 2;
            var x = c.X + 12; var y = c.Y + 8;
            var maxW = c.Width - 28;
            var text = _notes.ToString();
            if (text.Length == 0)
            {
                g.DrawString("Type here. Your notes are saved automatically.", f, Brushes.Silver, x, y);
            }
            var lines = new List<string>();
            foreach (var para in text.Split('\n'))
            {
                var cur = "";
                foreach (var word in para.Split(' '))
                {
                    var trial = cur.Length == 0 ? word : cur + " " + word;
                    if (cur.Length > 0 && g.MeasureString(trial, f).Width > maxW) { lines.Add(cur); cur = word; }
                    else cur = trial;
                }
                lines.Add(cur);
            }
            var visible = Math.Max(1, (c.Height - 16) / lineH);
            var first = Math.Max(0, lines.Count - visible);
            for (int i = first; i < lines.Count; i++)
                g.DrawString(lines[i], f, Brushes.Black, x, y + (i - first) * lineH);
            if ((Environment.TickCount64 / 500) % 2 == 0)
            {
                var last = lines.Count == 0 ? "" : lines[^1];
                var cx = x + (int)g.MeasureString(last + ".", f).Width - (int)g.MeasureString(".", f).Width;
                g.FillRectangle(Brushes.Black, Math.Min(cx, c.Right - 8), y + (lines.Count - 1 - first) * lineH + 2, 2, lineH - 4);
            }
        }

        // ====================================================================== Paint

        private readonly PixBuf _paint = new(240, 150);
        private Bitmap? _paintBmp;
        private bool _paintInit, _paintDirty = true;
        private int _paintColor = Rgb(0, 0, 0);
        private int _paintBrush = 2;
        private Rectangle _paintCanvas;      // client-relative
        private readonly List<(Rectangle Rect, int Color)> _paintSwatches = new();
        private Rectangle _paintClear;

        private static readonly int[] PaintPalette =
        {
            Rgb(0,0,0), Rgb(128,128,128), Rgb(192,192,192), Rgb(255,255,255),
            Rgb(128,0,0), Rgb(255,0,0), Rgb(128,64,0), Rgb(255,150,40),
            Rgb(128,128,0), Rgb(255,230,0), Rgb(0,128,0), Rgb(0,230,60),
            Rgb(0,128,128), Rgb(0,230,230), Rgb(0,0,160), Rgb(40,90,255),
            Rgb(128,0,128), Rgb(255,0,255), Rgb(255,150,200), Rgb(120,80,40),
        };

        private void OpenPaint()
        {
            if (!_paintInit)
            {
                _paintInit = true;
                Array.Fill(_paint.P, Rgb(255, 255, 255));
            }
            _icons ??= new DesktopIcons();
            OpenWindow(new RetroWindow
            {
                Id = "paint",
                Title = "Paint - untitled",
                Icon = _icons.Paint,
                Bounds = Cascade(760, 560),
                MinSize = new Size(520, 400),
                Background = Color.FromArgb(192, 192, 192),
                DrawClient = DrawPaint,
                OnClientClick = p => { PaintClick(p); },
                OnClientDrag = p => PaintAt(p),
                OnClientUp = () => { },
                OnKey = k =>
                {
                    if (k == Keys.Escape) { CloseById("paint"); return true; }
                    if (k == Keys.OemOpenBrackets) { _paintBrush = Math.Max(1, _paintBrush - 1); return true; }
                    if (k == Keys.OemCloseBrackets) { _paintBrush = Math.Min(8, _paintBrush + 1); return true; }
                    return false;
                },
            });
        }

        private void PaintClick(Point p)
        {
            foreach (var (r, col) in _paintSwatches) if (r.Contains(p)) { _paintColor = col; return; }
            if (_paintClear.Contains(p)) { Array.Fill(_paint.P, Rgb(255, 255, 255)); _paintDirty = true; return; }
            PaintAt(p);
        }

        private void PaintAt(Point p)
        {
            if (!_paintCanvas.Contains(p)) return;
            var sx = _paintCanvas.Width / (double)_paint.W;
            var x = (int)((p.X - _paintCanvas.X) / sx); var y = (int)((p.Y - _paintCanvas.Y) / sx);
            for (int dy = -_paintBrush / 2; dy <= _paintBrush / 2; dy++)
                for (int dx = -_paintBrush / 2; dx <= _paintBrush / 2; dx++)
                    _paint.Set(x + dx, y + dy, _paintColor);
            _paintDirty = true;
        }

        private void DrawPaint(Graphics g, Rectangle c)
        {
            using var f = Ui.Font(11f);
            // palette down the left
            _paintSwatches.Clear();
            for (int i = 0; i < PaintPalette.Length; i++)
            {
                var col = i % 2; var row = i / 2;
                var r = new Rectangle(8 + col * 28, 8 + row * 28, 24, 24);
                var abs = new Rectangle(c.X + r.X, c.Y + r.Y, r.Width, r.Height);
                using (var b = new SolidBrush(Color.FromArgb(PaintPalette[i]))) g.FillRectangle(b, abs);
                g.DrawRectangle(PaintPalette[i] == _paintColor ? Pens.Red : Pens.Black, abs);
                _paintSwatches.Add((r, PaintPalette[i]));
            }
            var clear = new Rectangle(8, 8 + 10 * 28 + 6, 52, 28);
            _paintClear = clear;
            Raised(g, new Rectangle(c.X + clear.X, c.Y + clear.Y, clear.Width, clear.Height));
            g.DrawString("Clear", f, Brushes.Black, c.X + clear.X + 6, c.Y + clear.Y + 6);
            g.DrawString("[ ] brush", f, Brushes.Black, c.X + 8, c.Y + clear.Bottom + 8);
            g.DrawString($"size {_paintBrush}", f, Brushes.Black, c.X + 8, c.Y + clear.Bottom + 26);

            // canvas
            var availW = c.Width - 84; var availH = c.Height - 16;
            var scale = Math.Max(1, Math.Min(availW / _paint.W, availH / _paint.H));
            _paintCanvas = new Rectangle(76, 8, _paint.W * scale, _paint.H * scale);
            if (_paintBmp == null || _paintDirty)
            {
                _paintBmp ??= new Bitmap(_paint.W, _paint.H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                _paint.CopyTo(_paintBmp);
                _paintDirty = false;
            }
            var dest = new Rectangle(c.X + _paintCanvas.X, c.Y + _paintCanvas.Y, _paintCanvas.Width, _paintCanvas.Height);
            Raised(g, Rectangle.Inflate(dest, 2, 2), true);
            var pi = g.InterpolationMode; var po = g.PixelOffsetMode;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(_paintBmp, dest, 0, 0, _paint.W, _paint.H, GraphicsUnit.Pixel);
            g.InterpolationMode = pi; g.PixelOffsetMode = po;
        }

        // ====================================================================== Disk Defragmenter

        private int[] _defragCells = Array.Empty<int>();
        private int[] _defragTarget = Array.Empty<int>();
        private bool _defragRunning, _defragDone;
        private Rectangle _defragButton;
        private static readonly Color[] DefragColors =
        {
            Color.FromArgb(235, 235, 235), Color.DodgerBlue, Color.LimeGreen, Color.Gold, Color.OrangeRed, Color.MediumOrchid
        };

        private void ResetDefrag()
        {
            var rng = new Random();
            const int count = 32 * 14;
            _defragCells = new int[count];
            for (int i = 0; i < count; i++) _defragCells[i] = rng.NextDouble() < 0.18 ? 0 : rng.Next(1, 6);
            _defragTarget = _defragCells.OrderBy(v => v == 0 ? 99 : v).ToArray();   // used blocks grouped, free space last
            _defragRunning = false; _defragDone = false;
        }

        private void OpenDefrag()
        {
            if (_defragCells.Length == 0) ResetDefrag();
            _icons ??= new DesktopIcons();
            OpenWindow(new RetroWindow
            {
                Id = "defrag",
                Title = "Disk Defragmenter",
                Icon = _icons.Defrag,
                Bounds = Cascade(720, 500),
                MinSize = new Size(560, 380),
                Background = Color.FromArgb(192, 192, 192),
                DrawClient = DrawDefrag,
                OnClientClick = p =>
                {
                    if (!_defragButton.Contains(p) || _defragRunning) return;
                    if (_defragDone) ResetDefrag();
                    _defragRunning = true;
                },
                OnTick = _ => TickDefrag(),
                OnKey = k => { if (k != Keys.Escape) return false; CloseById("defrag"); return true; },
            });
        }

        private void TickDefrag()
        {
            if (!_defragRunning) return;
            for (int step = 0; step < 6; step++)
            {
                var i = -1;
                for (int k = 0; k < _defragCells.Length; k++) if (_defragCells[k] != _defragTarget[k]) { i = k; break; }
                if (i < 0)
                {
                    _defragRunning = false; _defragDone = true;
                    UnlockAchievement("defragged");
                    return;
                }
                var j = -1;
                for (int k = i + 1; k < _defragCells.Length; k++) if (_defragCells[k] == _defragTarget[i]) { j = k; break; }
                if (j < 0) { _defragCells[i] = _defragTarget[i]; continue; }
                (_defragCells[i], _defragCells[j]) = (_defragCells[j], _defragCells[i]);
            }
        }

        private void DrawDefrag(Graphics g, Rectangle c)
        {
            using var f = Ui.Font(10.5f);
            using var b = Ui.Font(10.5f, FontStyle.Bold);
            const int cols = 32;
            var rows = _defragCells.Length / cols;
            var cell = Math.Max(8, Math.Min((c.Width - 40) / cols, (c.Height - 150) / rows));
            var gx = c.X + (c.Width - cell * cols) / 2; var gy = c.Y + 14;
            var area = new Rectangle(gx - 3, gy - 3, cell * cols + 6, cell * rows + 6);
            Raised(g, area, true, Color.White);
            for (int i = 0; i < _defragCells.Length; i++)
            {
                var r = new Rectangle(gx + (i % cols) * cell, gy + (i / cols) * cell, cell - 1, cell - 1);
                using var br = new SolidBrush(DefragColors[_defragCells[i]]);
                g.FillRectangle(br, r);
            }

            var correct = 0; for (int i = 0; i < _defragCells.Length; i++) if (_defragCells[i] == _defragTarget[i]) correct++;
            var pct = _defragDone ? 100 : correct * 100 / _defragCells.Length;

            // progress bar with the percentage written inside it
            var bar = new Rectangle(area.X, area.Bottom + 14, area.Width, 26);
            Raised(g, bar, true, Color.White);
            using (var pb = new SolidBrush(Color.FromArgb(10, 36, 106))) g.FillRectangle(pb, bar.X + 2, bar.Y + 2, (bar.Width - 4) * pct / 100, bar.Height - 4);
            var label = $"{pct}% defragmented";
            var ls = g.MeasureString(label, b);
            var lx = bar.X + (bar.Width - ls.Width) / 2;
            g.DrawString(label, b, Brushes.Gray, lx + 1, bar.Y + (bar.Height - ls.Height) / 2 + 1);
            g.DrawString(label, b, pct >= 50 ? Brushes.White : Brushes.Black, lx, bar.Y + (bar.Height - ls.Height) / 2);

            // button on the right, status text wrapping in the space to its left
            var btn = new Rectangle(bar.Right - 130, bar.Bottom + 14, 130, 34);
            _defragButton = new Rectangle(btn.X - c.X, btn.Y - c.Y, btn.Width, btn.Height);
            Raised(g, btn);
            var btnLabel = _defragDone ? "Again" : _defragRunning ? "Working" : "Start";
            var bs = g.MeasureString(btnLabel, b);
            g.DrawString(btnLabel, b, Brushes.Black, btn.X + (btn.Width - bs.Width) / 2, btn.Y + (btn.Height - bs.Height) / 2);

            var status = _defragDone ? "Done! Your drive is 0% faster, but it looks lovely."
                : _defragRunning ? "Tidying blocks..."
                : "This drive is " + (100 - pct) + "% fragmented (not that it matters on an SSD).";
            var y = bar.Bottom + 14;
            foreach (var line in WrapTextToWidth(g, status, f, bar.Width - btn.Width - 20))
            {
                g.DrawString(line, f, Brushes.Black, bar.X, y);
                y += f.Height + 2;
            }
        }

        // ====================================================================== Command Prompt

        private readonly List<string> _cmdLines = new();
        private string _cmdInput = "";
        private readonly List<string> _cmdHistory = new();

        private void OpenCommandPrompt()
        {
            if (_cmdLines.Count == 0)
            {
                _cmdLines.Add($"GamingStack [Version {typeof(MainForm).Assembly.GetName().Version?.ToString(3)}]");
                _cmdLines.Add("(c) GamingStack Systems. Nothing here can hurt your PC. Type HELP.");
                _cmdLines.Add("(Psst... a few secret commands are hiding in here. Type HINT if you want a clue.)");
                _cmdLines.Add("");
            }
            _icons ??= new DesktopIcons();
            OpenWindow(new RetroWindow
            {
                Id = "cmd",
                Title = "Command Prompt",
                Icon = _icons.Cmd,
                Bounds = Cascade(760, 480),
                MinSize = new Size(420, 260),
                Background = Color.Black,
                DrawClient = DrawCmd,
                OnChar = ch =>
                {
                    if (ch == '\b') { if (_cmdInput.Length > 0) _cmdInput = _cmdInput[..^1]; }
                    else if (ch == '\r' || ch == '\n') { RunCommand(_cmdInput); _cmdInput = ""; }
                    else if (ch >= ' ' && _cmdInput.Length < 80) _cmdInput += ch;
                },
                OnKey = k => { if (k != Keys.Escape) return false; CloseById("cmd"); return true; },
            });
        }

        // The secret commands. Each has a name (shown once found) and a riddle-style clue that
        // the HINT command hands out - no spoilers, but enough to point you the right way.
        private sealed record CmdSecret(string Command, string Name, string Clue);

        private static readonly CmdSecret[] CmdSecrets =
        {
            new("xyzzy", "A Hollow Voice", "A very old text adventure taught everyone one magic word. Say it, and see what happens."),
            new("iddqd", "God Mode", "Four letters. A certain space marine never dies when he types them."),
            new("matrix", "The Red Pill", "Which pill do you take? Name the film, all in one word."),
            new("sudo", "Not Linux", "Every Linux user tries this on a Windows machine at least once."),
            new("format", "Point of No Return", "The scariest word in old DOS, usually followed by a drive letter."),
            new("hack", "Hackerman", "What every movie hacker types right before saying they are in."),
            new("fortune", "Fortune Cookie", "Ask the universe for a little wisdom, the way an old Unix toy did - one word."),
            new("cowsay", "Moo", "Something with four legs will say whatever you tell it. Type its name, then a message."),
        };

        // Records a find (once), tells the player, and awards the trophies.
        private void FoundSecret(string command)
        {
            var secret = CmdSecrets.FirstOrDefault(s => s.Command == command);
            if (secret == null) return;
            UnlockAchievement("hacker_voice");
            if (!ProgressStore.AddSecret(command)) return;
            var found = ProgressStore.SecretsFound.Count;
            CmdOut($"  ** Secret found: {secret.Name}  ({found}/{CmdSecrets.Length}) **");
            if (found >= CmdSecrets.Length) { CmdOut("  ** You found every secret. Respect. **"); UnlockAchievement("secret_master"); }
        }

        private static readonly string[] Fortunes =
        {
            "You will install one more launcher than you need.",
            "A backup in the hand is worth two restore points.",
            "The RGB will not make it faster. It will make it better.",
            "Someone, somewhere, is still playing on Windows 98.",
            "Today is a good day to defragment something that does not need it.",
            "The best loading screen is the one you skip.",
        };

        private string CmdPrompt => $"C:\\Users\\{Environment.UserName}>";

        private void DrawCmd(Graphics g, Rectangle c)
        {
            using var f = Ui.Font(12f);
            var lineH = (int)f.GetHeight(g) + 1;
            var visible = Math.Max(2, (c.Height - 12) / lineH);
            var all = new List<string>(_cmdLines) { CmdPrompt + _cmdInput + ((Environment.TickCount64 / 500) % 2 == 0 ? "_" : " ") };
            var first = Math.Max(0, all.Count - visible);
            for (int i = first; i < all.Count; i++)
                g.DrawString(all[i], f, i == all.Count - 1 ? Brushes.White : Brushes.LightGray, c.X + 8, c.Y + 6 + (i - first) * lineH);
        }

        private void CmdOut(params string[] lines) => _cmdLines.AddRange(lines);

        private void RunCommand(string raw)
        {
            _cmdLines.Add(CmdPrompt + raw);
            var line = raw.Trim();
            if (line.Length == 0) return;
            _cmdHistory.Add(line);
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var arg = parts.Length > 1 ? parts[1] : "";

            switch (cmd)
            {
                case "help":
                    CmdOut("Commands:  HELP  VER  DIR  DATE  TIME  WHOAMI  HOSTNAME  ECHO  CLS  NEOFETCH  PING",
                           "Launch:    INSTALL  SNAKE  PONG  MINESWEEPER  BREAKOUT  TROPHIES  NOTES  PAINT  DEFRAG",
                           $"Secrets:   {CmdSecrets.Length} commands are hiding in here. HINT gives a clue, SECRETS shows your progress.");
                    break;
                case "ver": CmdOut($"GamingStack [Version {typeof(MainForm).Assembly.GetName().Version?.ToString(3)}] - runs on Windows, thinks it is 1995."); break;
                case "cls": _cmdLines.Clear(); break;
                case "date": CmdOut(DateTime.Now.ToString("dddd dd/MM/yyyy")); break;
                case "time": CmdOut(DateTime.Now.ToString("HH:mm:ss")); break;
                case "whoami": CmdOut($"{Environment.MachineName}\\{Environment.UserName}"); break;
                case "hostname": CmdOut(Environment.MachineName); break;
                case "echo": CmdOut(arg); break;
                case "dir":
                    CmdOut(" Volume in drive C is GAMING", " Directory of C:\\GAMINGSTACK", "",
                           "01/01/1995  12:00    <DIR>          SAVES",
                           "01/01/1995  12:00    <DIR>          MODS",
                           "24/12/1999  23:59           4,096  README.NFO",
                           "13/11/2026  18:00         640,000  HIGHSCORE.DAT",
                           "01/04/1998  04:20       1,474,560  BOOT.IMG", "               3 File(s)  2,118,656 bytes");
                    break;
                case "ping":
                    var host = string.IsNullOrWhiteSpace(arg) ? "localhost" : arg;
                    CmdOut($"Pinging {host} with 32 bytes of data:", "Reply: bytes=32 time=1ms TTL=64", "Reply: bytes=32 time=1ms TTL=64",
                           "(This is pretend - nothing left your PC.)");
                    break;
                case "neofetch":
                    CmdOut("   ____   ___     " + Environment.UserName + "@" + Environment.MachineName,
                           "  / ___| / __|    ------------------");
                    CmdOut("  | |  _ \\__ \\    OS: " + (_sysInfo?.FirstOrDefault(x => x.Label == "Windows").Value ?? "Windows"),
                           "  | |_| |___) |   CPU: " + (_sysInfo?.FirstOrDefault(x => x.Label == "Processor").Value ?? "(open My Computer first)"),
                           "   \\____|____/    RAM: " + (_sysInfo?.FirstOrDefault(x => x.Label == "Memory").Value ?? "?"),
                           "                  Shell: GamingStack 95");
                    break;
                case "install": EnterTerminalStage(); CmdOut("Opening the Setup Wizard..."); break;
                case "snake": OpenSnake(); CmdOut("Hissss."); break;
                case "pong": OpenPong(); CmdOut("Ping."); break;
                case "minesweeper": OpenMinesweeper(); CmdOut("Careful now."); break;
                case "breakout": OpenBreakout(); CmdOut("Bricks incoming."); break;
                case "trophies": OpenTrophies(); break;
                case "notes": OpenNotes(); break;
                case "paint": OpenPaint(); break;
                case "defrag": OpenDefrag(); break;
                case "exit": CloseById("cmd"); break;

                // ---- clues ----
                case "hint":
                {
                    var hidden = CmdSecrets.Where(s => !ProgressStore.SecretsFound.Contains(s.Command)).ToArray();
                    if (hidden.Length == 0)
                    {
                        CmdOut(ProgressStore.IsUnlocked("secret_konami")
                            ? "No more clues - you have found everything. Nicely done."
                            : "Every command is found. One more secret lives outside the prompt: a famous cheat code from 1986 - use the arrow keys, then B and A.");
                        break;
                    }
                    var pick = hidden[Random.Shared.Next(hidden.Length)];
                    CmdOut($"  Clue: {pick.Clue}", $"  ({CmdSecrets.Length - hidden.Length}/{CmdSecrets.Length} found - ask again for a different clue)");
                    break;
                }
                case "secrets":
                {
                    var found = ProgressStore.SecretsFound;
                    CmdOut($"  Secret commands found: {found.Count}/{CmdSecrets.Length}");
                    foreach (var s in CmdSecrets)
                        CmdOut(found.Contains(s.Command) ? $"   [x] {s.Name}  ({s.Command.ToUpperInvariant()})" : "   [ ] ????????");
                    if (found.Count < CmdSecrets.Length) CmdOut("  Stuck? Type HINT.");
                    break;
                }

                // ---- secrets ----
                case "xyzzy": CmdOut("Nothing happens."); FoundSecret("xyzzy"); break;
                case "iddqd": CmdOut("God mode is not available in installers."); FoundSecret("iddqd"); break;
                case "matrix":
                    var r = new Random(); const string glyphs = "01ABCDEFGHJKLMNPQRSTUVWXYZ+=<>#%";
                    for (int i = 0; i < 12; i++) CmdOut(new string(Enumerable.Range(0, 64).Select(_ => glyphs[r.Next(glyphs.Length)]).ToArray()));
                    CmdOut("Wake up, installer...");
                    FoundSecret("matrix");
                    break;
                case "sudo": CmdOut("Nice try. This is Windows."); FoundSecret("sudo"); break;
                case "format":
                    CmdOut("Whoa there. No.", "(Your restore point has trust issues now.)"); FoundSecret("format"); break;
                case "hack": case "hackthegibson":
                    CmdOut("Accessing mainframe...", "Bypassing firewall...", "Reticulating splines...", "Just kidding. You are already in.");
                    FoundSecret("hack");
                    break;
                case "fortune":
                    CmdOut("  " + Fortunes[Random.Shared.Next(Fortunes.Length)]);
                    FoundSecret("fortune");
                    break;
                case "cowsay":
                {
                    var msg = string.IsNullOrWhiteSpace(arg) ? "Moo." : arg;
                    CmdOut(" " + new string('_', msg.Length + 2), $"< {msg} >", " " + new string('-', msg.Length + 2),
                           "        \\   ^__^", "         \\  (oo)\\_______", "            (__)\\       )\\/\\",
                           "                ||----w |", "                ||     ||");
                    FoundSecret("cowsay");
                    break;
                }
                default:
                    CmdOut($"'{cmd}' is not recognized as an internal or external command,", "operable program or batch file. (Type HELP.)");
                    break;
            }
        }
    }
}
