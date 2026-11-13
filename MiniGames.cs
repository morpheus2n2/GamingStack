namespace GamingStackGUI
{
    // Three small games for the Arcade. Each one is plain logic + GDI+ drawing, driven by
    // the shell: Draw() every frame, Tick() from the ~30 fps timer, and Key()/Click() from
    // the focused window. Keeping them tiny and separate means adding another is easy.

    // ====================================================================== Minesweeper

    internal sealed class MinesweeperGame
    {
        public enum State { Ready, Playing, Won, Lost }
        private const int N = 9, MineCount = 10;

        private readonly Random _rng = new();
        private bool[,] _mine = new bool[N, N];
        private bool[,] _open = new bool[N, N];
        private bool[,] _flag = new bool[N, N];
        private int[,] _adj = new int[N, N];
        private long _startedAt;
        private long _endedAt;

        public State Mode { get; private set; } = State.Ready;
        public event Action<int>? Won;      // seconds taken

        // layout from the last Draw, used to hit-test clicks
        private int _bx, _by, _cell;
        private Rectangle _face;

        public MinesweeperGame() => Reset();

        public void Reset()
        {
            _mine = new bool[N, N]; _open = new bool[N, N]; _flag = new bool[N, N]; _adj = new int[N, N];
            Mode = State.Ready;
        }

        private void PlaceMines(int safeX, int safeY)
        {
            var placed = 0;
            while (placed < MineCount)
            {
                var x = _rng.Next(N); var y = _rng.Next(N);
                if (_mine[x, y] || (Math.Abs(x - safeX) <= 1 && Math.Abs(y - safeY) <= 1)) continue;
                _mine[x, y] = true; placed++;
            }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var c = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            var nx = x + dx; var ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < N && ny < N && _mine[nx, ny]) c++;
                        }
                    _adj[x, y] = c;
                }
        }

        private void Reveal(int x, int y)
        {
            if (x < 0 || y < 0 || x >= N || y >= N || _open[x, y] || _flag[x, y]) return;
            _open[x, y] = true;
            if (_adj[x, y] == 0 && !_mine[x, y])
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        Reveal(x + dx, y + dy);
        }

        public void Click(Point p, bool right)
        {
            if (_face.Contains(p)) { Reset(); return; }
            if (Mode is State.Won or State.Lost || _cell <= 0) return;
            var x = (p.X - _bx) / _cell; var y = (p.Y - _by) / _cell;
            if (p.X < _bx || p.Y < _by || x >= N || y >= N) return;

            if (right)
            {
                if (!_open[x, y]) _flag[x, y] = !_flag[x, y];
                return;
            }
            if (_flag[x, y]) return;
            if (Mode == State.Ready)
            {
                PlaceMines(x, y);
                Mode = State.Playing;
                _startedAt = Environment.TickCount64;
            }
            if (_mine[x, y])
            {
                _open[x, y] = true;
                Mode = State.Lost;
                _endedAt = Environment.TickCount64;
                return;
            }
            Reveal(x, y);
            var closed = 0;
            for (int j = 0; j < N; j++) for (int i = 0; i < N; i++) if (!_open[i, j]) closed++;
            if (closed == MineCount)
            {
                Mode = State.Won;
                _endedAt = Environment.TickCount64;
                Won?.Invoke((int)((_endedAt - _startedAt) / 1000));
            }
        }

        private int Seconds => Mode == State.Ready ? 0
            : (int)(((Mode is State.Won or State.Lost ? _endedAt : Environment.TickCount64) - _startedAt) / 1000);

        private static readonly Color[] NumberColors =
        {
            Color.Empty, Color.Blue, Color.DarkGreen, Color.Red, Color.Navy, Color.Maroon, Color.Teal, Color.Black, Color.Gray
        };

        public void Draw(Graphics g, Rectangle c, Font font, Font bold)
        {
            using (var bg = new SolidBrush(Color.FromArgb(192, 192, 192))) g.FillRectangle(bg, c);
            const int header = 44;
            _cell = Math.Max(14, Math.Min((c.Width - 24) / N, (c.Height - header - 24) / N));
            var bw = _cell * N;
            _bx = c.X + (c.Width - bw) / 2 - c.X; _by = header + 8;       // client-relative for clicks
            var abx = c.X + _bx; var aby = c.Y + _by;

            // header: mines left, face, timer
            var flags = 0; foreach (var f in _flag) if (f) flags++;
            DrawLcd(g, bold, $"{Math.Max(0, MineCount - flags):D3}", abx, c.Y + 6);
            DrawLcd(g, bold, $"{Math.Min(999, Seconds):D3}", abx + bw - 62, c.Y + 6);
            var faceSize = 34;
            var faceRect = new Rectangle(abx + bw / 2 - faceSize / 2, c.Y + 5, faceSize, faceSize);
            _face = new Rectangle(faceRect.X - c.X, faceRect.Y - c.Y, faceRect.Width, faceRect.Height);
            using (var fb = new SolidBrush(Color.FromArgb(192, 192, 192))) g.FillRectangle(fb, faceRect);
            g.DrawRectangle(Pens.Black, faceRect);
            var mood = Mode == State.Lost ? "x_x" : Mode == State.Won ? "B)" : ":)";
            var ms = g.MeasureString(mood, bold);
            g.DrawString(mood, bold, Brushes.Black, faceRect.X + (faceRect.Width - ms.Width) / 2, faceRect.Y + (faceRect.Height - ms.Height) / 2);

            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var r = new Rectangle(abx + x * _cell, aby + y * _cell, _cell, _cell);
                    var showMine = Mode == State.Lost && _mine[x, y];
                    if (_open[x, y] || showMine)
                    {
                        using var open = new SolidBrush(showMine && _open[x, y] ? Color.Red : Color.FromArgb(206, 206, 206));
                        g.FillRectangle(open, r);
                        g.DrawRectangle(Pens.Gray, r);
                        if (_mine[x, y])
                        {
                            g.FillEllipse(Brushes.Black, r.X + _cell / 4, r.Y + _cell / 4, _cell / 2, _cell / 2);
                            g.DrawLine(Pens.Black, r.X + 3, r.Y + _cell / 2, r.Right - 3, r.Y + _cell / 2);
                            g.DrawLine(Pens.Black, r.X + _cell / 2, r.Y + 3, r.X + _cell / 2, r.Bottom - 3);
                        }
                        else if (_adj[x, y] > 0)
                        {
                            using var nb = new SolidBrush(NumberColors[_adj[x, y]]);
                            var t = _adj[x, y].ToString();
                            var s = g.MeasureString(t, bold);
                            g.DrawString(t, bold, nb, r.X + (r.Width - s.Width) / 2, r.Y + (r.Height - s.Height) / 2);
                        }
                    }
                    else
                    {
                        using var face = new SolidBrush(Color.FromArgb(192, 192, 192));
                        g.FillRectangle(face, r);
                        g.DrawLine(Pens.White, r.Left, r.Top, r.Right - 1, r.Top);
                        g.DrawLine(Pens.White, r.Left, r.Top, r.Left, r.Bottom - 1);
                        g.DrawLine(Pens.DimGray, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1);
                        g.DrawLine(Pens.DimGray, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1);
                        if (_flag[x, y])
                        {
                            g.FillRectangle(Brushes.Black, r.X + _cell / 2, r.Y + 4, 2, _cell - 9);
                            g.FillPolygon(Brushes.Red, new[]
                            {
                                new Point(r.X + _cell / 2 + 2, r.Y + 4), new Point(r.X + _cell / 2 + 2, r.Y + _cell / 2),
                                new Point(r.X + 4, r.Y + _cell / 3)
                            });
                        }
                    }
                }

            if (Mode is State.Won or State.Lost)
            {
                var msg = Mode == State.Won ? $"Cleared in {Seconds}s!  Click the face to play again" : "Boom!  Click the face to try again";
                var s = g.MeasureString(msg, font);
                g.DrawString(msg, font, Brushes.Black, c.X + (c.Width - s.Width) / 2, aby + bw + 8);
            }
            else if (Mode == State.Ready)
            {
                var msg = "Left click to dig, right click to flag";
                var s = g.MeasureString(msg, font);
                g.DrawString(msg, font, Brushes.DimGray, c.X + (c.Width - s.Width) / 2, aby + bw + 8);
            }
        }

        private static void DrawLcd(Graphics g, Font f, string text, int x, int y)
        {
            using var bg = new SolidBrush(Color.Black);
            g.FillRectangle(bg, x, y, 62, 32);
            g.DrawString(text, f, Brushes.Red, x + 6, y + 4);
        }
    }

    // ====================================================================== Pong

    internal sealed class PongGame
    {
        public enum State { Ready, Playing, Over }
        private double _py = 0.5, _ay = 0.5;               // paddle centres, 0..1
        private double _bx = 0.5, _by = 0.5, _vx, _vy;     // ball, in field widths per second
        private double _aiSpeed = 0.55;
        public int PlayerScore { get; private set; }
        public int AiScore { get; private set; }
        public State Mode { get; private set; } = State.Ready;
        public event Action? PlayerWon;

        private const double PaddleH = 0.20;

        public void Start()
        {
            PlayerScore = AiScore = 0; _aiSpeed = 0.55;
            Serve(1);
            Mode = State.Playing;
        }

        private void Serve(int dir)
        {
            _bx = 0.5; _by = 0.5;
            var rng = new Random();
            _vx = 0.55 * dir; _vy = (rng.NextDouble() - 0.5) * 0.5;
        }

        public void Key(Keys k)
        {
            if (k is Keys.Up or Keys.W) _py = Math.Max(PaddleH / 2, _py - 0.05);
            else if (k is Keys.Down or Keys.S) _py = Math.Min(1 - PaddleH / 2, _py + 0.05);
        }

        public void Tick(long dtMs)
        {
            if (Mode != State.Playing) return;
            var dt = Math.Min(dtMs, 60) / 1000.0;
            // the computer follows the ball, a little slower than the ball can go
            var dy = _by - _ay;
            _ay += Math.Clamp(dy, -_aiSpeed * dt, _aiSpeed * dt);
            _ay = Math.Clamp(_ay, PaddleH / 2, 1 - PaddleH / 2);

            _bx += _vx * dt; _by += _vy * dt;
            if (_by < 0.02) { _by = 0.02; _vy = Math.Abs(_vy); }
            if (_by > 0.98) { _by = 0.98; _vy = -Math.Abs(_vy); }

            // paddles sit at x = 0.04 (player) and 0.96 (computer)
            if (_vx < 0 && _bx <= 0.05 && Math.Abs(_by - _py) <= PaddleH / 2 + 0.02) Bounce(_py, 1);
            else if (_vx > 0 && _bx >= 0.95 && Math.Abs(_by - _ay) <= PaddleH / 2 + 0.02) Bounce(_ay, -1);
            else if (_bx < -0.02) { AiScore++; Next(1); }
            else if (_bx > 1.02) { PlayerScore++; _aiSpeed = Math.Min(0.9, _aiSpeed + 0.03); Next(-1); }
        }

        private void Bounce(double paddleY, int dir)
        {
            var speed = Math.Min(1.3, Math.Abs(_vx) * 1.06);
            _vx = speed * dir;
            _vy = (_by - paddleY) / (PaddleH / 2) * 0.6;
            _bx = dir > 0 ? 0.051 : 0.949;
        }

        private void Next(int dir)
        {
            if (PlayerScore >= 7 || AiScore >= 7)
            {
                Mode = State.Over;
                if (PlayerScore >= 7) PlayerWon?.Invoke();
                return;
            }
            Serve(dir);
        }

        public void Draw(Graphics g, Rectangle c, Font font, Font big)
        {
            using (var bg = new SolidBrush(Color.FromArgb(6, 8, 20))) g.FillRectangle(bg, c);
            var f = new Rectangle(c.X + 20, c.Y + 50, c.Width - 40, c.Height - 70);
            g.DrawRectangle(Pens.SteelBlue, f);
            for (int y = f.Y; y < f.Bottom; y += 18) g.FillRectangle(Brushes.SteelBlue, f.X + f.Width / 2 - 1, y, 2, 9);

            var score = $"{PlayerScore}   :   {AiScore}";
            var ss = g.MeasureString(score, big);
            g.DrawString(score, big, Brushes.Cyan, c.X + (c.Width - ss.Width) / 2, c.Y + 8);

            var ph = (int)(f.Height * PaddleH);
            g.FillRectangle(Brushes.Cyan, f.X + (int)(f.Width * 0.04) - 4, f.Y + (int)(f.Height * _py) - ph / 2, 8, ph);
            g.FillRectangle(Brushes.Magenta, f.X + (int)(f.Width * 0.96) - 4, f.Y + (int)(f.Height * _ay) - ph / 2, 8, ph);
            g.FillRectangle(Brushes.White, f.X + (int)(f.Width * _bx) - 6, f.Y + (int)(f.Height * _by) - 6, 12, 12);

            if (Mode != State.Playing)
            {
                var title = Mode == State.Over ? (PlayerScore >= 7 ? "YOU WIN!" : "THE COMPUTER WINS") : "PONG";
                var ts = g.MeasureString(title, big);
                g.DrawString(title, big, Brushes.Gold, f.X + (f.Width - ts.Width) / 2, f.Y + f.Height / 2 - 40);
                var hint = "Up / Down (or W / S) to move.  SPACE to serve.  First to 7.";
                var hs = g.MeasureString(hint, font);
                g.DrawString(hint, font, Brushes.Gainsboro, f.X + (f.Width - hs.Width) / 2, f.Y + f.Height / 2 + 4);
            }
        }
    }

    // ====================================================================== Breakout

    internal sealed class BreakoutGame
    {
        public enum State { Ready, Playing, Over, Cleared }
        private const int Cols = 10, Rows = 6;
        private readonly bool[,] _alive = new bool[Cols, Rows];
        private double _px = 0.5;                       // paddle centre 0..1
        private double _bx, _by, _vx, _vy;              // ball, field units per second
        private const double PaddleW = 0.16;

        public State Mode { get; private set; } = State.Ready;
        public int Score { get; private set; }
        public int Lives { get; private set; }
        public event Action? Cleared;

        public void Start()
        {
            for (int y = 0; y < Rows; y++) for (int x = 0; x < Cols; x++) _alive[x, y] = true;
            Score = 0; Lives = 3; _px = 0.5;
            Launch();
            Mode = State.Playing;
        }

        private void Launch()
        {
            _bx = _px; _by = 0.82;
            var rng = new Random();
            _vx = (rng.NextDouble() < 0.5 ? -1 : 1) * (0.25 + rng.NextDouble() * 0.15); _vy = -0.6;
        }

        public void Key(Keys k)
        {
            if (k is Keys.Left or Keys.A) _px = Math.Max(PaddleW / 2, _px - 0.05);
            else if (k is Keys.Right or Keys.D) _px = Math.Min(1 - PaddleW / 2, _px + 0.05);
        }

        public void Tick(long dtMs)
        {
            if (Mode != State.Playing) return;
            var dt = Math.Min(dtMs, 60) / 1000.0;
            _bx += _vx * dt; _by += _vy * dt;
            if (_bx < 0.01) { _bx = 0.01; _vx = Math.Abs(_vx); }
            if (_bx > 0.99) { _bx = 0.99; _vx = -Math.Abs(_vx); }
            if (_by < 0.01) { _by = 0.01; _vy = Math.Abs(_vy); }

            // paddle at y = 0.92
            if (_vy > 0 && _by >= 0.90 && _by <= 0.95 && Math.Abs(_bx - _px) <= PaddleW / 2 + 0.015)
            {
                _vy = -Math.Abs(_vy);
                _vx = (_bx - _px) / (PaddleW / 2) * 0.55;
                _by = 0.899;
            }

            // bricks occupy y 0.08..0.38
            var bx = (int)(_bx * Cols); var by = (int)((_by - 0.08) / 0.30 * Rows);
            if (bx >= 0 && bx < Cols && by >= 0 && by < Rows && _alive[bx, by])
            {
                _alive[bx, by] = false; Score += 10 + (Rows - by) * 2;
                _vy = -_vy;
                var left = 0; foreach (var a in _alive) if (a) left++;
                if (left == 0) { Mode = State.Cleared; Cleared?.Invoke(); }
            }

            if (_by > 1.02)
            {
                Lives--;
                if (Lives <= 0) Mode = State.Over; else Launch();
            }
        }

        private static readonly Color[] Row = { Color.Red, Color.OrangeRed, Color.Orange, Color.Gold, Color.LimeGreen, Color.DeepSkyBlue };

        public void Draw(Graphics g, Rectangle c, Font font, Font big)
        {
            using (var bg = new SolidBrush(Color.FromArgb(8, 6, 24))) g.FillRectangle(bg, c);
            g.DrawString($"SCORE {Score}", font, Brushes.Gold, c.X + 16, c.Y + 8);
            var lives = $"LIVES {Lives}";
            var ls = g.MeasureString(lives, font);
            g.DrawString(lives, font, Brushes.Cyan, c.Right - ls.Width - 16, c.Y + 8);

            var f = new Rectangle(c.X + 20, c.Y + 36, c.Width - 40, c.Height - 52);
            g.DrawRectangle(Pens.SteelBlue, f);
            var bw = f.Width / (double)Cols; var bh = f.Height * 0.30 / Rows;
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    if (!_alive[x, y]) continue;
                    using var b = new SolidBrush(Row[y]);
                    g.FillRectangle(b, f.X + (float)(x * bw) + 1, f.Y + (float)(f.Height * 0.08 + y * bh) + 1, (float)bw - 2, (float)bh - 2);
                }
            g.FillRectangle(Brushes.Cyan, f.X + (int)(f.Width * (_px - PaddleW / 2)), f.Y + (int)(f.Height * 0.92), (int)(f.Width * PaddleW), 8);
            g.FillEllipse(Brushes.White, f.X + (int)(f.Width * _bx) - 6, f.Y + (int)(f.Height * _by) - 6, 12, 12);

            if (Mode != State.Playing)
            {
                var title = Mode == State.Cleared ? "CLEARED!" : Mode == State.Over ? "GAME OVER" : "BREAKOUT";
                var ts = g.MeasureString(title, big);
                g.DrawString(title, big, Brushes.Gold, f.X + (f.Width - ts.Width) / 2, f.Y + f.Height * 0.55f);
                var hint = "Left / Right (or A / D) to move.  SPACE to start.";
                var hs = g.MeasureString(hint, font);
                g.DrawString(hint, font, Brushes.Gainsboro, f.X + (f.Width - hs.Width) / 2, f.Y + f.Height * 0.55f + 40);
            }
        }
    }
}
