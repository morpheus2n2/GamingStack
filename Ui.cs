namespace GamingStackGUI
{
    /// <summary>
    /// Fonts for the whole app, sized in PIXELS rather than points.
    /// Why: a font in points is drawn bigger when Windows display scaling is on (a 14pt
    /// font is ~19px at 100% but ~28px at 150%), while all the window/layout maths in this
    /// app is in pixels - which is why text overlapped and ran off screen on scaled 4K
    /// displays. Pixel fonts keep text and layout in proportion; the shell then applies
    /// the Windows scale factor to both together (see UiScale in MainForm.Shell.cs).
    /// The point sizes passed in are the same ones the layout was designed around.
    /// </summary>
    internal static class Ui
    {
        public static Font Font(float points, FontStyle style = FontStyle.Regular) =>
            new("Consolas", points * 96f / 72f, style, GraphicsUnit.Pixel);
    }
}
