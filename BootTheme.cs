using static GamingStackGUI.Px;

namespace GamingStackGUI
{
    /// <summary>Lit side, shaded side and distance-haze colours for one landscape layer.</summary>
    internal readonly record struct LayerColors(int Lit, int Shade, int Haze);

    /// <summary>
    /// The colour scheme of the boot scene. One is picked from the PC's clock each time
    /// the app starts, so the boot screen matches the time of day:
    /// dawn (5-8), day (8-17), evening (17-21) or night (21-5).
    /// Set the environment variable GAMINGSTACK_BOOT_THEME to dawn, day, evening or
    /// night to force one (handy for trying them all).
    /// </summary>
    internal sealed record BootTheme(
        string Name,
        (double, int)[] Sky,
        int Glow, double GlowStrength,
        (double, int)[] Sun, bool SunStripes, bool Moon, double SunX, int SunY,
        double Stars, int StarReach,
        LayerColors FarMountains, LayerColors MidMountains, LayerColors FarHills, LayerColors NearHills,
        int SnowLit, int SnowShade, int Rim,
        int CloudLight, int CloudShade,
        int TreeDark, int TreeMid, int TreeLight,
        int Bird)
    {
        public static BootTheme Evening { get; } = new(
            "evening",
            new (double, int)[]
            {
                (0.00, Rgb(12, 14, 58)), (0.22, Rgb(32, 52, 142)), (0.48, Rgb(66, 126, 210)),
                (0.70, Rgb(132, 196, 238)), (0.88, Rgb(236, 220, 206)), (1.00, Rgb(255, 186, 140)),
            },
            Rgb(255, 232, 170), 0.62,
            new (double, int)[]
            {
                (0.0, Rgb(255, 250, 206)), (0.4, Rgb(255, 214, 110)), (0.72, Rgb(255, 140, 110)), (1.0, Rgb(236, 84, 140)),
            },
            true, false, 0.77, 150,
            1.0, 70,
            new(Rgb(158, 176, 230), Rgb(92, 106, 172), Rgb(222, 214, 232)),
            new(Rgb(112, 150, 184), Rgb(60, 92, 142), Rgb(196, 214, 226)),
            new(Rgb(88, 168, 112), Rgb(34, 96, 92), Rgb(120, 186, 170)),
            new(Rgb(78, 160, 84), Rgb(26, 84, 64), Rgb(10, 24, 42)),
            Rgb(246, 248, 255), Rgb(188, 204, 236), Rgb(255, 244, 214),
            Rgb(255, 255, 255), Rgb(156, 176, 222),
            Rgb(16, 62, 56), Rgb(30, 100, 70), Rgb(76, 156, 92),
            Rgb(34, 42, 74));

        public static BootTheme Day { get; } = new(
            "day",
            new (double, int)[]
            {
                (0.00, Rgb(30, 100, 214)), (0.40, Rgb(72, 150, 238)), (0.75, Rgb(150, 208, 250)), (1.00, Rgb(222, 242, 252)),
            },
            Rgb(255, 252, 220), 0.42,
            new (double, int)[] { (0.0, Rgb(255, 255, 238)), (0.6, Rgb(255, 244, 180)), (1.0, Rgb(255, 224, 120)) },
            false, false, 0.72, 62,
            0.0, 0,
            new(Rgb(176, 196, 236), Rgb(108, 130, 190), Rgb(224, 236, 250)),
            new(Rgb(96, 158, 172), Rgb(52, 106, 136), Rgb(196, 226, 232)),
            new(Rgb(110, 190, 96), Rgb(48, 124, 72), Rgb(160, 214, 170)),
            new(Rgb(118, 204, 76), Rgb(44, 128, 52), Rgb(10, 34, 34)),
            Rgb(255, 255, 255), Rgb(196, 212, 240), Rgb(255, 255, 230),
            Rgb(255, 255, 255), Rgb(176, 198, 236),
            Rgb(20, 80, 56), Rgb(40, 124, 70), Rgb(100, 184, 90),
            Rgb(40, 50, 80));

        public static BootTheme Dawn { get; } = new(
            "dawn",
            new (double, int)[]
            {
                (0.00, Rgb(56, 44, 112)), (0.30, Rgb(132, 78, 150)), (0.60, Rgb(238, 128, 140)),
                (0.85, Rgb(255, 190, 140)), (1.00, Rgb(255, 224, 170)),
            },
            Rgb(255, 180, 130), 0.60,
            new (double, int)[] { (0.0, Rgb(255, 236, 190)), (0.5, Rgb(255, 170, 110)), (1.0, Rgb(255, 110, 110)) },
            false, false, 0.30, 156,
            0.35, 50,
            new(Rgb(232, 170, 190), Rgb(118, 86, 150), Rgb(255, 200, 180)),
            new(Rgb(184, 130, 170), Rgb(84, 66, 132), Rgb(240, 180, 170)),
            new(Rgb(130, 170, 110), Rgb(56, 90, 92), Rgb(200, 180, 150)),
            new(Rgb(100, 160, 84), Rgb(34, 82, 70), Rgb(16, 24, 44)),
            Rgb(255, 236, 236), Rgb(200, 170, 210), Rgb(255, 210, 170),
            Rgb(255, 226, 214), Rgb(206, 140, 176),
            Rgb(24, 62, 60), Rgb(40, 96, 76), Rgb(112, 150, 100),
            Rgb(60, 40, 80));

        public static BootTheme Night { get; } = new(
            "night",
            new (double, int)[]
            {
                (0.00, Rgb(4, 6, 30)), (0.30, Rgb(10, 18, 76)), (0.60, Rgb(24, 40, 122)),
                (0.85, Rgb(44, 70, 150)), (1.00, Rgb(80, 104, 176)),
            },
            Rgb(170, 190, 255), 0.50,
            new (double, int)[] { (0.0, Rgb(252, 253, 255)), (0.6, Rgb(220, 228, 248)), (1.0, Rgb(176, 190, 232)) },
            false, true, 0.24, 74,
            1.0, 175,
            new(Rgb(92, 112, 176), Rgb(34, 44, 104), Rgb(70, 92, 160)),
            new(Rgb(60, 86, 140), Rgb(26, 40, 96), Rgb(54, 72, 134)),
            new(Rgb(40, 100, 96), Rgb(16, 46, 64), Rgb(34, 70, 110)),
            new(Rgb(40, 96, 76), Rgb(14, 44, 52), Rgb(6, 12, 28)),
            Rgb(214, 224, 255), Rgb(120, 136, 200), Rgb(190, 210, 255),
            Rgb(130, 144, 210), Rgb(54, 64, 128),
            Rgb(8, 30, 40), Rgb(16, 56, 56), Rgb(40, 100, 80),
            Rgb(170, 180, 226));

        /// <summary>Picks the theme for the current local time (or the override).</summary>
        public static BootTheme ForNow()
        {
            var forced = Environment.GetEnvironmentVariable("GAMINGSTACK_BOOT_THEME")?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(forced)) forced = ProgressStore.BootTheme?.Trim().ToLowerInvariant();   // Display Properties
            switch (forced)
            {
                case "dawn": return Dawn;
                case "day": return Day;
                case "evening": return Evening;
                case "night": return Night;
            }
            var hour = DateTime.Now.Hour;
            return hour switch
            {
                >= 5 and < 8 => Dawn,
                >= 8 and < 17 => Day,
                >= 17 and < 21 => Evening,
                _ => Night
            };
        }
    }
}
