using System.Diagnostics;
using System.Media;
using System.Reflection;

namespace GamingStackGUI
{
    // Trophies: unlocking, the pop-up toast and the Konami code.
    public partial class MainForm
    {
        // ---- achievement toasts ----
        // Unlock conditions are checked from wherever they naturally happen (the
        // installer's background task via OnItemResult/OnFinished, or the wizard's UI
        // thread for choice-based ones like Completionist), so the queue and the
        // fired-set are guarded by their own lock. _currentToast/_toastWatch are only
        // ever touched from Advance()/Paint on the UI thread, so they don't need it.
        private readonly record struct Achievement(string Id, string Title, string Subtitle);

        // Single source of truth for every achievement - both UnlockAchievement (by id)
        // and the summary screen's tracker (which needs to list every achievement,
        // locked or not) read from this instead of each call site repeating its own
        // copy of the title/subtitle text.
        private static readonly Achievement[] AllAchievements =
        {
            new("snake_10", "Snake Charmer", "Score 10 in Snake, in the Arcade."),
            new("welcome_back", "Welcome Back", "You came back on another day, just for fun."),
            new("explorer", "Desktop Explorer", "Opened every app on the desktop."),
            new("perfect_corner", "Perfect Corner", "The screensaver logo hit a corner exactly."),
            new("on_a_roll", "On a Roll", "Opened GamingStack 3 days in a row."),
            new("regular", "Regular", "Opened GamingStack 7 times."),
            new("mine_sweeper", "Sweeper", "Cleared a Minesweeper board."),
            new("pong_champ", "Pong Champ", "Beat the computer at Pong."),
            new("brick_breaker", "Brick Breaker", "Cleared every brick in Breakout."),
            new("defragged", "Fully Defragmented", "Defragged a drive that did not need it."),
            new("hacker_voice", "Hacker Voice", "Found a secret command in the Command Prompt."),
            new("secret_master", "Secret Keeper", "Found every secret command in the Command Prompt."),
            new("first_blood", "First Blood", "Your first successful install this run."),
            new("completionist", "Completionist", "Going for the whole catalog, nothing held back."),
            new("redistributable_rampage", "Redistributable Rampage", "Every VC++ runtime, handled in one go."),
            new("already_perfect", "Already Perfect", "Something on this PC didn't need our help."),
            new("not_meant_to_be", "Not Everything's Meant To Be", "Something didn't make it - check the summary for why."),
            new("speedrunner", "Speedrunner", "Full install finished in under 90 seconds."),
        };

        private readonly HashSet<string> _achievementsFired = new();
        private readonly Queue<Achievement> _pendingToasts = new();
        private readonly object _achievementLock = new();
        private Achievement? _currentToast;
        private readonly Stopwatch _toastWatch = new();

        private const int ToastSlideInMs = 250;
        private const int ToastHoldMs = 2200;
        private const int ToastFadeOutMs = 400;
        private const int ToastTotalMs = ToastSlideInMs + ToastHoldMs + ToastFadeOutMs;

        private void UnlockAchievement(string id)
        {
            var index = Array.FindIndex(AllAchievements, a => a.Id == id);
            if (index < 0) return; // unknown id - shouldn't happen, just don't crash over it

            lock (_achievementLock)
            {
                if (!_achievementsFired.Add(id)) return; // already unlocked this run
                _pendingToasts.Enqueue(AllAchievements[index]);
            }
            ProgressStore.Unlock(id); // remembered between runs, shown in the Trophies window
        }

        // Deliberately separate from UnlockAchievement/AllAchievements - the Konami
        // code's toast is a genuine hidden surprise, so it must never appear in (or
        // inflate the count on) the Summary screen's achievement tracker, which only
        // ever enumerates AllAchievements. Still routes through the same fired-set
        // guard (keyed with a "secret_" prefix so it can't collide with a real id)
        // so mashing the code twice in one run doesn't queue a second toast.
        private void UnlockSecretAchievement(string id, string title, string subtitle)
        {
            lock (_achievementLock)
            {
                if (!_achievementsFired.Add("secret_" + id)) return;
                _pendingToasts.Enqueue(new Achievement(id, title, subtitle));
            }
            ProgressStore.Unlock("secret_" + id);
        }

        // ---- Konami code easter egg ----
        // The only hint anywhere in the app is the disguised BIOS POST line in
        // BuildBiosTimeline(). Checked on every keypress regardless of _stage, so it
        // works whether you're staring at the BIOS screen, the boot animation, the
        // desktop, or the terminal wizard.
        private static readonly Keys[] KonamiSequence =
        {
            Keys.Up, Keys.Up, Keys.Down, Keys.Down,
            Keys.Left, Keys.Right, Keys.Left, Keys.Right,
            Keys.B, Keys.A,
        };
        private int _konamiProgress;

        private void CheckKonamiCode(Keys key)
        {
            if (key == KonamiSequence[_konamiProgress])
            {
                _konamiProgress++;
                if (_konamiProgress == KonamiSequence.Length)
                {
                    _konamiProgress = 0;
                    UnlockSecretAchievement("konami", "Cheat Code Accepted",
                        "Konami code entered. It doesn't do anything - but you found it.");
                }
            }
            else
            {
                // Restart the match, but allow the failed keypress to itself be a
                // valid restart of the sequence (e.g. two Ups in a row shouldn't
                // dead-end the whole thing).
                _konamiProgress = key == KonamiSequence[0] ? 1 : 0;
            }
        }

        // Bottom-right achievement toast, drawn as an overlay on top of whatever phase
        // is currently showing - an unlock can happen mid-wizard or mid-install, and
        // shouldn't have to wait for a specific screen. Slides up, holds, then fades
        // out; the queue (see Advance()) only ever shows one at a time.
        private void DrawAchievementToast(Graphics g, int fullX, int fullY, int fullW, int fullH)
        {
            if (_currentToast == null) return;
            var toast = _currentToast.Value;
            var elapsed = _toastWatch.ElapsedMilliseconds;

            // Roughly double the original size across the board - box, fonts, icon,
            // padding - since the first pass read as too small to comfortably read.
            // Capped against the window width for a small/resized terminal.
            const int margin = 24;
            var boxWidth = Math.Min(900, fullW - margin * 2);
            const int padding = 22;
            const int iconColumnWidth = 76;
            const int headerLineHeight = 26;
            const int titleLineHeight = 30;
            const int subtitleLineHeight = 22;
            const int maxSubtitleLines = 3; // defensive cap - real subtitles are one-liners

            using var headerFont = Ui.Font(14f, FontStyle.Bold);
            using var titleFont = Ui.Font(18f, FontStyle.Bold);
            using var subtitleFont = Ui.Font(14f, FontStyle.Regular);

            // Box height is driven by the actual content, not a guessed constant - a
            // long title or subtitle wraps onto extra lines instead of overflowing the
            // box, which is exactly what was overlapping/getting cut off before.
            var textAreaWidth = boxWidth - iconColumnWidth - padding * 2;
            var titleLines = WrapTextToWidth(g, toast.Title, titleFont, textAreaWidth);
            var subtitleLines = WrapTextToWidth(g, toast.Subtitle, subtitleFont, textAreaWidth);
            if (subtitleLines.Count > maxSubtitleLines)
                subtitleLines = subtitleLines.Take(maxSubtitleLines).ToList();

            var contentHeight = headerLineHeight + titleLines.Count * titleLineHeight +
                                 subtitleLines.Count * subtitleLineHeight;
            var boxHeight = Math.Max(128, contentHeight + padding * 2);

            // Slide up from just below the window edge, hold in place, then fade by
            // dropping alpha rather than moving - a fade reads as "done", a slide back
            // down reads as "still busy", which we don't want colliding with a toast
            // that's about to be replaced by the next one in queue.
            float slideProgress = Math.Clamp(elapsed / (float)ToastSlideInMs, 0f, 1f);
            var eased = 1 - MathF.Pow(1 - slideProgress, 3);

            int alpha;
            if (elapsed < ToastSlideInMs + ToastHoldMs)
            {
                alpha = 255;
            }
            else
            {
                var fadeElapsed = elapsed - (ToastSlideInMs + ToastHoldMs);
                var fadeProgress = Math.Clamp(fadeElapsed / (float)ToastFadeOutMs, 0f, 1f);
                alpha = (int)(255 * (1 - fadeProgress));
            }
            if (alpha <= 0) return;

            var boxX = fullX + fullW - boxWidth - margin;
            var restY = fullY + fullH - boxHeight - margin;
            var boxY = (int)(restY + boxHeight * (1 - eased)); // slides up into place

            using var bgBrush = new SolidBrush(Color.FromArgb(alpha, 20, 20, 20));
            using var borderPen = new Pen(Color.FromArgb(alpha, 255, 215, 0), 2); // gold
            g.FillRectangle(bgBrush, boxX, boxY, boxWidth, boxHeight);
            g.DrawRectangle(borderPen, boxX, boxY, boxWidth, boxHeight);

            DrawPixelTrophy(g, boxX + 20, boxY + padding + 4, alpha);

            using var headerBrush = new SolidBrush(Color.FromArgb(alpha, 255, 215, 0));
            using var titleBrush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
            using var subtitleBrush = new SolidBrush(Color.FromArgb(alpha, 180, 180, 180));

            var textX = boxX + iconColumnWidth + padding;
            var ty = boxY + padding;
            g.DrawString("ACHIEVEMENT UNLOCKED", headerFont, headerBrush, textX, ty);
            ty += headerLineHeight;

            foreach (var line in titleLines)
            {
                g.DrawString(line, titleFont, titleBrush, textX, ty);
                ty += titleLineHeight;
            }

            foreach (var line in subtitleLines)
            {
                g.DrawString(line, subtitleFont, subtitleBrush, textX, ty);
                ty += subtitleLineHeight;
            }
        }

        // A small original pixel-art trophy (rectangles/ellipses only) for the toast -
        // not a reproduction of any real icon or brand.
        private static void DrawPixelTrophy(Graphics g, int x, int y, int alpha)
        {
            using var cupBrush = new SolidBrush(Color.FromArgb(alpha, 255, 215, 0));
            using var stemBrush = new SolidBrush(Color.FromArgb(alpha, 218, 165, 32));
            g.FillRectangle(cupBrush, x + 8, y, 24, 24);
            g.FillEllipse(cupBrush, x, y, 12, 20);
            g.FillEllipse(cupBrush, x + 28, y, 12, 20);
            g.FillRectangle(stemBrush, x + 16, y + 24, 8, 12);
            g.FillRectangle(stemBrush, x + 10, y + 36, 20, 6);
        }

        // ---- quick-skip ----
        // SPACE jumps straight from the BIOS / boot / desktop intro to the terminal.
        // Achievement toast queue: pop the next one once nothing is showing, and clear the
        // current one once its total on-screen time is up. Only pops while the desktop is
        // up, so a toast can never run its whole life unseen behind the BIOS or boot screen.
        private void UpdateToasts()
        {
            if (_currentToast == null)
            {
                if (!InShell) return;
                Achievement? next = null;
                lock (_achievementLock)
                {
                    if (_pendingToasts.Count > 0) next = _pendingToasts.Dequeue();
                }
                if (next != null)
                {
                    _currentToast = next;
                    _toastWatch.Restart();
                }
            }
            else if (_toastWatch.ElapsedMilliseconds >= ToastTotalMs)
            {
                _currentToast = null;
            }
        }
    }
}
