using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static GamingStackGUI.Px;

namespace GamingStackGUI
{
    /// <summary>
    /// The fake BIOS splash artwork: an original beveled "chip" emblem with a chrome
    /// wordmark, plus a small round "GAME READY" badge - the kind of glossy 90s /
    /// early-2000s firmware branding, drawn from scratch (not a copy of any real BIOS
    /// vendor logo). Drawn as pixel art at low resolution and scaled up with
    /// whole-number nearest-neighbour sampling so it stays crisp on any screen.
    /// A soft shine sweeps across it every few seconds.
    /// </summary>
    internal sealed class BiosLogo : IDisposable
    {
        public const int LogoW = 250, LogoH = 62;
        public const int BadgeW = 66, BadgeH = 54;

        private readonly PixBuf _logoBase = new(LogoW, LogoH);
        private readonly PixBuf _logoFrame = new(LogoW, LogoH);
        private readonly Bitmap _logoBmp = new(LogoW, LogoH, PixelFormat.Format32bppPArgb);
        private readonly PixBuf _badgeBase = new(BadgeW, BadgeH);
        private readonly PixBuf _badgeFrame = new(BadgeW, BadgeH);
        private readonly Bitmap _badgeBmp = new(BadgeW, BadgeH, PixelFormat.Format32bppPArgb);

        public BiosLogo()
        {
            BuildLogo();
            BuildBadge();
        }

        // ------------------------------------------------------------ the logo

        private void BuildLogo()
        {
            var p = _logoBase;
            const int cx = 30, cy = 30;

            // soft cyan halo behind the chip
            for (int y = 0; y < LogoH; y++)
                for (int x = 0; x < 64; x++)
                {
                    var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d < 22 || d > 34) continue;
                    var g = Math.Pow(1 - (d - 22) / 12, 2) * 0.55;
                    var q = Quantise(g, 5, x, y);
                    if (q > 0) p.Set(x, y, Mix(Rgb(0, 0, 0), Rgb(0, 170, 220), q));
                }

            // gold pins: 7 per side
            var pinHi = Rgb(255, 232, 150); var pinLo = Rgb(176, 120, 36);
            for (int i = 0; i < 7; i++)
            {
                var o = 14 + i * 5;
                p.Rect(o, 8, 2, 4, pinHi); p.Rect(o + 1, 8, 1, 4, pinLo);          // top
                p.Rect(o, 48, 2, 4, pinHi); p.Rect(o + 1, 48, 1, 4, pinLo);        // bottom
                p.Rect(8, o, 4, 2, pinHi); p.Rect(8, o + 1, 4, 1, pinLo);          // left
                p.Rect(48, o, 4, 2, pinHi); p.Rect(48, o + 1, 4, 1, pinLo);        // right
            }

            // chip body: brushed-steel gradient with bevelled edges
            p.VGrad(12, 12, 36, 36, Rgb(92, 104, 140), Rgb(30, 36, 62), 10);
            p.Rect(12, 12, 36, 1, Rgb(190, 204, 236)); p.Rect(12, 12, 1, 36, Rgb(160, 176, 212));
            p.Rect(12, 47, 36, 1, Rgb(10, 12, 26)); p.Rect(47, 12, 1, 36, Rgb(14, 18, 34));
            // knock the corners off for a rounded look
            foreach (var (x, y) in new[] { (12, 12), (47, 12), (12, 47), (47, 47) }) p.Set(x, y, 0);

            // the die: glowing gradient with a dark frame
            p.Rect(16, 16, 28, 28, Rgb(6, 8, 22));
            p.VGrad(17, 17, 26, 26, Rgb(0, 236, 255), Rgb(150, 40, 230), 12);
            // faint circuit traces on the die
            var trace = Rgb(255, 255, 255);
            for (int i = 0; i < 5; i++)
            {
                var ty = 19 + i * 5;
                for (int x = 18; x < 42; x++)
                    if (((x + i * 3) % 7) < 3 && Dither(x, ty) > 0.4) p.Set(x, ty, Mix(p.Get(x, ty), trace, 0.18));
            }

            // GS monogram, outlined so it pops off the gradient
            p.TextOutlined(19, 23, "GS", 2, Rgb(255, 255, 255), Rgb(14, 10, 50));
            // pin-1 marker and a glint on the corner
            p.Disc(15, 15, 1, Rgb(255, 255, 255));
            p.Rect(14, 13, 4, 1, Rgb(255, 255, 255));

            // chrome wordmark
            BuildChromeText(p, "GAMINGSTACK", 66, 6, 3);

            // gradient rule under the name
            for (int x = 0; x < 178; x++)
            {
                var c = Ramp(new (double, int)[] { (0, Rgb(0, 214, 235)), (0.5, Rgb(120, 70, 255)), (1, Rgb(255, 0, 170)) },
                    Quantise(x / 177.0, 56, x, 35));
                p.Set(68 + x, 35, c); p.Set(68 + x, 36, Shade(c, 0.55));
            }

            p.Text(68, 41, "SYSTEMS  BIOS  V1.1", 1, Rgb(170, 182, 214), 2);
            p.Text(68, 52, "ENHANCED GAMING FIRMWARE", 1, Rgb(88, 100, 140), 1);
        }

        // Pixel-font text with a chrome look: bright sky-blue top, a hard dark
        // "horizon" in the middle, warm reflected light underneath, a navy outline and
        // a magenta drop shadow - the classic glossy-logo recipe.
        internal static void BuildChromeText(PixBuf dest, string text, int x, int y, int scale)
        {
            var w = PixelFont.Width(text, scale) + 4;
            var h = PixelFont.GlyphH * scale + 4;
            var mask = new PixBuf(w, h);
            mask.Text(2, 2, text, scale, 1);

            var stops = new (double, int)[]
            {
                (0.00, Rgb(255, 255, 255)), (0.30, Rgb(150, 220, 255)), (0.52, Rgb(40, 120, 210)),
                (0.54, Rgb(10, 24, 70)), (0.70, Rgb(40, 130, 230)), (1.00, Rgb(190, 240, 255)),
            };
            var shadow = Rgb(214, 20, 150);
            var outline = Rgb(6, 8, 30);

            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    if (mask.Get(xx, yy) != 0)
                    {
                        var v = (yy - 2) / (double)(PixelFont.GlyphH * scale);
                        dest.Set(x + xx, y + yy, Ramp(stops, v));
                    }
                }
            // outline + shadow go behind: only fill pixels that are still empty
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    if (mask.Get(xx, yy) != 0) continue;
                    var near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (mask.Get(xx + dx, yy + dy) != 0) { near = true; break; }
                    if (near) { if (dest.Get(x + xx, y + yy) == 0) dest.Set(x + xx, y + yy, outline); continue; }
                    // shadow: a short smear down-right of the letter (outline wins over it)
                    if (mask.Get(xx - 1, yy - 1) != 0 || mask.Get(xx - 2, yy - 2) != 0)
                        if (dest.Get(x + xx, y + yy) == 0) dest.Set(x + xx, y + yy, shadow);
                }
        }

        // ------------------------------------------------------------ the badge

        private void BuildBadge()
        {
            var p = _badgeBase;
            const int cx = 33, cy = 21, r = 19;

            // ring: bright teal at the top fading to deep blue at the bottom
            for (int y = 0; y < 44; y++)
                for (int x = 0; x < BadgeW; x++)
                {
                    var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d > r + 1) continue;
                    if (d <= r - 3.5)
                        p.Set(x, y, Mix(Rgb(10, 20, 56), Rgb(2, 6, 22), (y - (cy - r)) / (2.0 * r)));
                    else
                        p.Set(x, y, Mix(Rgb(120, 250, 255), Rgb(0, 80, 190), Quantise((y - (cy - r)) / (2.0 * r), 6, x, y)));
                }

            // five-point gold star, even-odd polygon fill
            var pts = new List<(double X, double Y)>();
            for (int i = 0; i < 10; i++)
            {
                var ang = -Math.PI / 2 + i * Math.PI / 5;
                var rad = i % 2 == 0 ? 12.0 : 5.0;
                pts.Add((cx + Math.Cos(ang) * rad, cy + 0.5 + Math.Sin(ang) * rad));
            }
            for (int y = cy - 14; y <= cy + 12; y++)
                for (int x = cx - 14; x <= cx + 14; x++)
                {
                    var inside = false;
                    for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
                    {
                        if ((pts[i].Y > y) != (pts[j].Y > y) &&
                            x < (pts[j].X - pts[i].X) * (y - pts[i].Y) / (pts[j].Y - pts[i].Y) + pts[i].X)
                            inside = !inside;
                    }
                    if (!inside) continue;
                    var v = (y - (cy - 12)) / 24.0;
                    p.Set(x, y, Mix(Rgb(255, 250, 190), Rgb(240, 150, 30), Quantise(v, 5, x, y)));
                }

            var label = "GAME READY";
            p.Text((BadgeW - PixelFont.Width(label, 1, 1)) / 2, 44, label, 1, Rgb(190, 204, 236));
        }

        // ------------------------------------------------------------ drawing

        /// <summary>Draws the logo with its top-left at (x, y); <paramref name="scale"/> is
        /// how many screen pixels each art pixel becomes.</summary>
        public void DrawLogo(Graphics g, int x, int y, int scale, double t) =>
            Present(g, _logoBase, _logoFrame, _logoBmp, x, y, scale, t, 0);

        public void DrawBadge(Graphics g, int x, int y, int scale, double t) =>
            Present(g, _badgeBase, _badgeFrame, _badgeBmp, x, y, scale, t, 1.7);

        // Copies the static art and lays a diagonal shine over it that sweeps across
        // once every ~4.5 seconds (offset a little per piece so they don't flash together).
        private static void Present(Graphics g, PixBuf src, PixBuf frame, Bitmap bmp, int x, int y, int scale, double t, double phase)
        {
            Array.Copy(src.P, frame.P, src.P.Length);

            var cycle = (t + phase) % 4.5;
            if (cycle < 1.2)
            {
                var pos = cycle / 1.2 * (src.W + src.H + 24) - 12;
                for (int yy = 0; yy < src.H; yy++)
                    for (int xx = 0; xx < src.W; xx++)
                    {
                        var c = frame.P[yy * src.W + xx];
                        if (c == 0) continue;
                        var d = Math.Abs(xx + yy * 0.6 - pos);
                        if (d < 6) frame.P[yy * src.W + xx] = Mix(c, Rgb(255, 255, 255), 0.5 * (1 - d / 6));
                    }
            }

            frame.CopyTo(bmp);
            var prevI = g.InterpolationMode; var prevO = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(bmp, new Rectangle(x, y, src.W * scale, src.H * scale), 0, 0, src.W, src.H, GraphicsUnit.Pixel);
            g.InterpolationMode = prevI; g.PixelOffsetMode = prevO;
        }

        public void Dispose()
        {
            _logoBmp.Dispose();
            _badgeBmp.Dispose();
        }
    }
}
