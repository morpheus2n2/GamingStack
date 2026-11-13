using static GamingStackGUI.Px;

namespace GamingStackGUI
{
    /// <summary>
    /// A small Snake game for the Arcade window. Plain rules: eat apples to grow,
    /// don't hit the walls or yourself, it speeds up as you score. The logic is
    /// stepped from the shell's ~30 fps tick, and drawing is pure GDI+.
    /// </summary>
    internal sealed class SnakeGame
    {
        public enum State { Ready, Playing, Over }

        private const int Cols = 26, Rows = 17;
        private readonly Random _rng = new();
        private readonly List<Point> _body = new();
        private Point _dir = new(1, 0), _nextDir = new(1, 0);
        private Point _apple;
        private long _accum;
        private int _stepMs;
        private long _flashUntil;

        public State Mode { get; private set; } = State.Ready;
        public int Score { get; private set; }

        /// <summary>Raised on every apple eaten, with the new score.</summary>
        public event Action<int>? Scored;
        /// <summary>Raised once when a run ends, with the final score.</summary>
        public event Action<int>? Died;

        public void Start()
        {
            _body.Clear();
            for (int i = 0; i < 4; i++) _body.Add(new Point(8 - i, Rows / 2));
            _dir = _nextDir = new Point(1, 0);
            Score = 0;
            _stepMs = 150;
            _accum = 0;
            PlaceApple();
            Mode = State.Playing;
        }

        public void Turn(Keys key)
        {
            var d = key switch
            {
                Keys.Up or Keys.W => new Point(0, -1),
                Keys.Down or Keys.S => new Point(0, 1),
                Keys.Left or Keys.A => new Point(-1, 0),
                Keys.Right or Keys.D => new Point(1, 0),
                _ => Point.Empty
            };
            if (d == Point.Empty) return;
            if (d.X == -_dir.X && d.Y == -_dir.Y) return;     // no instant U-turn into yourself
            _nextDir = d;
        }

        public void Tick(long dtMs)
        {
            if (Mode != State.Playing) return;
            _accum += dtMs;
            while (_accum >= _stepMs && Mode == State.Playing)
            {
                _accum -= _stepMs;
                Step();
            }
        }

        private void Step()
        {
            _dir = _nextDir;
            var head = new Point(_body[0].X + _dir.X, _body[0].Y + _dir.Y);
            if (head.X < 0 || head.Y < 0 || head.X >= Cols || head.Y >= Rows || _body.Contains(head))
            {
                Mode = State.Over;
                Died?.Invoke(Score);
                return;
            }
            _body.Insert(0, head);
            if (head == _apple)
            {
                Score++;
                _stepMs = Math.Max(60, 150 - Score * 4);
                _flashUntil = Environment.TickCount64 + 120;
                Scored?.Invoke(Score);
                PlaceApple();
            }
            else
            {
                _body.RemoveAt(_body.Count - 1);
            }
        }

        private void PlaceApple()
        {
            for (int tries = 0; tries < 400; tries++)
            {
                var p = new Point(_rng.Next(Cols), _rng.Next(Rows));
                if (!_body.Contains(p)) { _apple = p; return; }
            }
        }

        public void Draw(Graphics g, Rectangle client, Font font, Font big, int best)
        {
            // Square-ish cells that fit the window, centred under a score strip.
            const int header = 34;
            var cell = Math.Max(8, Math.Min((client.Width - 16) / Cols, (client.Height - header - 12) / Rows));
            var bw = cell * Cols; var bh = cell * Rows;
            var bx = client.X + (client.Width - bw) / 2;
            var by = client.Y + header + (client.Height - header - bh) / 2;

            using (var bg = new SolidBrush(Color.FromArgb(12, 18, 36))) g.FillRectangle(bg, client);
            g.DrawString($"SCORE {Score}", font, Brushes.Gold, client.X + 12, client.Y + 8);
            var bestText = $"BEST {Math.Max(best, Score)}";
            var bs = g.MeasureString(bestText, font);
            g.DrawString(bestText, font, Brushes.Cyan, client.Right - bs.Width - 12, client.Y + 8);

            // playfield: dark checker so movement is easy to read
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    using var b = new SolidBrush((x + y) % 2 == 0 ? Color.FromArgb(18, 30, 58) : Color.FromArgb(14, 24, 48));
                    g.FillRectangle(b, bx + x * cell, by + y * cell, cell, cell);
                }
            g.DrawRectangle(Pens.SteelBlue, bx - 1, by - 1, bw + 1, bh + 1);

            // apple
            var flash = Environment.TickCount64 < _flashUntil;
            using (var ab = new SolidBrush(flash ? Color.White : Color.FromArgb(255, 70, 90)))
                g.FillEllipse(ab, bx + _apple.X * cell + 2, by + _apple.Y * cell + 2, cell - 4, cell - 4);
            using (var leaf = new SolidBrush(Color.FromArgb(90, 230, 120)))
                g.FillRectangle(leaf, bx + _apple.X * cell + cell / 2, by + _apple.Y * cell, Math.Max(2, cell / 5), Math.Max(2, cell / 4));

            // snake: cyan head shading toward magenta tail (the app's colours)
            for (int i = _body.Count - 1; i >= 0; i--)
            {
                var t = _body.Count <= 1 ? 0 : i / (double)(_body.Count - 1);
                var c = Mix(Rgb(0, 230, 255), Rgb(255, 0, 170), t);
                using var sb = new SolidBrush(Color.FromArgb(Rc(c), Gc(c), Bc(c)));
                g.FillRectangle(sb, bx + _body[i].X * cell + 1, by + _body[i].Y * cell + 1, cell - 2, cell - 2);
            }
            if (_body.Count > 0)
            {
                var h = _body[0];
                g.FillRectangle(Brushes.White, bx + h.X * cell + cell / 3 - 1, by + h.Y * cell + cell / 3, 3, 3);
                g.FillRectangle(Brushes.White, bx + h.X * cell + 2 * cell / 3 - 1, by + h.Y * cell + cell / 3, 3, 3);
            }

            if (Mode != State.Playing)
            {
                using var veil = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
                g.FillRectangle(veil, bx, by, bw, bh);
                var title = Mode == State.Over ? "GAME OVER" : "SNAKE";
                var ts = g.MeasureString(title, big);
                g.DrawString(title, big, Mode == State.Over ? Brushes.OrangeRed : Brushes.Cyan, bx + (bw - ts.Width) / 2, by + bh / 2 - ts.Height - 8);
                var hint = Mode == State.Over ? $"You scored {Score}.  SPACE to try again" : "Arrow keys or WASD to steer.  SPACE to start";
                var hs = g.MeasureString(hint, font);
                g.DrawString(hint, font, Brushes.Gainsboro, bx + (bw - hs.Width) / 2, by + bh / 2 + 6);
            }
        }
    }
}
