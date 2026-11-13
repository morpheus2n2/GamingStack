using System.Drawing.Drawing2D;

namespace GamingStackGUI
{
    /// <summary>
    /// The fake desktop's "operating system": windows you can move, resize, minimise,
    /// maximise and close; a taskbar with a button per window; a Start menu; desktop
    /// icons you can click. It is all hand-drawn on the one canvas (see CLAUDE.md -
    /// there are no real child windows), so this file is the little window manager.
    /// The apps themselves live in MainForm.Apps.cs.
    /// </summary>
    public partial class MainForm
    {
        private const int TaskbarH = 44;
        private const int Frame = 4;

        private readonly List<RetroWindow> _windows = new();   // back-to-front z-order
        private int _windowSequence;
        private RetroWindow? _dragWindow;
        private bool _dragResize;
        private Point _dragStart;
        private Rectangle _dragBounds;
        private Point _mouse;
        private bool _startOpen;
        private int _selectedIcon = -1;
        private string? _notice;
        private long _noticeUntil;
        private DesktopIcons? _icons;

        private int TitleH => _titleBarHeight;

        private RetroWindow? ActiveWindow => _windows.LastOrDefault(w => !w.Minimized);

        // ------------------------------------------------------------ window control

        private void OpenWindow(RetroWindow w)
        {
            var existing = _windows.FirstOrDefault(x => x.Id == w.Id);
            if (existing != null) { FocusWindow(existing); return; }

            w.OpenedAt = Environment.TickCount64;
            w.Sequence = ++_windowSequence;
            if (w.RestoreBounds.IsEmpty) w.RestoreBounds = w.Bounds;
            _windows.Add(w);
            _startOpen = false;
            NoteAppOpened(w.Id);
        }

        private void FocusWindow(RetroWindow w)
        {
            w.Minimized = false;
            _windows.Remove(w);
            _windows.Add(w);
        }

        private void MinimizeWindow(RetroWindow w)
        {
            w.Minimized = true;
        }

        private void ToggleMaximize(RetroWindow w)
        {
            if (!w.CanResize) return;
            if (w.Maximized)
            {
                w.Bounds = w.RestoreBounds;
                w.Maximized = false;
            }
            else
            {
                w.RestoreBounds = w.Bounds;
                w.Bounds = new Rectangle(0, 0, VW, VH - TaskbarH);
                w.Maximized = true;
            }
        }

        private void CloseWindow(RetroWindow w)
        {
            if (w.CanClose != null && !w.CanClose())
            {
                ShowNotice("Please wait - that can't be closed while it is working.");
                return;
            }
            _windows.Remove(w);
            w.OnClosed?.Invoke();
        }

        private void ShowNotice(string text)
        {
            _notice = text;
            _noticeUntil = Environment.TickCount64 + 3500;
        }

        // Called from the main tick, regardless of stage.
        private void TickWindows(long dtMs)
        {
            foreach (var w in _windows.ToArray())
                if (!w.Minimized) w.OnTick?.Invoke(dtMs);
        }

        // ------------------------------------------------------------ geometry helpers

        private Rectangle TitleRect(RetroWindow w) =>
            new(w.Bounds.X + Frame, w.Bounds.Y + Frame, w.Bounds.Width - 2 * Frame, TitleH);

        private Rectangle ClientRect(RetroWindow w) =>
            new(w.Bounds.X + Frame, w.Bounds.Y + Frame + TitleH + 1,
                w.Bounds.Width - 2 * Frame, w.Bounds.Height - 2 * Frame - TitleH - 1);

        // Title-bar buttons, right to left: close, then maximise, minimise.
        private Rectangle ButtonRect(RetroWindow w, int index)
        {
            var t = TitleRect(w);
            var size = TitleH - 6;
            var closeX = t.Right - 3 - size;
            var x = index switch
            {
                0 => closeX,
                1 => closeX - 4 - size,
                _ => closeX - 4 - size - size - 1
            };
            return new Rectangle(x, t.Y + 3, size, size);
        }

        private Rectangle GripRect(RetroWindow w) =>
            new(w.Bounds.Right - 20, w.Bounds.Bottom - 20, 20, 20);

        private Rectangle StartButtonRect => new(6, VH - TaskbarH + 5, _startButtonWidth, TaskbarH - 10);
        private int _startButtonWidth = 110;

        private List<(RetroWindow Win, Rectangle Rect)> TaskbarButtons()
        {
            var list = new List<(RetroWindow, Rectangle)>();
            var ordered = _windows.OrderBy(w => w.Sequence).ToList();
            if (ordered.Count == 0) return list;
            var left = StartButtonRect.Right + 10;
            var right = VW - 130;
            var each = Math.Min(230, (right - left) / ordered.Count - 4);
            for (int i = 0; i < ordered.Count; i++)
                list.Add((ordered[i], new Rectangle(left + i * (each + 4), VH - TaskbarH + 5, each, TaskbarH - 10)));
            return list;
        }

        // ------------------------------------------------------------ painting

        private void PaintShell(Graphics g)
        {
            if (_screensaverOn) { DrawScreensaver(g); return; }

            DrawWallpaper(g);                                   // real pixels
            var saved = g.Save();
            g.ScaleTransform((float)UiScale, (float)UiScale);   // everything below is scaled
            DrawDesktopIcons(g);

            foreach (var w in _windows.ToArray())
                if (!w.Minimized) DrawWindow(g, w, w == ActiveWindow);

            DrawTaskbar(g);
            if (_startOpen) DrawStartMenu(g);
            if (_ctxOpen) DrawCtxMenu(g);
            DrawNotice(g);
            if (_stage == Stage.Desktop && _windows.Count == 0)
                DrawSkipHint(g, TaskbarH + 8, "Press SPACE to open the installer now");
            DrawAchievementToast(g, 0, 0, VW, VH - TaskbarH);
            g.Restore(saved);
        }

        private static void Raised(Graphics g, Rectangle r, bool sunken = false, Color? face = null)
        {
            using (var b = new SolidBrush(face ?? Color.FromArgb(192, 192, 192))) g.FillRectangle(b, r);
            var tl = sunken ? Pens.DimGray : Pens.White;
            var br = sunken ? Pens.White : Pens.DimGray;
            g.DrawLine(tl, r.Left, r.Top, r.Right - 1, r.Top);
            g.DrawLine(tl, r.Left, r.Top, r.Left, r.Bottom - 1);
            g.DrawLine(br, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1);
            g.DrawLine(br, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1);
            if (!sunken)
            {
                g.DrawLine(Pens.Black, r.Left, r.Bottom, r.Right, r.Bottom);
                g.DrawLine(Pens.Black, r.Right, r.Top, r.Right, r.Bottom);
            }
        }

        private void DrawWindow(Graphics g, RetroWindow w, bool active)
        {
            var b = w.Bounds;
            var t = Math.Clamp((Environment.TickCount64 - w.OpenedAt) / (double)w.OpenAnimMs, 0, 1);

            if (t < 1)
            {
                // grow-in: just a plain frame scaling up from the centre
                var eased = 1 - Math.Pow(1 - t, 3);
                var sw = Math.Max(2, (int)(b.Width * eased)); var sh = Math.Max(2, (int)(b.Height * eased));
                var r = new Rectangle(b.X + (b.Width - sw) / 2, b.Y + (b.Height - sh) / 2, sw, sh);
                using var face = new SolidBrush(Color.FromArgb(192, 192, 192));
                g.FillRectangle(face, r);
                g.DrawRectangle(Pens.White, r);
                return;
            }

            // outer frame
            Raised(g, b);
            var title = TitleRect(w);

            // title bar: blue gradient when active, grey when not
            using (var grad = new LinearGradientBrush(title,
                active ? Color.FromArgb(10, 36, 106) : Color.FromArgb(110, 110, 120),
                active ? Color.FromArgb(70, 150, 230) : Color.FromArgb(180, 180, 190),
                LinearGradientMode.Horizontal))
                g.FillRectangle(grad, title);

            var textX = title.X + 6;
            if (w.Icon != null)
            {
                var iconSize = Math.Min(w.Icon.Width, title.Height - 4);
                var prevI = g.InterpolationMode; var prevO = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(w.Icon, new Rectangle(textX, title.Y + (title.Height - iconSize) / 2, iconSize, iconSize));
                g.InterpolationMode = prevI; g.PixelOffsetMode = prevO;
                textX += iconSize + 6;
            }
            using (var boldFont = Ui.Font(11f, FontStyle.Bold))
            {
                var ts = g.MeasureString(w.Title, boldFont);
                g.DrawString(w.Title, boldFont, Brushes.White, textX, title.Y + (title.Height - ts.Height) / 2);
            }

            // buttons: minimise, maximise/restore, close
            for (int i = 0; i < 3; i++)
            {
                var r = ButtonRect(w, i);
                if (i == 1 && !w.CanResize) { Raised(g, r, false); continue; }
                var down = _dragWindow == null && r.Contains(_mouse) && (Control.MouseButtons & MouseButtons.Left) != 0;
                Raised(g, r, down);
                var o = down ? 1 : 0;
                using var pen = new Pen(Color.Black, 2);
                if (i == 0)       // close: an X
                {
                    g.DrawLine(pen, r.X + 5 + o, r.Y + 5 + o, r.Right - 6 + o, r.Bottom - 6 + o);
                    g.DrawLine(pen, r.Right - 6 + o, r.Y + 5 + o, r.X + 5 + o, r.Bottom - 6 + o);
                }
                else if (i == 1)  // maximise: a box (two boxes when already maximised)
                {
                    if (w.Maximized)
                    {
                        g.DrawRectangle(pen, r.X + 4 + o, r.Y + 7 + o, r.Width - 12, r.Height - 12);
                        g.DrawRectangle(Pens.Black, r.X + 8 + o, r.Y + 4 + o, r.Width - 12, r.Height - 12);
                    }
                    else
                    {
                        g.DrawRectangle(pen, r.X + 4 + o, r.Y + 4 + o, r.Width - 9, r.Height - 9);
                    }
                }
                else              // minimise: a low bar
                {
                    g.FillRectangle(Brushes.Black, r.X + 5 + o, r.Bottom - 8 + o, r.Width - 12, 3);
                }
            }

            // client area: sunken frame, then the app draws inside it
            var client = ClientRect(w);
            var sunk = Rectangle.Inflate(client, 1, 1);
            Raised(g, sunk, true);
            using (var bg = new SolidBrush(w.Background)) g.FillRectangle(bg, client);

            var state = g.Save();
            g.SetClip(client);
            try { w.DrawClient?.Invoke(g, client); }
            finally { g.Restore(state); }

            // resize grip
            if (w.CanResize && !w.Maximized)
            {
                var gr = GripRect(w);
                for (int i = 4; i <= 16; i += 4)
                {
                    g.DrawLine(Pens.White, gr.Right - i - 1, gr.Bottom - 3, gr.Right - 3, gr.Bottom - i - 1);
                    g.DrawLine(Pens.DimGray, gr.Right - i, gr.Bottom - 3, gr.Right - 3, gr.Bottom - i);
                }
            }
        }

        // ------------------------------------------------------------ icons

        private sealed record DesktopIconItem(string Label, Func<Bitmap> Art, Action Launch);

        private List<DesktopIconItem>? _iconItems;
        private List<DesktopIconItem> IconItems() => _iconItems ??= BuildIconItems();

        private List<DesktopIconItem> BuildIconItems()
        {
            _icons ??= new DesktopIcons();
            var i = _icons;
            return new List<DesktopIconItem>
            {
                new("Gaming", () => i.Gaming, () => LaunchSetup(SetupProfile.Gaming)),
                new("Office", () => i.Office, () => LaunchSetup(SetupProfile.Office)),
                new("Developer", () => i.Developer, () => LaunchSetup(SetupProfile.Developer)),
                new("My Computer", () => i.Computer, OpenMyComputer),
                new("Trophies", () => i.Trophy, OpenTrophies),
                new("Arcade", () => i.Arcade, OpenArcade),
                new("Notes", () => i.Notes, OpenNotes),
                new("Paint", () => i.Paint, OpenPaint),
                new("Command", () => i.Cmd, OpenCommandPrompt),
                new("Defrag", () => i.Defrag, OpenDefrag),
                new("Display", () => i.Display, OpenDisplay),
                new("Recycle Bin", () => i.Recycle, OpenRecycleBin),
            };
        }

        private int IconScale => Math.Max(2, VH / 432);   // icon art grows a little with the screen

        // ---- icon layout: icons sit on a grid of "slots" and can be dragged to any free
        // slot (or swapped with another icon); positions are remembered between runs ----

        private Dictionary<string, int>? _iconSlots;
        private int _iconPress = -1;
        private Point _iconPressPt;
        private bool _iconDragging;
        private Point _iconDragPt;

        private (int Cols, int Per, int CellW, int CellH) IconGrid()
        {
            var size = DesktopIcons.ArtSize * IconScale;
            var cellH = size + 56;
            var cellW = Math.Max(size + 24, 132);
            var per = Math.Max(1, (VH - TaskbarH - 24) / cellH);
            var cols = Math.Max(1, (VW - 10) / (cellW + 10));
            return (cols, per, cellW, cellH);
        }

        private Rectangle SlotRect(int slot)
        {
            var (_, per, cellW, cellH) = IconGrid();
            var col = slot / per; var row = slot % per;
            return new Rectangle(10 + col * (cellW + 10), 24 + row * cellH, cellW, cellH - 6);
        }

        // label -> slot, with any missing or doubled-up icons moved to the first free slot
        private Dictionary<string, int> Slots()
        {
            if (_iconSlots != null) return _iconSlots;
            var (cols, per, _, _) = IconGrid();
            var max = cols * per;
            var saved = ProgressStore.IconSlots;
            var result = new Dictionary<string, int>();
            var used = new HashSet<int>();
            var items = IconItems();
            foreach (var it in items)
                if (saved.TryGetValue(it.Label, out var s) && s >= 0 && s < max && used.Add(s)) result[it.Label] = s;
            var next = 0;
            foreach (var it in items)
            {
                if (result.ContainsKey(it.Label)) continue;
                while (used.Contains(next)) next++;
                used.Add(next); result[it.Label] = next;
            }
            return _iconSlots = result;
        }

        private Rectangle IconCell(int index) => SlotRect(Slots()[IconItems()[index].Label]);

        private int SlotAt(Point p)
        {
            var (cols, per, cellW, cellH) = IconGrid();
            var col = Math.Clamp((p.X - 10) / (cellW + 10), 0, cols - 1);
            var row = Math.Clamp((p.Y - 24) / cellH, 0, per - 1);
            return col * per + row;
        }

        private void DropIcon()
        {
            if (_iconPress < 0) return;
            var slots = Slots();
            var items = IconItems();
            var mine = items[_iconPress].Label;
            var target = SlotAt(_iconDragPt);
            var old = slots[mine];
            var other = slots.FirstOrDefault(kv => kv.Value == target && kv.Key != mine);
            if (other.Key != null) slots[other.Key] = old;     // swap with whoever is there
            slots[mine] = target;
            ProgressStore.SaveIconSlots(slots);
        }

        private void ResetIconLayout()
        {
            ProgressStore.ResetIconSlots();
            _iconSlots = null;
            ShowNotice("Icons arranged.");
        }

        private void DrawDesktopIcons(Graphics g)
        {
            var items = IconItems();
            var scale = IconScale;
            var size = DesktopIcons.ArtSize * scale;
            for (int i = 0; i < items.Count; i++)
            {
                var cell = IconCell(i);
                var dragged = _iconDragging && i == _iconPress;
                var x = cell.X + (cell.Width - size) / 2; var y = cell.Y + 6;
                var selected = i == _selectedIcon;
                if (selected && !dragged)
                {
                    using var hi = new SolidBrush(Color.FromArgb(70, 80, 120, 255));
                    g.FillRectangle(hi, cell);
                    g.DrawRectangle(Pens.White, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
                }
                if (dragged) continue;                                   // drawn at the mouse below
                DrawOneIcon(g, items[i], x, y, size, scale, selected, cell.X, cell.Width, 255);
            }

            if (_iconDragging && _iconPress >= 0)
            {
                // where it would land, and the icon following the mouse
                var target = SlotRect(SlotAt(_iconDragPt));
                using (var dash = new Pen(Color.White, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawRectangle(dash, target);
                var cell = IconCell(_iconPress);
                var gx = _iconDragPt.X - cell.Width / 2; var gy = _iconDragPt.Y - cell.Height / 2;
                DrawOneIcon(g, items[_iconPress], gx + (cell.Width - size) / 2, gy + 6, size, scale, true, gx, cell.Width, 170);
            }
        }

        private void DrawOneIcon(Graphics g, DesktopIconItem item, int x, int y, int size, int scale, bool selected, int cellX, int cellW, int alpha)
        {
            if (alpha >= 255)
            {
                DesktopIcons.Draw(g, item.Art(), x, y, scale);
            }
            else
            {
                using var attr = new System.Drawing.Imaging.ImageAttributes();
                attr.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha / 255f });
                var art = item.Art();
                var pi = g.InterpolationMode; var po = g.PixelOffsetMode;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(art, new Rectangle(x, y, art.Width * scale, art.Height * scale), 0, 0, art.Width, art.Height, GraphicsUnit.Pixel, attr);
                g.InterpolationMode = pi; g.PixelOffsetMode = po;
            }
            var ls = g.MeasureString(item.Label, _mono);
            var lx = cellX + (cellW - ls.Width) / 2;
            var ly = y + size + 4;
            using var shade = new SolidBrush(selected ? Color.FromArgb(Math.Min(alpha, 220), 10, 36, 106) : Color.FromArgb(Math.Min(alpha, 150), 0, 0, 0));
            g.FillRectangle(shade, lx - 2, ly - 1, ls.Width + 4, ls.Height + 2);
            g.DrawString(item.Label, _mono, Brushes.White, lx, ly);
        }

        // ---- desktop right-click menu ----
        private bool _ctxOpen;
        private Point _ctxPos;
        private List<(string Label, Action Run)> CtxItems() => new()
        {
            ("Arrange icons", ResetIconLayout),
            ("Display Properties", OpenDisplay),
            ("Command Prompt", OpenCommandPrompt),
        };

        private Rectangle CtxItemRect(int i)
        {
            var count = CtxItems().Count;
            const int w = 240, h = 30;
            var x = Math.Min(_ctxPos.X, VW - w - 4); var y = Math.Min(_ctxPos.Y, VH - TaskbarH - count * h - 12);
            return new Rectangle(x + 3, y + 3 + i * h, w - 6, h);
        }

        private void DrawCtxMenu(Graphics g)
        {
            var items = CtxItems();
            var first = CtxItemRect(0); var last = CtxItemRect(items.Count - 1);
            Raised(g, new Rectangle(first.X - 3, first.Y - 3, first.Width + 6, last.Bottom - first.Y + 6));
            using var f = Ui.Font(10.5f);
            for (int i = 0; i < items.Count; i++)
            {
                var r = CtxItemRect(i);
                var hover = r.Contains(_mouse);
                if (hover) using (var hb = new SolidBrush(Color.FromArgb(10, 36, 106))) g.FillRectangle(hb, r);
                g.DrawString(items[i].Label, f, hover ? Brushes.White : Brushes.Black, r.X + 10, r.Y + (r.Height - f.Height) / 2);
            }
        }

        // Icons that start the installer: pick that setup type for you (if the
        // wizard is still at its first question) and bring the installer forward.
        private void LaunchSetup(SetupProfile profile)
        {
            if (_stage == Stage.Desktop)
            {
                EnterTerminalStage();
                SelectProfile(profile);
                return;
            }
            if (_stage != Stage.Terminal) return;

            OpenInstaller();
            if (_wizardPhase == WizardPhase.ChooseProfile)
                SelectProfile(profile);
            else
                ShowNotice($"Setup is already running ({SetupProfileInfo.Title(_profile)}).");
        }

        // ------------------------------------------------------------ taskbar + start menu

        private void DrawTaskbar(Graphics g)
        {
            var bar = new Rectangle(0, VH - TaskbarH, VW, TaskbarH);
            Raised(g, bar);

            // Start button
            _icons ??= new DesktopIcons();
            const int logoScale = 2;
            var logoW = _icons.StartLogo.Width * logoScale;
            using var startFont = Ui.Font(14f, FontStyle.Bold);
            var startTextSize = g.MeasureString("Start", startFont);
            _startButtonWidth = (int)startTextSize.Width + logoW + 40;
            var sr = StartButtonRect;
            Raised(g, sr, _startOpen);
            var o = _startOpen ? 1 : 0;
            DesktopIcons.Draw(g, _icons.StartLogo, sr.X + 10 + o, sr.Y + (sr.Height - _icons.StartLogo.Height * logoScale) / 2 + o, logoScale);
            g.DrawString("Start", startFont, Brushes.Black, sr.X + 10 + logoW + 8 + o, sr.Y + (sr.Height - startTextSize.Height) / 2 + o);

            // window buttons
            var active = ActiveWindow;
            foreach (var (win, rect) in TaskbarButtons())
            {
                var pressed = win == active;
                Raised(g, rect, pressed, pressed ? Color.FromArgb(214, 214, 214) : null);
                var tx = rect.X + 6;
                if (win.Icon != null)
                {
                    DesktopIcons.Draw(g, win.Icon, tx, rect.Y + (rect.Height - win.Icon.Height) / 2, 1);
                    tx += win.Icon.Width + 4;
                }
                var tw = rect.Right - tx - 6;
                using var f = Ui.Font(11f, pressed ? FontStyle.Bold : FontStyle.Regular);
                var text = win.Title;
                while (text.Length > 3 && g.MeasureString(text, f).Width > tw) text = text[..^1];
                if (text.Length < win.Title.Length) text += "..";
                g.DrawString(text, f, Brushes.Black, tx + (pressed ? 1 : 0), rect.Y + (rect.Height - f.Height) / 2 + (pressed ? 1 : 0));
            }

            // system tray: a sunken box with a little speaker, then the time over the date
            // (time in bold, date smaller), each measured so they stack cleanly. Hover the
            // clock for the full date.
            using var timeFont = Ui.Font(11f, FontStyle.Bold);
            using var dateFont = Ui.Font(9f, FontStyle.Regular);
            var now = DateTime.Now;
            var timeStr = now.ToString("HH:mm");
            var dateStr = now.ToString("dd/MM/yyyy");
            var timeSize = g.MeasureString(timeStr, timeFont);
            var dateSize = g.MeasureString(dateStr, dateFont);
            var textW = (int)Math.Max(timeSize.Width, dateSize.Width);
            var trayW = textW + 58;
            var tray = new Rectangle(VW - trayW - 8, VH - TaskbarH + 4, trayW, TaskbarH - 8);
            Raised(g, tray, true);

            // speaker icon
            var iy = tray.Y + tray.Height / 2;
            g.FillRectangle(Brushes.Black, tray.X + 10, iy - 3, 4, 6);
            g.FillPolygon(Brushes.Black, new[]
            {
                new Point(tray.X + 14, iy - 3), new Point(tray.X + 21, iy - 8),
                new Point(tray.X + 21, iy + 8), new Point(tray.X + 14, iy + 3),
            });
            using (var wave = new Pen(Color.Black, 1.5f))
            {
                g.DrawArc(wave, tray.X + 20, iy - 5, 7, 10, -55, 110);
                g.DrawArc(wave, tray.X + 22, iy - 8, 9, 16, -55, 110);
            }

            // text block, right-aligned and centred vertically (GDI+ pads each line by ~3px)
            const int pad = 3;
            var blockH = (timeSize.Height - pad) + (dateSize.Height - pad);
            var y0 = tray.Y + (tray.Height - blockH) / 2f - pad / 2f;
            g.DrawString(timeStr, timeFont, Brushes.Black, tray.Right - 8 - timeSize.Width, y0);
            g.DrawString(dateStr, dateFont, Brushes.Black, tray.Right - 8 - dateSize.Width, y0 + timeSize.Height - pad);

            if (tray.Contains(_mouse))
            {
                var full = now.ToString("dddd d MMMM yyyy");
                using var tipFont = Ui.Font(10f);
                var ts = g.MeasureString(full, tipFont);
                var tip = new Rectangle(Math.Max(4, tray.Right - (int)ts.Width - 20), tray.Y - 32, (int)ts.Width + 16, 26);
                Raised(g, tip, false, Color.FromArgb(255, 255, 225));
                g.DrawString(full, tipFont, Brushes.Black, tip.X + 8, tip.Y + (tip.Height - ts.Height) / 2);
            }
        }

        private sealed record StartItem(string Label, Func<Bitmap>? Art, Action? Run);

        private List<StartItem> StartItems()
        {
            _icons ??= new DesktopIcons();
            var i = _icons;
            return new List<StartItem>
            {
                new("Setup Wizard", () => i.Developer, () => { if (_stage == Stage.Desktop) EnterTerminalStage(); else OpenInstaller(); }),
                new("My Computer", () => i.Computer, OpenMyComputer),
                new("Trophies", () => i.Trophy, OpenTrophies),
                new("Arcade", () => i.Arcade, OpenArcade),
                new("Notes", () => i.Notes, OpenNotes),
                new("Command Prompt", () => i.Cmd, OpenCommandPrompt),
                new("Display Properties", () => i.Display, OpenDisplay),
                new("Setup Logs", () => i.Notes, OpenLogsFolder),
                new("-", null, null),
                new("Shut Down...", null, ShutDown),
            };
        }

        private const int StartItemH = 44, StartSepH = 12, StartBand = 44, StartWidth = 340;

        private Rectangle StartMenuRect()
        {
            var items = StartItems();
            var h = items.Sum(it => it.Label == "-" ? StartSepH : StartItemH) + 8;
            return new Rectangle(6, VH - TaskbarH - h, StartWidth, h);
        }

        private Rectangle StartItemRect(int index)
        {
            var m = StartMenuRect();
            var items = StartItems();
            var y = m.Y + 4;
            for (int k = 0; k < index; k++) y += items[k].Label == "-" ? StartSepH : StartItemH;
            return new Rectangle(m.X + StartBand + 4, y, m.Width - StartBand - 8, items[index].Label == "-" ? StartSepH : StartItemH);
        }

        private void DrawStartMenu(Graphics g)
        {
            var m = StartMenuRect();
            Raised(g, m);

            // left band with the vertical wordmark
            var band = new Rectangle(m.X + 3, m.Y + 3, StartBand, m.Height - 6);
            using (var grad = new LinearGradientBrush(band, Color.FromArgb(10, 36, 106), Color.FromArgb(40, 120, 210), LinearGradientMode.Vertical))
                g.FillRectangle(grad, band);
            var state = g.Save();
            g.TranslateTransform(band.X + 6, band.Bottom - 8);
            g.RotateTransform(-90);
            using (var wm = Ui.Font(16f, FontStyle.Bold))
            {
                g.DrawString("GamingStack", wm, Brushes.White, 0, 0);
            }
            g.Restore(state);

            var items = StartItems();
            for (int i = 0; i < items.Count; i++)
            {
                var r = StartItemRect(i);
                if (items[i].Label == "-")
                {
                    g.DrawLine(Pens.DimGray, r.X + 4, r.Y + r.Height / 2, r.Right - 4, r.Y + r.Height / 2);
                    g.DrawLine(Pens.White, r.X + 4, r.Y + r.Height / 2 + 1, r.Right - 4, r.Y + r.Height / 2 + 1);
                    continue;
                }
                var hover = r.Contains(_mouse);
                if (hover) using (var hb = new SolidBrush(Color.FromArgb(10, 36, 106))) g.FillRectangle(hb, r);
                var tx = r.X + 10;
                if (items[i].Art != null)
                {
                    DesktopIcons.Draw(g, items[i].Art!(), r.X + 8, r.Y + (r.Height - 24) / 2, 1);
                    tx = r.X + 44;
                }
                using var f = Ui.Font(13f, FontStyle.Regular);
                g.DrawString(items[i].Label, f, hover ? Brushes.White : Brushes.Black, tx, r.Y + (r.Height - f.Height) / 2);
            }
        }

        private void DrawNotice(Graphics g)
        {
            if (_notice == null) return;
            if (Environment.TickCount64 > _noticeUntil) { _notice = null; return; }
            using var f = Ui.Font(13f, FontStyle.Bold);
            var s = g.MeasureString(_notice, f);
            var r = new Rectangle((int)((VW - s.Width) / 2) - 16, VH - TaskbarH - 62, (int)s.Width + 32, 44);
            Raised(g, r);
            g.DrawString(_notice, f, Brushes.Black, r.X + 16, r.Y + (r.Height - s.Height) / 2);
        }

        // ------------------------------------------------------------ input

        // ------------------------------------------------------------ UI scale
        // Everything on the desktop is laid out in "virtual" pixels (VW x VH) and drawn
        // through a scale transform, so on a 4K screen the whole shell - window chrome,
        // fonts, icons - is simply drawn bigger instead of looking tiny. Mouse positions
        // are converted back with V(). The wallpaper and boot/BIOS art are drawn at real
        // pixel size, before the transform is applied.

        private double _uiScale = -1;

        private double UiScale
        {
            get
            {
                if (_uiScale > 0) return _uiScale;
                var setting = Environment.GetEnvironmentVariable("GAMINGSTACK_UI_SCALE");   // 1 = 100%, 1.5 = 150% ...
                if (string.IsNullOrWhiteSpace(setting)) setting = ProgressStore.UiScaleSetting;
                double v;
                if (!string.Equals(setting, "auto", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(setting, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                    _uiScale = Math.Clamp(v, 1.0, 3.0);
                else
                    _uiScale = 1.0;   // default is exactly 1x - bigger choices are in Display Properties
                return _uiScale;
            }
        }

        private int VW => (int)Math.Ceiling(Width / UiScale);
        private int VH => (int)Math.Ceiling(Height / UiScale);
        private Point V(Point p) => new((int)(p.X / UiScale), (int)(p.Y / UiScale));

        private void SetUiScale(string setting)
        {
            ProgressStore.UiScaleSetting = setting;
            _uiScale = -1;
            RelayoutWindows();
        }

        // After the scale changes, keep every window on screen.
        private void RelayoutWindows()
        {
            foreach (var w in _windows)
            {
                if (w.Maximized) { w.Bounds = new Rectangle(0, 0, VW, VH - TaskbarH); continue; }
                var b = w.Bounds;
                var nw = Math.Min(b.Width, VW);
                var nh = Math.Min(b.Height, VH - TaskbarH);
                w.Bounds = new Rectangle(Math.Clamp(b.X, 0, Math.Max(0, VW - nw)), Math.Clamp(b.Y, 0, Math.Max(0, VH - TaskbarH - nh)), nw, nh);
                w.RestoreBounds = w.Bounds;
            }
        }

        // ------------------------------------------------------------ input

        private enum WindowPart { None, Close, Maximize, Minimize, Title, Grip, Client, Border }

        private (RetroWindow? Win, WindowPart Part) HitTest(Point p)
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                var w = _windows[i];
                if (w.Minimized || !w.Bounds.Contains(p)) continue;
                if (ButtonRect(w, 0).Contains(p)) return (w, WindowPart.Close);
                if (ButtonRect(w, 1).Contains(p)) return (w, WindowPart.Maximize);
                if (ButtonRect(w, 2).Contains(p)) return (w, WindowPart.Minimize);
                if (w.CanResize && !w.Maximized && GripRect(w).Contains(p)) return (w, WindowPart.Grip);
                if (TitleRect(w).Contains(p)) return (w, WindowPart.Title);
                if (ClientRect(w).Contains(p)) return (w, WindowPart.Client);
                return (w, WindowPart.Border);
            }
            return (null, WindowPart.None);
        }

        private bool InShell => _stage is Stage.Desktop or Stage.Terminal;

        private RetroWindow? _clientDragWindow;
        private Point _lastMouseScreen;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (WakeFromScreensaver()) return;
            if (!InShell) return;
            var pt = V(e.Location);
            _mouse = pt;

            if (_ctxOpen)
            {
                var items = CtxItems();
                var picked = -1;
                for (int i = 0; i < items.Count; i++) if (CtxItemRect(i).Contains(pt)) picked = i;
                _ctxOpen = false;
                if (picked >= 0 && e.Button == MouseButtons.Left) { items[picked].Run(); return; }
                if (e.Button == MouseButtons.Left) return;
            }

            if (e.Button == MouseButtons.Right)
            {
                _startOpen = false;
                var (rw, rpart) = HitTest(pt);
                if (rw != null && rpart == WindowPart.Client)
                {
                    FocusWindow(rw);
                    var rc = ClientRect(rw);
                    rw.OnClientRightClick?.Invoke(new Point(pt.X - rc.X, pt.Y - rc.Y));
                }
                else if (rw == null && pt.Y < VH - TaskbarH)
                {
                    _ctxOpen = true; _ctxPos = pt;       // right-click on the bare desktop
                }
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            // Start menu has priority while it is open.
            if (_startOpen)
            {
                var items = StartItems();
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Run != null && StartItemRect(i).Contains(pt))
                    {
                        _startOpen = false;
                        items[i].Run!();
                        return;
                    }
                }
                _startOpen = false;
                if (StartButtonRect.Contains(pt)) return;   // click on Start closes it
            }

            if (pt.Y >= VH - TaskbarH)
            {
                if (StartButtonRect.Contains(pt)) { _startOpen = true; return; }
                foreach (var (win, rect) in TaskbarButtons())
                {
                    if (!rect.Contains(pt)) continue;
                    if (win == ActiveWindow) MinimizeWindow(win); else FocusWindow(win);
                    return;
                }
                return;
            }

            var (w, part) = HitTest(pt);
            if (w != null)
            {
                FocusWindow(w);
                switch (part)
                {
                    case WindowPart.Close: CloseWindow(w); break;
                    case WindowPart.Maximize: ToggleMaximize(w); break;
                    case WindowPart.Minimize: MinimizeWindow(w); break;
                    case WindowPart.Title:
                        if (e.Clicks >= 2) { ToggleMaximize(w); break; }
                        if (w.Maximized) break;
                        _dragWindow = w; _dragResize = false; _dragStart = pt; _dragBounds = w.Bounds;
                        break;
                    case WindowPart.Grip:
                        _dragWindow = w; _dragResize = true; _dragStart = pt; _dragBounds = w.Bounds;
                        break;
                    case WindowPart.Client:
                        var c = ClientRect(w);
                        if (w.OnClientDrag != null || w.OnClientUp != null) _clientDragWindow = w;
                        w.OnClientClick?.Invoke(new Point(pt.X - c.X, pt.Y - c.Y));
                        break;
                }
                return;
            }

            // desktop icons: click selects, double-click opens
            var icons = IconItems();
            for (int i = 0; i < icons.Count; i++)
            {
                if (!IconCell(i).Contains(pt)) continue;
                _selectedIcon = i;
                if (e.Clicks >= 2) { _iconPress = -1; icons[i].Launch(); }
                else { _iconPress = i; _iconPressPt = pt; }     // might turn into a drag
                return;
            }
            _selectedIcon = -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // a few pixels of jitter shouldn't wake the screensaver
            if (_screensaverOn)
            {
                if (Math.Abs(e.X - _lastMouseScreen.X) + Math.Abs(e.Y - _lastMouseScreen.Y) > 6) WakeFromScreensaver();
                return;
            }
            _lastMouseScreen = e.Location;
            _lastInputTick = Environment.TickCount64;

            var pt = V(e.Location);
            _mouse = pt;
            if (!InShell) return;

            if (_iconPress >= 0 && (e.Button & MouseButtons.Left) != 0)
            {
                if (!_iconDragging && Math.Abs(pt.X - _iconPressPt.X) + Math.Abs(pt.Y - _iconPressPt.Y) > 8) _iconDragging = true;
                if (_iconDragging) { _iconDragPt = pt; return; }
            }

            if (_dragWindow != null)
            {
                var dx = pt.X - _dragStart.X; var dy = pt.Y - _dragStart.Y;
                if (_dragResize)
                {
                    var nw = Math.Max(_dragWindow.MinSize.Width, Math.Min(VW - _dragBounds.X, _dragBounds.Width + dx));
                    var nh = Math.Max(_dragWindow.MinSize.Height, Math.Min(VH - TaskbarH - _dragBounds.Y, _dragBounds.Height + dy));
                    _dragWindow.Bounds = new Rectangle(_dragBounds.X, _dragBounds.Y, nw, nh);
                    _dragWindow.RestoreBounds = _dragWindow.Bounds;
                }
                else
                {
                    var nx = Math.Clamp(_dragBounds.X + dx, 80 - _dragBounds.Width, VW - 80);
                    var ny = Math.Clamp(_dragBounds.Y + dy, 0, VH - TaskbarH - TitleH);
                    _dragWindow.Bounds = new Rectangle(nx, ny, _dragBounds.Width, _dragBounds.Height);
                    _dragWindow.RestoreBounds = _dragWindow.Bounds;
                }
                return;
            }

            if (_clientDragWindow != null && (e.Button & MouseButtons.Left) != 0)
            {
                var c = ClientRect(_clientDragWindow);
                _clientDragWindow.OnClientDrag?.Invoke(new Point(pt.X - c.X, pt.Y - c.Y));
                return;
            }

            var (_, part) = HitTest(pt);
            Cursor = part == WindowPart.Grip ? Cursors.SizeNWSE : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_iconDragging) DropIcon();
            _iconPress = -1; _iconDragging = false;
            _dragWindow = null;
            if (_clientDragWindow != null)
            {
                _clientDragWindow.OnClientUp?.Invoke();
                _clientDragWindow = null;
            }
        }

        // Typed characters go to the focused window (Notes, Command Prompt...).
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (_screensaverOn || !InShell) return;
            var w = ActiveWindow;
            if (w?.OnChar != null) { w.OnChar(e.KeyChar); e.Handled = true; }
        }

        // Keys go to the focused window first. Returns true if it used the key.
        private bool RouteKeyToWindow(Keys key)
        {
            if (!InShell) return false;
            if (key == Keys.Escape && (_startOpen || _ctxOpen)) { _startOpen = false; _ctxOpen = false; return true; }
            var w = ActiveWindow;
            return w?.OnKey != null && w.OnKey(key);
        }

        // Opens the folder the setup logs are saved in (made if it does not exist yet).
        private void OpenLogsFolder()
        {
            try
            {
                Directory.CreateDirectory(InstallerEngine.LogsFolder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{InstallerEngine.LogsFolder}\"") { UseShellExecute = true });
            }
            catch { ShowNotice("Could not open the logs folder."); }
        }

        private void ShutDown()
        {
            if (IsBusyPhase) { ShowNotice("Can't shut down while a backup or install is running."); return; }
            Close();
        }
    }
}
