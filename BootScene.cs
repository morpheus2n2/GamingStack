using static GamingStackGUI.Px;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GamingStackGUI
{
    /// <summary>
    /// The boot-screen artwork: an original 16/32-bit style sunset scene (dithered
    /// gradient sky, glowing striped sun, snow-capped mountains, forest hills, shaded
    /// clouds, a waving pixel-lettered flag, birds and twinkling stars).
    ///
    /// Everything is drawn procedurally into a small low-resolution pixel buffer and
    /// then scaled up with nearest-neighbour sampling, the same trick real SNES/Mega
    /// Drive games used. That keeps the pixels crisp at any screen size, and it means
    /// there are no image files to lose (the old design shipped four PNG sprites).
    /// All artwork is original - nothing here is copied from Windows or any game.
    /// </summary>
    internal sealed class BootScene : IDisposable
    {
        // Everything is laid out for a 480x270 design area. On a bigger screen the
        // buffer simply gets more rows/columns (see the constructor), and the scene is
        // anchored to the bottom so the hills always sit under the progress bar.
        private const int DesignH = 270;

        private readonly int _w, _h, _scale, _oy;
        private readonly Bitmap _buf;
        private readonly Graphics _g;

        private readonly Bitmap _backdrop;           // sky + glow + sun, never changes
        private readonly List<Layer> _layers = new();
        private readonly Layer? _farMountains, _midMountains, _farHills, _nearHills;
        private readonly bool _classic;
        private readonly Bitmap? _wordmark, _footer;

        /// <summary>True for the classic layout: a full-screen cloudy sky with a big waving
        /// flag and wordmark in the middle (set GAMINGSTACK_BOOT_STYLE=landscape for the
        /// mountains-and-hills scene instead).</summary>
        public bool Classic => _classic;

        public static bool ClassicByDefault =>
            !string.Equals(Environment.GetEnvironmentVariable("GAMINGSTACK_BOOT_STYLE") is { Length: > 0 } envStyle ? envStyle : ProgressStore.BootStyle,
                "landscape", StringComparison.OrdinalIgnoreCase);
        private readonly Bitmap[] _clouds;
        private readonly CloudInst[] _cloudInst;
        private readonly (int X, int Y, double Phase)[] _stars;
        private readonly Bitmap _flagBmp;
        private readonly PixBuf _flagPix, _barPix;
        private readonly Bitmap _barBmp;

        private const int FlagW = 156, FlagH = 46, FlagPad = 12;

        private sealed record Layer(Bitmap Bmp, double Speed);
        private sealed record CloudInst(int Sprite, int Y, double Speed, double Offset);

        // ---------------------------------------------------------------- setup

        private readonly BootTheme _theme;

        public BootScene(int screenW, int screenH, BootTheme? theme = null, bool classic = true)
        {
            _theme = theme ?? BootTheme.Evening;
            _classic = classic;
            // Whole-number scale so every low-res pixel becomes an identical square.
            _scale = Math.Max(1, screenH / DesignH);
            _w = (int)Math.Ceiling(screenW / (double)_scale);
            _h = (int)Math.Ceiling(screenH / (double)_scale);
            _w = Math.Max(_w, 320);
            _h = Math.Max(_h, DesignH);
            _oy = _h - DesignH; // extra rows (if any) are just more sky, up top

            _buf = new Bitmap(_w, _h, PixelFormat.Format32bppPArgb);
            _g = Graphics.FromImage(_buf);
            _g.InterpolationMode = InterpolationMode.NearestNeighbor;
            _g.PixelOffsetMode = PixelOffsetMode.Half;
            _g.SmoothingMode = SmoothingMode.None;
            _g.CompositingQuality = CompositingQuality.HighSpeed;

            _backdrop = BuildBackdrop();

            if (!classic)
            {
                // Back to front. Speeds are low-res pixels per second - slower = further away.
                _farMountains = Add(BuildRidge(960, 168, new[] { (2, 26.0, 0.3), (5, 14.0, 1.7), (11, 7.0, 0.4), (23, 3.0, 2.2) },
                    ridged: true, _theme.FarMountains,
                    hazeFrom: 6, hazeTo: 58, snowLine: 138, trees: 0, seed: 11), 2);
                _midMountains = Add(BuildRidge(960, 188, new[] { (3, 18.0, 2.1), (7, 12.0, 0.2), (13, 6.0, 1.0) },
                    ridged: true, _theme.MidMountains,
                    hazeFrom: 5, hazeTo: 52, snowLine: -1, trees: 0, seed: 12), 4);
                _farHills = Add(BuildRidge(960, 202, new[] { (3, 10.0, 0.5), (8, 5.0, 1.3), (17, 2.0, 0.7) },
                    ridged: false, _theme.FarHills,
                    hazeFrom: 10, hazeTo: 44, snowLine: -1, trees: 70, seed: 13), 8);
                _nearHills = Add(BuildRidge(960, 220, new[] { (2, 10.0, 1.1), (6, 5.0, 0.2), (15, 2.0, 2.5) },
                    ridged: false, _theme.NearHills,
                    hazeFrom: 12, hazeTo: 46, snowLine: -1, trees: 26, seed: 14), 15);

            }

            _clouds = new[]
            {
                BuildCloud(96, 30, 1, _theme), BuildCloud(64, 22, 2, _theme),
                BuildCloud(128, 38, 3, _theme), BuildCloud(80, 26, 4, _theme)
            };
            _cloudInst = new[]
            {
                new CloudInst(1, 46, 5.0, 40), new CloudInst(0, 70, 7.5, 260), new CloudInst(2, 96, 10.0, 90),
                new CloudInst(3, 38, 6.0, 420), new CloudInst(1, 120, 12.0, 330), new CloudInst(0, 24, 4.0, 150),
            };
            if (classic)
            {
                // Big, soft clouds drifting across the WHOLE screen, bigger and faster the
                // closer to the bottom - the sky-full-of-clouds feel of a 90s boot screen.
                _clouds = new[]
                {
                    BuildCloud(240, 70, 5, _theme), BuildCloud(190, 56, 6, _theme), BuildCloud(300, 84, 7, _theme),
                    BuildCloud(160, 46, 8, _theme), BuildCloud(220, 64, 9, _theme),
                };
                var cr = new Random(21);
                var list = new List<CloudInst>();
                for (int i = 0; i < 12; i++)
                    list.Add(new CloudInst(cr.Next(5), i * 21 - 8, 2.5 + i * 1.3, cr.Next(0, 900)));
                _cloudInst = list.ToArray();

                // Static overlays: the chrome wordmark and a small copyright line.
                var text = "GAMINGSTACK 95";
                var wmW = PixelFont.Width(text, 3) + 8;
                var wm = new PixBuf(wmW, 30);
                BiosLogo.BuildChromeText(wm, text, 2, 2, 3);
                _wordmark = wm.ToBitmap();

                var foot = "(C) 1995-2026 GAMINGSTACK SYSTEMS";
                var fp = new PixBuf(PixelFont.Width(foot, 1) + 4, 12);
                fp.Text(3, 3, foot, 1, Rgb(10, 20, 60));
                fp.Text(2, 2, foot, 1, Rgb(255, 255, 255));
                _footer = fp.ToBitmap();
            }

            var rng = new Random(77);
            _stars = Enumerable.Range(0, 46)
                .Select(_ => (rng.Next(0, _w), rng.Next(0, Math.Max(10, 62 + _oy)), rng.NextDouble() * Math.PI * 2))
                .ToArray();

            _flagPix = new PixBuf(FlagW + FlagPad * 2, FlagH + FlagPad * 2);
            _flagBmp = new Bitmap(_flagPix.W, _flagPix.H, PixelFormat.Format32bppPArgb);
            _barPix = new PixBuf(_w, BarRows);
            _barBmp = new Bitmap(_w, BarRows, PixelFormat.Format32bppPArgb);
        }

        private Layer Add(Bitmap bmp, double speed)
        {
            var layer = new Layer(bmp, speed);
            _layers.Add(layer);
            return layer;
        }

        // Dithered gradient sky, a soft sun glow and the striped sun itself.
        private Bitmap BuildBackdrop()
        {
            var pix = new PixBuf(_w, _h);
            var stops = _theme.Sky;
            const int bands = 26;
            var skyH = _classic ? _h : 205 + _oy;
            // t -> nearest dithered band -> colour.
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    var t = Math.Min(1.0, y / (double)skyH);
                    var v = t * bands;
                    var b = Math.Floor(v);
                    if (v - b > Dither(x, y)) b++;
                    pix.Set(x, y, Ramp(stops, Math.Min(1.0, b / bands)));
                }

            // Sun: sits on the horizon behind the mountains, right of centre.
            int cx = (int)(_w * _theme.SunX), cy = _theme.SunY + _oy;
            const int radius = 30, glow = 96;
            for (int y = Math.Max(0, cy - glow); y < Math.Min(_h, cy + glow); y++)
                for (int x = Math.Max(0, cx - glow); x < Math.Min(_w, cx + glow); x++)
                {
                    var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d > glow) continue;
                    var g = Math.Pow(1 - d / glow, 2.2) * _theme.GlowStrength;
                    var level = Quantise(g, 7, x, y);
                    pix.Set(x, y, Mix(pix.P[y * _w + x], _theme.Glow, level));
                }

            var sunStops = _theme.Sun;
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    var dx = x - cx; var dy = y - cy;
                    if (dx * dx + dy * dy > radius * radius) continue;
                    // Horizontal slits that widen toward the bottom - the "sunset stripes".
                    var rel = dy + radius; // 0..2r
                    if (_theme.SunStripes && rel > radius + 6 && (rel % 7) < (rel - radius) / 7) continue;
                    var tt = rel / (2.0 * radius);
                    pix.Set(x, y, Ramp(sunStops, Quantise(tt, 9, x, y)));
                }

            // The moon gets a few darker craters.
            if (_theme.Moon)
            {
                var crater = Rgb(186, 198, 232);
                foreach (var (mx, my, mr) in new[] { (-10, -8, 5), (8, 4, 7), (-6, 14, 4), (14, -12, 3) })
                    for (int y = -mr; y <= mr; y++)
                        for (int x = -mr; x <= mr; x++)
                            if (x * x + y * y <= mr * mr && Dither(cx + mx + x, cy + my + y) > 0.35)
                                pix.Set(cx + mx + x, cy + my + y, crater);
            }

            return pix.ToBitmap();
        }

        // A wide, horizontally-tileable strip of landscape (mountains or hills),
        // shaded by which way each slope faces the sun, hazed toward the bottom, with
        // optional snow caps and pine trees. Heights are sums of sine waves with whole
        // numbers of cycles across the strip, which is what makes it wrap seamlessly.
        private Bitmap BuildRidge(int width, double baseY, (int K, double Amp, double Phase)[] harmonics, bool ridged,
            LayerColors colors, double hazeFrom, double hazeTo, int snowLine, int trees, int seed)
        {
            int lit = colors.Lit, shade = colors.Shade, haze = colors.Haze;
            var pix = new PixBuf(width, _h);
            var ridge = new int[width];
            for (int x = 0; x < width; x++)
            {
                double sum = 0;
                foreach (var (k, amp, phase) in harmonics)
                {
                    var s = Math.Sin(2 * Math.PI * k * x / width + phase);
                    sum += amp * (ridged ? 1 - Math.Abs(s) : s * 0.5 + 0.5);
                }
                ridge[x] = (int)Math.Round(baseY - sum) + _oy;
            }

            for (int x = 0; x < width; x++)
            {
                var slope = ridge[(x + 3) % width] - ridge[(x - 3 + width) % width];
                var facing = Math.Clamp(0.5 + slope / 10.0, 0, 1); // 1 = faces the sun
                for (int y = Math.Max(0, ridge[x]); y < _h; y++)
                {
                    var d = y - ridge[x];
                    var c = Mix(shade, lit, Quantise(facing, 3, x, y));
                    if (d == 0) c = Mix(c, _theme.Rim, 0.35); // sunlit rim
                    var hz = (d - hazeFrom) / (hazeTo - hazeFrom);
                    c = Mix(c, haze, Quantise(hz * 0.92, 5, x, y));
                    if (snowLine > 0 && ridge[x] < snowLine + _oy)
                    {
                        var depth = 5 + (snowLine + _oy - ridge[x]) * 0.30 + Dither(x, y) * 3;
                        if (d < depth) c = facing > 0.5 ? _theme.SnowLit : _theme.SnowShade;
                    }
                    pix.Set(x, y, c);
                }
            }

            if (trees > 0)
            {
                var rng = new Random(seed);
                for (int i = 0; i < trees; i++)
                {
                    var x = rng.Next(width);
                    var h = rng.Next(9, 17);
                    DrawPine(pix, x, ridge[x] + 2, h, _theme);
                }
            }

            return pix.ToBitmap();
        }

        // A little three-tone pine: dark on the shaded (left) side, light toward the sun.
        private static void DrawPine(PixBuf pix, int x, int baseY, int h, BootTheme theme)
        {
            var dark = theme.TreeDark; var mid = theme.TreeMid; var light = theme.TreeLight;
            for (int i = 0; i < h; i++)
            {
                var half = 1 + i * 3 / Math.Max(1, h) + (i % 3 == 2 ? 1 : 0);
                var y = baseY - h + i;
                for (int dx = -half; dx <= half; dx++)
                    pix.SetWrap(x + dx, y, dx < -half / 3 ? dark : (dx > half / 3 ? light : mid));
            }
            pix.SetWrap(x, baseY, Rgb(60, 36, 24));
            pix.SetWrap(x, baseY + 1, Rgb(60, 36, 24));
        }

        // A soft-shaded cloud: overlapping puffs, flat underside, white tops fading to
        // blue-grey bellies through dithered steps (no hard outline, like 16-bit art).
        private static Bitmap BuildCloud(int w, int h, int seed, BootTheme theme)
        {
            var rng = new Random(seed * 31);
            var puffs = new List<(double X, double Y, double R)>();
            var count = Math.Max(4, w / 18);
            for (int i = 0; i < count; i++)
            {
                var t = (i + 0.5) / count;
                var bulge = Math.Sin(t * Math.PI);
                var r = h * (0.22 + 0.30 * bulge) * (0.65 + rng.NextDouble() * 0.7);
                puffs.Add((t * (w - 2 * r) + r, h * 0.70 - r * 0.8 - bulge * h * 0.12 + (rng.NextDouble() - 0.5) * h * 0.12, r));
            }

            var pix = new PixBuf(w, h);
            var baseLine = (int)(h * 0.76);
            var inside = new bool[w, h];
            var top = new int[w];
            for (int x = 0; x < w; x++)
            {
                top[x] = int.MaxValue;
                for (int y = 0; y <= baseLine && y < h; y++)
                {
                    foreach (var p in puffs)
                    {
                        var dx = x - p.X; var dy = y - p.Y;
                        if (dx * dx + dy * dy <= p.R * p.R) { inside[x, y] = true; break; }
                    }
                    if (inside[x, y] && top[x] == int.MaxValue) top[x] = y;
                }
            }

            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    if (!inside[x, y]) continue;
                    var fromTop = y - top[x];
                    var fromBottom = baseLine - y;
                    var u = Math.Min(1.0, fromTop / (h * 0.5)) * 0.55 + (1 - Math.Min(1.0, fromBottom / 7.0)) * 0.45;
                    var q = Quantise(u, 4, x, y);
                    pix.Set(x, y, Mix(theme.CloudLight, theme.CloudShade, q));
                }
            return pix.ToBitmap();
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>Renders the scene at time <paramref name="t"/> (seconds) and
        /// scales it onto <paramref name="g"/>. <paramref name="fade"/> is 0..1.</summary>
        public void Draw(Graphics g, int screenW, int screenH, double t, double fade = 1, double progress = 0)
        {
            RenderBuffer(t, progress);

            var prevInterp = g.InterpolationMode;
            var prevOffset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_buf, new Rectangle(0, screenH - _h * _scale, _w * _scale, _h * _scale),
                0, 0, _w, _h, GraphicsUnit.Pixel);
            g.InterpolationMode = prevInterp;
            g.PixelOffsetMode = prevOffset;

            if (fade < 1)
            {
                using var black = new SolidBrush(Color.FromArgb((int)(255 * (1 - Math.Clamp(fade, 0, 1))), 0, 0, 0));
                g.FillRectangle(black, 0, 0, screenW, screenH);
            }
        }

        private void DrawStrip(Layer layer, double t)
        {
            var wStrip = layer.Bmp.Width;
            var offset = (t * layer.Speed) % wStrip;
            for (var x = -offset; x < _w; x += wStrip)
                _g.DrawImage(layer.Bmp, (int)Math.Round(x), 0, wStrip, layer.Bmp.Height);
        }

        private void RenderBuffer(double t, double progress)
        {
            _g.DrawImage(_backdrop, 0, 0, _w, _h);

            // Stars - only up in the dark part of the sky, twinkling out of phase.
            foreach (var (x, y, phase) in _stars)
            {
                var fadeByHeight = 1 - y / (double)(_theme.StarReach + _oy);
                var tw = 0.5 + 0.5 * Math.Sin(t * 2.4 + phase);
                var a = (int)(255 * _theme.Stars * Math.Clamp(fadeByHeight * (0.35 + 0.65 * tw), 0, 1));
                if (a < 20) continue;
                using var br = new SolidBrush(Color.FromArgb(a, 236, 242, 255));
                _g.FillRectangle(br, x, y, 1, 1);
                if (tw > 0.9 && fadeByHeight > 0.5)
                {
                    using var glint = new SolidBrush(Color.FromArgb(a / 2, 236, 242, 255));
                    _g.FillRectangle(glint, x - 1, y, 3, 1);
                    _g.FillRectangle(glint, x, y - 1, 1, 3);
                }
            }

            if (_classic)
            {
                DrawClouds(t);
                DrawFlag(t);
                if (_wordmark != null)
                    _g.DrawImage(_wordmark, (_w - _wordmark.Width) / 2, 56 + _oy + FlagH + 22, _wordmark.Width, _wordmark.Height);
                if (_footer != null)
                    _g.DrawImage(_footer, (_w - _footer.Width) / 2, _h - BarRows - 16, _footer.Width, _footer.Height);
                DrawBirds(t);
                DrawProgressBar(progress, t);
                return;
            }

            DrawStrip(_farMountains!, t);
            DrawStrip(_midMountains!, t);
            DrawClouds(t);
            DrawStrip(_farHills!, t);
            DrawFlag(t);
            DrawStrip(_nearHills!, t);
            DrawBirds(t);
            DrawProgressBar(progress, t);
        }

        private void DrawClouds(double t)
        {
            foreach (var c in _cloudInst)
            {
                var bmp = _clouds[c.Sprite];
                var travel = _w + bmp.Width;
                var x = _w - (int)((t * c.Speed + c.Offset) % travel);
                _g.DrawImage(bmp, x, c.Y + _oy, bmp.Width, bmp.Height);
            }
        }

        private void DrawBirds(double t)
        {
            var dark = Color.FromArgb(_theme.Bird);
            using var br = new SolidBrush(dark);
            for (int i = 0; i < 4; i++)
            {
                var speed = 22 + i * 4;
                var x = _w + 10 - (int)((t * speed + i * 140) % (_w + 40));
                var y = 60 + _oy + i * 14 + (int)(Math.Sin(t * 1.3 + i) * 3);
                var up = ((int)(t * 5 + i) & 1) == 0;
                void P(int dx, int dy) => _g.FillRectangle(br, x + dx, y + dy, 1, 1);
                P(0, 0);
                if (up) { P(-1, -1); P(-2, -2); P(1, -1); P(2, -2); }
                else { P(-1, 0); P(-2, 1); P(1, 0); P(2, 1); }
            }
        }

        // ---------------------------------------------------------------- flag

        private const string FlagText = "GAMINGSTACK";

        // Is the flag lettering lit at flag-local (x, y)? The 5x7 pixel font scaled 2x
        // with a 1px gap, centred in the middle band of the flag.
        private static bool TextPixel(int x, int y)
        {
            const int scale = 2, gap = 1;
            var totalW = PixelFont.Width(FlagText, scale, gap);
            var x0 = (FlagW - 16 - totalW) / 2 + 8;
            var y0 = (FlagH - PixelFont.GlyphH * scale) / 2;
            var lx = x - x0; var ly = y - y0;
            if (lx < 0 || ly < 0 || lx >= totalW || ly >= PixelFont.GlyphH * scale) return false;
            var pitch = PixelFont.GlyphW * scale + gap;
            var idx = lx / pitch; var gx = lx % pitch;
            if (gx >= PixelFont.GlyphW * scale) return false;
            return PixelFont.Lit(FlagText[idx], gx / scale, ly / scale);
        }

        // The cloth is built one column at a time. Each column is pushed up or down by
        // a travelling wave whose strength grows toward the free end (the pole end is
        // pinned, the tip flaps most), shaded by the slope of the wave so folds read as
        // light and dark, and squashed slightly where it tilts away from the viewer.
        // The free end has a swallow-tail notch so it reads as a proper pennant.
        private void DrawFlag(double t)
        {
            var poleX = _classic ? _w / 2 - FlagW / 2 : _w / 2 - FlagW / 2 - 4;
            var poleTop = _classic ? 50 + _oy : 50 + _oy;
            var poleBottom = 232 + _oy;
            var flagY = _classic ? 56 + _oy : poleTop + 6;

            if (!_classic)
            using (var lightEdge = new SolidBrush(Color.FromArgb(240, 242, 250)))
            using (var body = new SolidBrush(Color.FromArgb(150, 156, 178)))
            using (var shadow = new SolidBrush(Color.FromArgb(86, 92, 120)))
            using (var gold = new SolidBrush(Color.FromArgb(255, 210, 86)))
            using (var goldHi = new SolidBrush(Color.FromArgb(255, 248, 196)))
            using (var goldLo = new SolidBrush(Color.FromArgb(196, 132, 40)))
            {
                _g.FillRectangle(shadow, poleX - 2, poleTop, 1, poleBottom - poleTop);
                _g.FillRectangle(body, poleX - 1, poleTop, 1, poleBottom - poleTop);
                _g.FillRectangle(lightEdge, poleX, poleTop, 1, poleBottom - poleTop);
                // finial: a small gold ball on a collar
                _g.FillRectangle(goldLo, poleX - 2, poleTop - 1, 4, 1);
                _g.FillRectangle(gold, poleX - 2, poleTop - 5, 4, 4);
                _g.FillRectangle(goldHi, poleX - 2, poleTop - 5, 2, 2);
                _g.FillRectangle(gold, poleX - 1, poleTop - 6, 2, 1);
            }

            // (light, dark) for each of the three bands: cyan, near-black, magenta
            var bands = new[]
            {
                (Light: Rgb(170, 252, 255), Dark: Rgb(0, 116, 158)),
                (Light: Rgb(84, 80, 140), Dark: Rgb(6, 6, 20)),
                (Light: Rgb(255, 176, 232), Dark: Rgb(142, 0, 104)),
            };
            var outline = Rgb(12, 10, 30);
            _flagPix.Clear();

            for (int x = 0; x < FlagW; x++)
            {
                var k = x / (double)(FlagW - 1);
                var attach = _classic ? 1.0 : Math.Min(1.0, x / 12.0);
                var amp = (1.0 + 4.6 * Math.Pow(k, 1.1)) * attach;
                var phase = x * 0.13 - t * 3.4;
                var wave = Math.Sin(phase) * amp + Math.Sin(x * 0.055 - t * 1.8) * 1.6 * attach;
                var slope = Math.Cos(phase) * attach;                // -1..1
                var shade = Math.Clamp(0.55 + 0.45 * slope, 0, 1);    // 0 = shadow fold, 1 = lit fold
                var squash = 1 - 0.07 * Math.Abs(slope);
                var yo = (int)Math.Round(wave) + FlagPad;
                var notch = x > FlagW - 16 ? (x - (FlagW - 16)) / 16.0 * FlagH * 0.26 : 0;

                for (int r = 0; r < FlagH; r++)
                {
                    if (notch > 0 && Math.Abs(r - (FlagH - 1) / 2.0) < notch) continue;
                    var band = bands[r < 15 ? 0 : (r < 31 ? 1 : 2)];
                    var c = Mix(band.Dark, band.Light, Quantise(shade, 7, x, r));
                    if (TextPixel(x, r))
                        c = Mix(Rgb(150, 160, 192), Rgb(255, 255, 255), Quantise(shade, 5, x, r));
                    else if (r == 0 || r == 15 || r == 31)
                        c = Mix(c, Rgb(255, 255, 255), 0.18);        // faint seam between bands
                    _flagPix.Set(x + FlagPad, yo + (int)Math.Round(r * squash), c);
                }
                _flagPix.Set(x + FlagPad, yo - 1, outline);
                _flagPix.Set(x + FlagPad, yo + (int)Math.Round(FlagH * squash), outline);
            }

            // two little grommets where the cloth is tied to the pole
            if (!_classic)
            {
                _flagPix.Set(FlagPad, FlagPad + 8, Rgb(255, 224, 120));
                _flagPix.Set(FlagPad, FlagPad + FlagH - 9, Rgb(255, 224, 120));
            }

            _flagPix.CopyTo(_flagBmp);
            _g.DrawImage(_flagBmp, poleX + 1 - FlagPad, flagY - FlagPad, _flagBmp.Width, _flagBmp.Height);
        }

        // ---------------------------------------------------------------- progress bar

        private const int BarRows = 7;

        // A full-width strip along the very bottom with a smooth, dithered colour
        // gradient that slides along as it fills - in the spirit of the old Windows 9x
        // start-up bar, but in original colours. A soft highlight sweeps across the
        // filled part so it never looks stuck.
        private void DrawProgressBar(double progress, double t)
        {
            var stops = new (double, int)[]
            {
                (0.00, Rgb(0, 214, 235)), (0.28, Rgb(30, 110, 255)), (0.55, Rgb(150, 60, 240)),
                (0.80, Rgb(255, 0, 170)), (1.00, Rgb(0, 214, 235)),
            };
            double[] rowLight = { 1.0, 1.5, 1.2, 1.0, 0.82, 0.66, 0.5 };
            var fillW = (int)Math.Round(Math.Clamp(progress, 0, 1) * _w);
            var sweep = (t * 0.55 % 1.5) * Math.Max(1, fillW) - 12;

            _barPix.Clear();
            for (int x = 0; x < _w; x++)
            {
                var u = (x / (double)_w - t * 0.30) % 1.0;
                if (u < 0) u += 1;
                for (int r = 0; r < BarRows; r++)
                {
                    int c;
                    if (x < fillW)
                    {
                        c = Shade(Ramp(stops, Quantise(u, 56, x, r)), rowLight[r]);
                        var d = Math.Abs(x - sweep);
                        if (d < 14) c = Mix(c, Rgb(255, 255, 255), 0.45 * (1 - d / 14));
                        if (x >= fillW - 2) c = Mix(c, Rgb(255, 255, 255), 0.7);   // bright leading edge
                    }
                    else
                    {
                        c = r == 0 ? Rgb(40, 52, 96) : Mix(Rgb(18, 26, 60), Rgb(4, 6, 20), r / (double)(BarRows - 1));
                    }
                    _barPix.Set(x, r, c);
                }
            }
            _barPix.CopyTo(_barBmp);
            _g.DrawImage(_barBmp, 0, _h - BarRows, _w, BarRows);
        }

        public void Dispose()
        {
            _g.Dispose();
            _buf.Dispose();
            _backdrop.Dispose();
            foreach (var l in _layers) l.Bmp.Dispose();
            foreach (var c in _clouds) c.Dispose();
            _flagBmp.Dispose();
            _barBmp.Dispose();
            _wordmark?.Dispose();
            _footer?.Dispose();
        }
    }
}
