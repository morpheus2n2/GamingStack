using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GamingStackGUI
{
    /// <summary>
    /// Small colour/dither helpers shared by every pixel-art scene (boot scene, BIOS
    /// logo, desktop wallpaper). Colours are plain opaque ARGB ints; 0 means
    /// "transparent / not drawn".
    /// </summary>
    internal static class Px
    {
        public static int Rgb(int r, int g, int b) =>
            unchecked((int)0xFF000000u) | ((r & 255) << 16) | ((g & 255) << 8) | (b & 255);
        public static int Rc(int c) => (c >> 16) & 255;
        public static int Gc(int c) => (c >> 8) & 255;
        public static int Bc(int c) => c & 255;

        public static int Mix(int a, int b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Rgb((int)Math.Round(Rc(a) + (Rc(b) - Rc(a)) * t),
                       (int)Math.Round(Gc(a) + (Gc(b) - Gc(a)) * t),
                       (int)Math.Round(Bc(a) + (Bc(b) - Bc(a)) * t));
        }

        /// <summary>Brightens (f &gt; 1) or darkens (f &lt; 1) a colour.</summary>
        public static int Shade(int c, double f) =>
            Rgb((int)Math.Clamp(Rc(c) * f, 0, 255), (int)Math.Clamp(Gc(c) * f, 0, 255), (int)Math.Clamp(Bc(c) * f, 0, 255));

        private static readonly int[,] Bayer4 =
        {
            { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 }
        };

        public static double Dither(int x, int y) => (Bayer4[y & 3, x & 3] + 0.5) / 16.0;

        /// <summary>Snaps t (0..1) to one of <paramref name="levels"/> steps using an
        /// ordered dither, so step edges become a fine checker pattern - the classic
        /// 16-bit way of faking more colours than the palette has.</summary>
        public static double Quantise(double t, int levels, int x, int y)
        {
            t = Math.Clamp(t, 0, 1);
            var v = t * levels;
            var b = Math.Floor(v);
            if (v - b > Dither(x, y)) b++;
            return Math.Min(b, levels) / levels;
        }

        public static int Ramp((double T, int Color)[] stops, double t)
        {
            if (t <= stops[0].T) return stops[0].Color;
            for (int i = 1; i < stops.Length; i++)
            {
                if (t <= stops[i].T)
                {
                    var span = stops[i].T - stops[i - 1].T;
                    return Mix(stops[i - 1].Color, stops[i].Color, span <= 0 ? 0 : (t - stops[i - 1].T) / span);
                }
            }
            return stops[^1].Color;
        }
    }

    /// <summary>
    /// A 5x7 pixel font (A-Z, 0-9 and a little punctuation) for lettering inside
    /// pixel-art scenes. Original glyphs, drawn from scratch.
    /// </summary>
    internal static class PixelFont
    {
        public const int GlyphW = 5, GlyphH = 7;

        private static readonly Dictionary<char, string> Glyphs = Build();

        private static Dictionary<char, string> Build()
        {
            // Each glyph is 7 rows of 5 characters, '#' = lit, joined into one string.
            var d = new Dictionary<char, string>();
            void G(char c, params string[] rows) => d[c] = string.Concat(rows);
            G('A', ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#");
            G('B', "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####.");
            G('C', ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###.");
            G('D', "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####.");
            G('E', "#####", "#....", "#....", "####.", "#....", "#....", "#####");
            G('F', "#####", "#....", "#....", "####.", "#....", "#....", "#....");
            G('G', ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###.");
            G('H', "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#");
            G('I', ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###.");
            G('J', "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##..");
            G('K', "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#");
            G('L', "#....", "#....", "#....", "#....", "#....", "#....", "#####");
            G('M', "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#");
            G('N', "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#");
            G('O', ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
            G('P', "####.", "#...#", "#...#", "####.", "#....", "#....", "#....");
            G('Q', ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#");
            G('R', "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#");
            G('S', ".####", "#....", "#....", ".###.", "....#", "....#", "####.");
            G('T', "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#..");
            G('U', "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
            G('V', "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#..");
            G('W', "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#");
            G('X', "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#");
            G('Y', "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#..");
            G('Z', "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####");
            G('0', ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###.");
            G('1', "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###.");
            G('2', ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####");
            G('3', "####.", "....#", "....#", ".###.", "....#", "....#", "####.");
            G('4', "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#.");
            G('5', "#####", "#....", "####.", "....#", "....#", "#...#", ".###.");
            G('6', ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###.");
            G('7', "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#...");
            G('8', ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###.");
            G('9', ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###.");
            G(' ', ".....", ".....", ".....", ".....", ".....", ".....", ".....");
            G('.', ".....", ".....", ".....", ".....", ".....", ".##..", ".##..");
            G(',', ".....", ".....", ".....", ".....", ".##..", "..#..", ".#...");
            G('-', ".....", ".....", ".....", "#####", ".....", ".....", ".....");
            G(':', ".....", ".##..", ".##..", ".....", ".##..", ".##..", ".....");
            G('!', "..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#..");
            G('/', "....#", "....#", "...#.", "..#..", ".#...", "#....", "#....");
            G('+', ".....", "..#..", "..#..", "#####", "..#..", "..#..", ".....");
            G('(', "...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#.");
            G(')', ".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#...");
            G('\'', "..#..", "..#..", ".#...", ".....", ".....", ".....", ".....");
            G('%', "##..#", "##..#", "...#.", "..#..", ".#...", "#..##", "#..##");
            G('&', ".##..", "#..#.", "#.#..", ".#...", "#.#.#", "#..#.", ".##.#");
            return d;
        }


        public static bool Lit(char c, int gx, int gy)
        {
            if (!Glyphs.TryGetValue(char.ToUpperInvariant(c), out var rows)) return false;
            return rows[gy * GlyphW + gx] == '#';
        }

        public static int Width(string text, int scale, int gap = 1) =>
            text.Length == 0 ? 0 : text.Length * (GlyphW * scale + gap) - gap;
    }

    /// <summary>
    /// A fixed-size grid of opaque pixels with drawing helpers. Everything the pixel
    /// scenes draw goes through this, then gets scaled up with nearest-neighbour
    /// sampling so each pixel becomes a crisp square.
    /// </summary>
    internal sealed class PixBuf
    {
        public readonly int W, H;
        public readonly int[] P;

        public PixBuf(int w, int h) { W = w; H = h; P = new int[w * h]; }

        public void Set(int x, int y, int argb)
        {
            if ((uint)x < (uint)W && (uint)y < (uint)H) P[y * W + x] = argb;
        }

        public int Get(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H ? P[y * W + x] : 0;

        // Wraps horizontally - used by scrolling strips so they tile seamlessly.
        public void SetWrap(int x, int y, int argb)
        {
            if ((uint)y >= (uint)H) return;
            x = ((x % W) + W) % W;
            P[y * W + x] = argb;
        }

        public void Clear() => Array.Clear(P, 0, P.Length);

        public void CopyTo(Bitmap bmp)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(P, 0, data.Scan0, P.Length); }
            finally { bmp.UnlockBits(data); }
        }

        public Bitmap ToBitmap()
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
            CopyTo(bmp);
            return bmp;
        }

        // ---- shapes ----

        public void Rect(int x, int y, int w, int h, int c)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    Set(xx, yy, c);
        }

        public void Frame(int x, int y, int w, int h, int c)
        {
            Rect(x, y, w, 1, c); Rect(x, y + h - 1, w, 1, c);
            Rect(x, y, 1, h, c); Rect(x + w - 1, y, 1, h, c);
        }

        /// <summary>Filled rectangle with a lit top/left edge and shadowed bottom/right edge.</summary>
        public void Bevel(int x, int y, int w, int h, int face, int light, int dark)
        {
            Rect(x, y, w, h, face);
            Rect(x, y, w, 1, light); Rect(x, y, 1, h, light);
            Rect(x, y + h - 1, w, 1, dark); Rect(x + w - 1, y, 1, h, dark);
        }

        /// <summary>Vertical gradient with ordered dithering.</summary>
        public void VGrad(int x, int y, int w, int h, int top, int bottom, int levels = 8)
        {
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                    Set(x + xx, y + yy, Px.Mix(top, bottom, Px.Quantise(h <= 1 ? 0 : yy / (double)(h - 1), levels, x + xx, y + yy)));
        }

        public void Disc(int cx, int cy, int r, int c)
        {
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                    if (x * x + y * y <= r * r + r / 2) Set(cx + x, cy + y, c);
        }

        /// <summary>Sphere-like disc lit from the top-left, dithered between light and dark.</summary>
        public void DiscShaded(int cx, int cy, int r, int light, int dark)
        {
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    if (x * x + y * y > r * r + r / 2) continue;
                    var lit = 1 - Math.Clamp(((x + r * 0.35) * (x + r * 0.35) + (y + r * 0.35) * (y + r * 0.35)) / (2.2 * r * r), 0, 1);
                    Set(cx + x, cy + y, Px.Mix(dark, light, Px.Quantise(lit, 4, cx + x, cy + y)));
                }
        }

        public void Line(int x0, int y0, int x1, int y1, int c)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                Set(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Draws pixel-font text. Returns the width drawn.</summary>
        public int Text(int x, int y, string text, int scale, int color, int gap = 1)
        {
            var cx = x;
            foreach (var ch in text)
            {
                for (int gy = 0; gy < PixelFont.GlyphH; gy++)
                    for (int gx = 0; gx < PixelFont.GlyphW; gx++)
                        if (PixelFont.Lit(ch, gx, gy))
                            Rect(cx + gx * scale, y + gy * scale, scale, scale, color);
                cx += PixelFont.GlyphW * scale + gap;
            }
            return cx - x - gap;
        }

        /// <summary>Text with a 1px outline and an offset drop shadow.</summary>
        public void TextOutlined(int x, int y, string text, int scale, int color, int outline, int shadow = 0, int gap = 1)
        {
            if (shadow != 0) Text(x + scale, y + scale, text, scale, shadow, gap);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (dx != 0 || dy != 0) Text(x + dx * Math.Max(1, scale / 2), y + dy * Math.Max(1, scale / 2), text, scale, outline, gap);
            Text(x, y, text, scale, color, gap);
        }
    }
}
