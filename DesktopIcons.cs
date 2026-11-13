using System.Drawing.Drawing2D;
using static GamingStackGUI.Px;

namespace GamingStackGUI
{
    /// <summary>
    /// Pixel-art desktop icons for the three setup types (Gaming, Office, Developer)
    /// and the little chip logo on the Start button. 24x24 art pixels each, drawn from
    /// scratch and scaled up with whole-number nearest-neighbour sampling so they stay
    /// crisp. Nothing here is copied from Windows or any other product.
    /// </summary>
    internal sealed class DesktopIcons : IDisposable
    {
        public const int ArtSize = 24;

        public readonly Bitmap Gaming, Office, Developer, StartLogo, Computer, Trophy, Arcade, Recycle, Notes, Paint, Defrag, Cmd, Display;

        public DesktopIcons()
        {
            Gaming = BuildGaming().ToBitmap();
            Office = BuildOffice().ToBitmap();
            Developer = BuildDeveloper().ToBitmap();
            StartLogo = BuildStartLogo().ToBitmap();
            Computer = BuildComputer().ToBitmap();
            Trophy = BuildTrophy().ToBitmap();
            Arcade = BuildArcade().ToBitmap();
            Recycle = BuildRecycle().ToBitmap();
            Notes = BuildNotes().ToBitmap();
            Paint = BuildPaint().ToBitmap();
            Defrag = BuildDefrag().ToBitmap();
            Cmd = BuildCmd().ToBitmap();
            Display = BuildDisplay().ToBitmap();
        }

        private static readonly int Outline = Rgb(16, 18, 34);

        private static PixBuf BuildGaming()
        {
            var p = new PixBuf(ArtSize, ArtSize);
            // pad body: rounded slab with two grips
            p.DiscShaded(6, 15, 5, Rgb(150, 156, 184), Rgb(46, 50, 74));
            p.DiscShaded(17, 15, 5, Rgb(150, 156, 184), Rgb(46, 50, 74));
            p.Bevel(5, 7, 14, 10, Rgb(110, 116, 146), Rgb(190, 198, 224), Rgb(34, 36, 56));
            // d-pad
            p.Rect(6, 11, 5, 2, Rgb(20, 22, 34)); p.Rect(7, 10, 3, 4, Rgb(20, 22, 34));
            // four coloured buttons
            p.Disc(16, 10, 1, Rgb(0, 224, 240)); p.Disc(14, 12, 1, Rgb(255, 120, 210));
            p.Disc(18, 12, 1, Rgb(255, 255, 255)); p.Disc(16, 14, 1, Rgb(255, 150, 40));
            // shoulder buttons and cable
            p.Rect(5, 5, 4, 2, Rgb(80, 86, 112)); p.Rect(15, 5, 4, 2, Rgb(80, 86, 112));
            for (int i = 0; i < 5; i++) p.Set(12, 6 - i, Rgb(40, 40, 50));
            Outline1(p);
            return p;
        }

        private static PixBuf BuildOffice()
        {
            var p = new PixBuf(ArtSize, ArtSize);
            // back sheet, then the front page with a folded corner
            p.Bevel(5, 2, 14, 19, Rgb(214, 224, 244), Rgb(250, 252, 255), Rgb(120, 136, 180));
            p.Bevel(7, 4, 14, 19, Rgb(255, 255, 255), Rgb(255, 255, 255), Rgb(150, 160, 190));
            p.Rect(17, 4, 4, 4, Rgb(190, 200, 226));
            p.Rect(17, 4, 1, 4, Rgb(150, 160, 190)); p.Rect(17, 7, 4, 1, Rgb(150, 160, 190));
            // blue header and text lines
            p.Rect(9, 6, 7, 3, Rgb(40, 100, 220));
            for (int i = 0; i < 5; i++) p.Rect(9, 11 + i * 2, i == 4 ? 6 : 10, 1, Rgb(120, 130, 160));
            // a pencil leaning on it
            for (int i = 0; i < 9; i++) { p.Set(3 + i / 2, 20 - i, Rgb(255, 200, 50)); p.Set(4 + i / 2, 20 - i, Rgb(220, 150, 20)); }
            p.Set(2, 21, Rgb(60, 40, 30)); p.Set(3, 21, Rgb(240, 190, 150));
            Outline1(p);
            return p;
        }

        private static PixBuf BuildDeveloper()
        {
            var p = new PixBuf(ArtSize, ArtSize);
            // terminal window: title bar with three dots, dark body, green prompt
            p.Bevel(1, 3, 22, 18, Rgb(18, 22, 40), Rgb(90, 100, 150), Rgb(6, 8, 18));
            p.Rect(2, 4, 20, 4, Rgb(0, 160, 200));
            p.Rect(2, 4, 20, 1, Rgb(120, 240, 255));
            p.Set(4, 6, Rgb(255, 90, 90)); p.Set(6, 6, Rgb(255, 210, 70)); p.Set(8, 6, Rgb(90, 230, 120));
            // ">_" prompt and a few code lines
            p.Line(4, 11, 7, 13, Rgb(90, 255, 130)); p.Line(7, 13, 4, 15, Rgb(90, 255, 130));
            p.Rect(9, 15, 5, 1, Rgb(90, 255, 130));
            p.Rect(10, 11, 8, 1, Rgb(150, 160, 220)); p.Rect(10, 13, 5, 1, Rgb(255, 130, 200));
            p.Rect(4, 18, 12, 1, Rgb(60, 70, 120));
            Outline1(p);
            return p;
        }


        private static PixBuf BuildComputer()
        {
            var p = new PixBuf(ArtSize, ArtSize);
            // CRT monitor on a stand, with a glowing screen
            p.Bevel(2, 2, 20, 15, Rgb(214, 204, 178), Rgb(244, 238, 216), Rgb(130, 120, 96));
            p.Rect(4, 4, 16, 11, Rgb(12, 16, 28));
            p.VGrad(5, 5, 14, 9, Rgb(70, 160, 255), Rgb(190, 230, 255), 5);
            p.Rect(6, 11, 12, 3, Rgb(60, 170, 80));
            p.Rect(9, 17, 6, 2, Rgb(190, 180, 154));
            p.Bevel(5, 19, 14, 3, Rgb(214, 204, 178), Rgb(244, 238, 216), Rgb(130, 120, 96));
            p.Set(19, 15, Rgb(80, 255, 110));
            Outline1(p);
            return p;
        }

        private static PixBuf BuildTrophy()
        {
            var p = new PixBuf(ArtSize, ArtSize);
            var gold = Rgb(255, 214, 70); var hi = Rgb(255, 246, 170); var lo = Rgb(190, 120, 20);
            // cup, handles, stem and base
            p.VGrad(7, 3, 10, 9, hi, lo, 5);
            p.Rect(8, 12, 8, 2, lo);
            p.Rect(3, 4, 4, 2, gold); p.Rect(3, 4, 1, 6, gold); p.Rect(3, 9, 4, 1, gold);
            p.Rect(17, 4, 4, 2, gold); p.Rect(20, 4, 1, 6, gold); p.Rect(17, 9, 4, 1, gold);
            p.Rect(11, 14, 2, 4, gold);
            p.Bevel(7, 18, 10, 4, Rgb(120, 70, 30), Rgb(190, 130, 70), Rgb(60, 30, 10));
            p.Rect(10, 21, 4, 1, gold);
            // star on the cup
            p.Rect(11, 6, 2, 4, Rgb(255, 255, 255)); p.Rect(10, 7, 4, 2, Rgb(255, 255, 255));
            Outline1(p);
            return p;
        }

        private static PixBuf BuildArcade()
        {
            var p = new PixBuf(ArtSize, ArtSize);
            // arcade cabinet: marquee, screen, control panel with stick and buttons
            p.Bevel(5, 1, 14, 21, Rgb(60, 70, 190), Rgb(130, 150, 255), Rgb(20, 24, 90));
            p.Rect(6, 2, 12, 3, Rgb(255, 0, 170));
            p.Rect(8, 3, 8, 1, Rgb(255, 230, 250));
            p.Rect(7, 6, 10, 7, Rgb(8, 10, 20));
            p.VGrad(8, 7, 8, 5, Rgb(0, 200, 230), Rgb(120, 40, 220), 4);
            p.Rect(10, 9, 2, 2, Rgb(255, 255, 255));
            p.Rect(6, 14, 12, 4, Rgb(30, 34, 60));
            p.Disc(9, 15, 1, Rgb(255, 80, 80)); p.Rect(9, 14, 1, 1, Rgb(255, 200, 200));
            p.Disc(13, 16, 1, Rgb(255, 220, 60)); p.Disc(16, 16, 1, Rgb(80, 230, 110));
            p.Rect(8, 19, 8, 3, Rgb(20, 24, 70));
            Outline1(p);
            return p;
        }

        private static PixBuf BuildRecycle()
        {
            var p = new PixBuf(ArtSize, ArtSize);
            // wire bin with a lid and a green recycle triangle
            p.Rect(5, 4, 14, 2, Rgb(150, 156, 170)); p.Rect(10, 2, 4, 2, Rgb(150, 156, 170));
            for (int y = 7; y < 22; y++)
            {
                var inset = (y - 7) / 7;
                p.Rect(6 + inset, y, 12 - inset * 2, 1, ((y % 3) == 0) ? Rgb(120, 132, 150) : Rgb(186, 196, 214));
            }
            for (int x = 8; x < 17; x += 3) p.Rect(x, 7, 1, 15, Rgb(110, 120, 140));
            p.Line(12, 10, 9, 15, Rgb(40, 190, 90)); p.Line(12, 10, 15, 15, Rgb(40, 190, 90)); p.Line(9, 15, 15, 15, Rgb(40, 190, 90));
            Outline1(p);
            return p;
        }


        private static PixBuf BuildNotes()
        {
            var p = new PixBuf(ArtSize, ArtSize);
            p.Bevel(4, 3, 16, 19, Rgb(255, 240, 150), Rgb(255, 252, 210), Rgb(190, 160, 50));
            p.Rect(4, 3, 16, 4, Rgb(210, 70, 70));
            for (int i = 0; i < 4; i++) p.Rect(7 + i * 3, 1, 1, 4, Rgb(60, 60, 70));
            for (int i = 0; i < 5; i++) p.Rect(7, 10 + i * 2, 11 - (i == 4 ? 4 : 0), 1, Rgb(120, 110, 60));
            Outline1(p);
            return p;
        }

        private static PixBuf BuildPaint()
        {
            var p = new PixBuf(ArtSize, ArtSize);
            p.DiscShaded(11, 12, 9, Rgb(224, 170, 110), Rgb(120, 70, 30));
            p.Disc(15, 15, 2, Rgb(0, 0, 0));   // thumb hole
            p.Disc(7, 8, 2, Rgb(255, 60, 60)); p.Disc(12, 5, 2, Rgb(255, 220, 40));
            p.Disc(17, 8, 2, Rgb(60, 200, 90)); p.Disc(6, 14, 2, Rgb(60, 120, 255));
            for (int i = 0; i < 8; i++) p.Set(15 + i / 2, 20 - i, Rgb(150, 90, 40));   // brush
            p.Rect(20, 12, 2, 2, Rgb(255, 0, 170));
            Outline1(p);
            return p;
        }

        private static PixBuf BuildDefrag()
        {
            var p = new PixBuf(ArtSize, ArtSize);
            p.Bevel(2, 4, 20, 16, Rgb(210, 210, 220), Rgb(250, 250, 255), Rgb(100, 100, 120));
            var cols = new[] { Rgb(40, 130, 255), Rgb(60, 210, 90), Rgb(255, 210, 40), Rgb(255, 90, 60) };
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 6; x++)
                    p.Rect(4 + x * 3, 6 + y * 4, 2, 3, cols[(x + y * 2) % 4]);
            p.Rect(4, 18, 16, 1, Rgb(80, 80, 100));
            Outline1(p);
            return p;
        }

        private static PixBuf BuildCmd()
        {
            var p = new PixBuf(ArtSize, ArtSize);
            p.Bevel(1, 3, 22, 18, Rgb(10, 10, 14), Rgb(120, 120, 130), Rgb(0, 0, 0));
            p.Rect(2, 4, 20, 3, Rgb(180, 180, 190));
            p.Rect(18, 5, 3, 1, Rgb(60, 60, 70));
            p.Text(3, 9, "C", 1, Rgb(220, 220, 220));
            p.Line(10, 10, 13, 12, Rgb(220, 220, 220)); p.Line(13, 12, 10, 14, Rgb(220, 220, 220));
            p.Rect(15, 14, 4, 1, Rgb(220, 220, 220));
            Outline1(p);
            return p;
        }

        private static PixBuf BuildDisplay()
        {
            var p = new PixBuf(ArtSize, ArtSize);
            p.Bevel(1, 3, 22, 15, Rgb(214, 204, 178), Rgb(244, 238, 216), Rgb(130, 120, 96));
            p.VGrad(3, 5, 18, 11, Rgb(80, 150, 255), Rgb(190, 230, 255), 5);
            p.Disc(16, 8, 2, Rgb(255, 230, 80));
            for (int i = 0; i < 18; i++) p.Set(3 + i, 15 - (int)(Math.Abs(Math.Sin(i * 0.5)) * 3), Rgb(60, 170, 80));
            p.Rect(9, 18, 6, 2, Rgb(190, 180, 154)); p.Rect(6, 20, 12, 2, Rgb(214, 204, 178));
            Outline1(p);
            return p;
        }

        private static PixBuf BuildStartLogo()
        {
            var p = new PixBuf(12, 12);
            for (int i = 0; i < 4; i++)
            {
                var o = 2 + i * 2;
                p.Set(o, 0, Rgb(230, 190, 70)); p.Set(o, 11, Rgb(230, 190, 70));
                p.Set(0, o, Rgb(230, 190, 70)); p.Set(11, o, Rgb(230, 190, 70));
            }
            p.Bevel(1, 1, 10, 10, Rgb(70, 80, 120), Rgb(170, 184, 226), Rgb(20, 24, 50));
            p.VGrad(3, 3, 6, 6, Rgb(0, 224, 245), Rgb(255, 0, 170), 6);
            return p;
        }

        // Dark 1px outline around everything drawn (pixels that are empty but touch a
        // drawn one) - the classic way icons were made readable on any wallpaper.
        private static void Outline1(PixBuf p)
        {
            var edge = new List<(int, int)>();
            for (int y = 0; y < p.H; y++)
                for (int x = 0; x < p.W; x++)
                {
                    if (p.Get(x, y) != 0) continue;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if ((dx != 0 || dy != 0) && p.Get(x + dx, y + dy) != 0 && p.Get(x + dx, y + dy) != Outline)
                            { edge.Add((x, y)); dy = 2; break; }
                }
            foreach (var (x, y) in edge) p.Set(x, y, Outline);
        }

        /// <summary>Draws one icon at (x, y), scaled by a whole number.</summary>
        public static void Draw(Graphics g, Bitmap art, int x, int y, int scale)
        {
            var prevI = g.InterpolationMode; var prevO = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(art, new Rectangle(x, y, art.Width * scale, art.Height * scale), 0, 0, art.Width, art.Height, GraphicsUnit.Pixel);
            g.InterpolationMode = prevI; g.PixelOffsetMode = prevO;
        }

        public void Dispose()
        {
            Gaming.Dispose(); Office.Dispose(); Developer.Dispose(); StartLogo.Dispose();
            Computer.Dispose(); Trophy.Dispose(); Arcade.Dispose(); Recycle.Dispose();
            Notes.Dispose(); Paint.Dispose(); Defrag.Dispose(); Cmd.Dispose(); Display.Dispose();
        }
    }
}
