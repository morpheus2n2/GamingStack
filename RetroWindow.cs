namespace GamingStackGUI
{
    /// <summary>
    /// One window on the fake desktop. Everything in GamingStack is drawn on a single
    /// canvas, so a "window" is just a rectangle plus a few callbacks; the shell
    /// (MainForm.Shell.cs) draws the title bar, buttons and frame, handles moving,
    /// resizing, minimising and maximising, and calls the callbacks for the content.
    /// </summary>
    internal sealed class RetroWindow
    {
        public required string Id { get; init; }
        public string Title { get; set; } = "";
        public Bitmap? Icon { get; init; }

        public Rectangle Bounds { get; set; }
        public Rectangle RestoreBounds { get; set; }
        public bool Minimized { get; set; }
        public bool Maximized { get; set; }
        public bool CanResize { get; init; } = true;
        public Size MinSize { get; init; } = new(300, 200);
        public Color Background { get; init; } = Color.White;

        /// <summary>When the window opened (Environment.TickCount64), for the grow-in animation.</summary>
        public long OpenedAt { get; set; }
        public int OpenAnimMs { get; init; } = 220;
        /// <summary>Order the window was opened in - taskbar buttons stay in this order.</summary>
        public int Sequence { get; set; }

        /// <summary>Draws the window's content inside the client rectangle.</summary>
        public Action<Graphics, Rectangle>? DrawClient { get; init; }
        /// <summary>Returns true if the key was used (so Esc etc. does not also quit the app).</summary>
        public Func<Keys, bool>? OnKey { get; init; }
        /// <summary>A left click inside the client area, in client-relative coordinates.</summary>
        public Action<Point>? OnClientClick { get; init; }
        /// <summary>A right click inside the client area (client-relative).</summary>
        public Action<Point>? OnClientRightClick { get; init; }
        /// <summary>The mouse moved while the left button is held down after a client click.</summary>
        public Action<Point>? OnClientDrag { get; init; }
        /// <summary>The left button was released after a client click.</summary>
        public Action? OnClientUp { get; init; }
        /// <summary>A typed character (letters, digits, Enter, Backspace as control chars).</summary>
        public Action<char>? OnChar { get; init; }
        /// <summary>Return false to refuse closing (e.g. the installer while it is busy).</summary>
        public Func<bool>? CanClose { get; init; }
        public Action? OnClosed { get; init; }
        /// <summary>Called ~30 times a second while the window is open and not minimised.</summary>
        public Action<long>? OnTick { get; init; }
    }
}
