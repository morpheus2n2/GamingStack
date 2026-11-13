using System.Management;
using System.Media;
using System.Reflection;

namespace GamingStackGUI
{
    // End of the run: the summary report, the easter-egg mascot, shutdown and farewell screens.
    public partial class MainForm
    {
        // End-of-run report: what actually happened to each selected item, grouped
        // under category headings like the wizard's own list, but re-packed from
        // only what was attempted (see BuildSummaryColumns) so a small selection
        // doesn't leave one column empty and the other overflowing.
        private void DrawSummary(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var catalog = _catalog;
            var contentX = fullX + 16;
            const int lineHeight = 18;

            ItemResult[] results;
            lock (_itemResultLock) { results = _itemResults.ToArray(); }

            var headingTop = fullY + titleHeight + 10 + lineHeight;
            g.DrawString("Installation summary:", _mono, Brushes.Gainsboro, contentX, headingTop);

            var totalsTop = headingTop + lineHeight + 4;
            if (results.Length == 0)
            {
                g.DrawString("Nothing was selected - nothing was installed.", _monoSmall, Brushes.Gold, contentX, totalsTop);
            }
            else
            {
                var installed = results.Count(r => r.Kind == InstallResultKind.Installed);
                var failed = results.Count(r => r.Kind == InstallResultKind.Failed);
                var skipped = results.Count(r => r.Kind == InstallResultKind.Skipped);
                var opened = results.Count(r => r.Kind == InstallResultKind.Opened);
                g.DrawString($"{installed} installed    {opened} opened in browser    {failed} failed    {skipped} skipped",
                    _monoSmall, Brushes.Gold, contentX, totalsTop);
            }

            var logLineTop = totalsTop + lineHeight + 2;
            if (_buildSheetPath != null)
                g.DrawString($"Build sheet: {_buildSheetPath}", _monoSmall, Brushes.DimGray, contentX + 0, logLineTop);
            if (_installer.LogFilePath != null)
            {
                logLineTop += lineHeight;
                g.DrawString($"Full log:    {_installer.LogFilePath}   (Start menu > Setup Logs opens the folder)", _monoSmall, Brushes.DimGray, contentX, logLineTop);
            }

            // Tweaks are never silent - this is where the run reports exactly what
            // happened to the "Apply these tweaks?" question, matching whatever the
            // ConfirmTweaks screen disclosed beforehand.
            var tweaksLineTop = logLineTop + lineHeight;
            if (_installer.TweaksApplied)
            {
                g.DrawString("Tweaks: applied (see the revert script for exactly what changed)",
                    _monoSmall, Brushes.Gold, contentX, tweaksLineTop);
                tweaksLineTop += lineHeight;
                if (_installer.TweaksRevertFilePath != null)
                    g.DrawString($"  Undo them (run as Administrator): {_installer.TweaksRevertFilePath}",
                        _monoSmall, Brushes.DimGray, contentX, tweaksLineTop);
            }
            else
            {
                g.DrawString("Tweaks: skipped - you said no", _monoSmall, Brushes.Gray, contentX, tweaksLineTop);
            }

            // Restore point + optional image backup - same "never silent" principle.
            var backupLineTop = tweaksLineTop + lineHeight + 2;
            if (_backup.RestorePointCreated)
            {
                g.DrawString("Restore point: created", _monoSmall, Brushes.Gold, contentX, backupLineTop);
            }
            else
            {
                g.DrawString($"Restore point: failed - {_backup.RestorePointError ?? "unknown reason"}",
                    _monoSmall, Brushes.OrangeRed, contentX, backupLineTop);
            }
            backupLineTop += lineHeight;

            switch (_backupTiming)
            {
                case BackupTiming.Skip:
                    g.DrawString(_noEligibleBackupDrive
                            ? "Image backup: skipped - no eligible drive was found"
                            : "Image backup: skipped - you said no",
                        _monoSmall, Brushes.Gray, contentX, backupLineTop);
                    backupLineTop += lineHeight;
                    break;
                default:
                    var timingLabel = _backupTiming == BackupTiming.Before ? "before changes" : "after setup";
                    if (_backup.ImageBackupSucceeded)
                    {
                        g.DrawString($"Image backup: completed ({timingLabel}) -> {_backup.ImageBackupDestination}",
                            _monoSmall, Brushes.Gold, contentX, backupLineTop);
                    }
                    else
                    {
                        g.DrawString($"Image backup: failed ({timingLabel}) - {_backup.ImageBackupError ?? "unknown reason"}",
                            _monoSmall, Brushes.OrangeRed, contentX, backupLineTop);
                    }
                    backupLineTop += lineHeight;
                    if (_backup.LogFilePath != null)
                    {
                        g.DrawString($"  Backup log: {_backup.LogFilePath}", _monoSmall, Brushes.DimGray, contentX, backupLineTop);
                        backupLineTop += lineHeight;
                    }
                    break;
            }

            var listTop = backupLineTop + 10;
            var colWidth = (fullW - 32) / ColumnCountFor(results.Length);

            // Room for the detail line's "    " indent plus a small gutter, so a long
            // reason wraps onto extra lines within its own column instead of running
            // straight into whatever the neighboring column is drawing.
            var indentWidth = g.MeasureString("    ", _monoSmall).Width;
            var detailMaxWidth = Math.Max(60f, colWidth - indentWidth - 12);

            // Wrap every item's detail text once, up front - the column-balancing pass
            // and the actual draw below both need the exact same wrapped line count, or
            // they can disagree and the layout drifts back out of sync.
            var wrappedDetails = new Dictionary<AppEntry, List<string>>();
            foreach (var result in results)
            {
                if (result.Detail != null)
                    wrappedDetails[result.Entry] = WrapTextToWidth(g, result.Detail, _monoSmall, detailMaxWidth);
            }

            var columnGroups = BuildSummaryColumns(results, wrappedDetails);
            var maxRowsUsed = 0;

            for (int col = 0; col < columnGroups.Length; col++)
            {
                var lx = contentX + col * colWidth;
                var row = 0;

                foreach (var (category, indices) in columnGroups[col])
                {
                    g.DrawString($"-- {category} --", _headingFont, Brushes.DeepSkyBlue, lx, listTop + row * lineHeight);
                    row++;
                    row++; // spacer after the heading, matching the wizard's spacing

                    foreach (var i in indices)
                    {
                        var result = results.First(r => r.Entry == catalog[i]);
                        var (marker, brush) = result.Kind switch
                        {
                            InstallResultKind.Installed => ("[x]", Brushes.LightGreen),
                            InstallResultKind.Failed => ("[!]", Brushes.OrangeRed),
                            InstallResultKind.Opened => ("[>]", Brushes.Cyan),
                            _ => ("[-]", Brushes.Gray)
                        };
                        g.DrawString($"{marker} {catalog[i].FriendlyName}", _monoSmall, brush, lx, listTop + row * lineHeight);
                        row++;

                        // Show *why* it failed/was skipped right under the item, wrapped
                        // to fit this column - so the reason is visible here instead of
                        // buried in the scrolled-past install log or failed.txt, without
                        // bleeding into the other column when it runs long.
                        if (wrappedDetails.TryGetValue(catalog[i], out var detailLines))
                        {
                            foreach (var detailLine in detailLines)
                            {
                                g.DrawString($"    {detailLine}", _monoSmall, Brushes.DimGray, lx, listTop + row * lineHeight);
                                row++;
                            }
                        }
                    }
                    row++; // spacer before the next category
                }

                maxRowsUsed = Math.Max(maxRowsUsed, row);
            }

            // Achievement tracker - just for the fun of it, not tied to what was
            // actually attempted this run (unlike the breakdown above): every
            // achievement that exists, and whether it's been earned across the whole
            // session so far, not just this one run.
            var achievementsTop = listTop + maxRowsUsed * lineHeight + 32;

            HashSet<string> firedSnapshot;
            lock (_achievementLock) { firedSnapshot = new HashSet<string>(_achievementsFired); }
            var unlockedCount = AllAchievements.Count(a => firedSnapshot.Contains(a.Id));

            g.DrawString($"-- Achievements ({unlockedCount}/{AllAchievements.Length}) --",
                _headingFont, Brushes.DeepSkyBlue, contentX, achievementsTop);

            const int achievementCols = 2;
            var achievementColWidth = (fullW - 32) / achievementCols;
            var achievementRowsPerCol = (AllAchievements.Length + achievementCols - 1) / achievementCols;
            var achievementListTop = achievementsTop + lineHeight + 10;
            var achievementDescMaxWidth = Math.Max(60f, achievementColWidth - 24);

            // Each entry now takes a title row plus however many wrapped description
            // lines it needs, so - same lesson as the per-item breakdown above - each
            // column tracks its own running row count instead of assuming a fixed
            // height per entry, or a long description would overlap the row below it.
            var achievementMaxRows = 0;
            for (int col = 0; col < achievementCols; col++)
            {
                var lx = contentX + col * achievementColWidth;
                var row = 0;
                var startIndex = col * achievementRowsPerCol;
                var endIndex = Math.Min(AllAchievements.Length, startIndex + achievementRowsPerCol);

                for (int i = startIndex; i < endIndex; i++)
                {
                    var achievement = AllAchievements[i];
                    var unlocked = firedSnapshot.Contains(achievement.Id);
                    var marker = unlocked ? "[x]" : "[ ]";
                    var brush = unlocked ? Brushes.Gold : Brushes.Gray;

                    g.DrawString($"{marker} {achievement.Title}", _monoSmall, brush,
                        lx, achievementListTop + row * lineHeight);
                    row++;

                    var descLines = WrapTextToWidth(g, achievement.Subtitle, _monoSmall, achievementDescMaxWidth);
                    foreach (var line in descLines)
                    {
                        g.DrawString($"    {line}", _monoSmall, Brushes.DimGray, lx, achievementListTop + row * lineHeight);
                        row++;
                    }

                    row++; // spacer between achievements, so entries don't run together
                }

                achievementMaxRows = Math.Max(achievementMaxRows, row);
            }

            var promptTop = achievementListTop + achievementMaxRows * lineHeight + 16;
            using var promptFont = Ui.Font(14f, FontStyle.Bold);
            g.DrawString("Press any key to continue...", promptFont, Brushes.Gold, contentX, promptTop);
        }

        // A little original mascot for the "didn't want to choose, didn't want to quit
        // either" branch - a wobbling floppy disk with a speech bubble. Fully original
        // shapes (rectangles/ellipses), not a reproduction of any real character.
        private void DrawEasterEgg(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight, long elapsedMs)
        {
            var contentTop = fullY + titleHeight;
            var contentHeight = fullH - titleHeight;
            var centerX = fullX + fullW / 2;
            var bob = (float)Math.Sin(elapsedMs / 250.0) * 6f;

            const int diskW = 90, diskH = 100;
            var diskX = centerX - diskW / 2;
            var diskY = contentTop + contentHeight / 2 - diskH / 2 + 30 + (int)bob;

            // body
            using var bodyBrush = new SolidBrush(Color.FromArgb(40, 40, 200));
            g.FillRectangle(bodyBrush, diskX, diskY, diskW, diskH);
            g.DrawRectangle(Pens.Gainsboro, diskX, diskY, diskW, diskH);

            // metal shutter
            using var shutterBrush = new SolidBrush(Color.FromArgb(200, 200, 210));
            g.FillRectangle(shutterBrush, diskX + 14, diskY, diskW - 28, 26);

            // label
            using var labelBrush = new SolidBrush(Color.FromArgb(235, 235, 245));
            g.FillRectangle(labelBrush, diskX + 10, diskY + 36, diskW - 20, 36);

            // face on the label
            using var eyePen = new Pen(Color.Black, 3);
            g.FillEllipse(Brushes.Black, diskX + 24, diskY + 46, 6, 6);
            g.FillEllipse(Brushes.Black, diskX + diskW - 30, diskY + 46, 6, 6);
            g.DrawArc(eyePen, diskX + 22, diskY + 52, diskW - 44, 16, 0, 180);

            // speech bubble
            using var bubbleFont = Ui.Font(13f, FontStyle.Regular);
            const string joke = "Big decisions, huh?\nI'm just a floppy disk and\neven I can commit to 1.44MB.";
            var jokeSize = g.MeasureString(joke, bubbleFont);
            var bubbleW = jokeSize.Width + 30;
            var bubbleH = jokeSize.Height + 24;
            var bubbleX = centerX - bubbleW / 2;
            var bubbleY = diskY - bubbleH - 30;

            using var bubbleBrush = new SolidBrush(Color.White);
            g.FillRectangle(bubbleBrush, bubbleX, bubbleY, bubbleW, bubbleH);
            g.DrawRectangle(Pens.Black, bubbleX, bubbleY, bubbleW, bubbleH);

            // little pointer triangle from bubble down toward the disk
            var tipX = centerX;
            var tipY = bubbleY + bubbleH;
            var tri = new[]
            {
                new PointF(tipX - 10, tipY),
                new PointF(tipX + 10, tipY),
                new PointF(tipX, tipY + 14)
            };
            g.FillPolygon(Brushes.White, tri);
            g.DrawPolygon(Pens.Black, tri);

            g.DrawString(joke, bubbleFont, Brushes.Black, bubbleX + 15, bubbleY + 12);
        }

        // The sign-off screen, styled like an old text-mode "installation complete"
        // box - reached either after a real install finishes or after the easter egg.
        // Fake "shutting down" outro, played dead straight in Windows 9x style: a
        // handful of staged status lines typed out over a couple of seconds, then a
        // full "It's now safe to turn off your computer" beat (the classic screen,
        // framed inside our own fake terminal - a screen within the screen) before
        // auto-advancing to Farewell. This is a beat, not a question, so it plays
        // itself out with no keypress needed (see Advance()'s ShutdownAutoAdvanceMs).
        private const long ShutdownLine1At = 0;
        private const long ShutdownLine2At = 900;
        private const long ShutdownLine3At = 1800;
        private const long ShutdownLine4At = 3000;
        private const long ShutdownFinalAt = 4400;
        private const long ShutdownAutoAdvanceMs = ShutdownFinalAt + 2600;

        private void DrawShuttingDown(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            var elapsed = _phaseWatch.ElapsedMilliseconds;

            if (elapsed >= ShutdownFinalAt)
            {
                var contentX = fullX + 1;
                var contentY = fullY + titleHeight + 1;
                var contentW = fullW - 2;
                var contentH = fullH - titleHeight - 2;
                using var bg = new SolidBrush(Color.FromArgb(0, 0, 128)); // classic shutdown-screen navy
                g.FillRectangle(bg, contentX, contentY, contentW, contentH);

                using var bigFont = Ui.Font(20f, FontStyle.Bold);
                const string message = "It's now safe to turn off your computer.";
                var size = g.MeasureString(message, bigFont);
                g.DrawString(message, bigFont, Brushes.White,
                    fullX + (fullW - size.Width) / 2, fullY + (fullH - size.Height) / 2);
                return;
            }

            var contentX2 = fullX + 16;
            const int lineHeight = 22;
            var top = fullY + titleHeight + 30;

            (long At, string Text)[] lines =
            {
                (ShutdownLine1At, "Saving your settings..."),
                (ShutdownLine2At, "Closing GamingStack Installer..."),
                (ShutdownLine3At, "GamingStack has finished configuring your PC."),
                (ShutdownLine4At, "Shutting down..."),
            };

            var row = 0;
            foreach (var (at, text) in lines)
            {
                if (elapsed < at) continue;
                g.DrawString(text, _mono, Brushes.LimeGreen, contentX2, top + row * lineHeight);
                row++;
            }
        }

        private void DrawFarewell(Graphics g, int fullX, int fullY, int fullW, int fullH, int titleHeight)
        {
            using var textFont = Ui.Font(13f, FontStyle.Regular);
            using var signFont = Ui.Font(13f, FontStyle.Bold);

            var lines = new[]
            {
                ("Thank you for using my Installer stack on your", textFont, Brushes.LimeGreen),
                ("Shiny PC, I hope you have lots of fun playing.", textFont, Brushes.LimeGreen),
                ("", textFont, Brushes.LimeGreen),
                ("Yours, Morpheus2n2", signFont, Brushes.Gold)
            };

            const int lineHeight = 24;
            var maxTextWidth = lines.Max(l => g.MeasureString(l.Item1, l.Item2).Width);

            const int glyphAreaHeight = 60;
            var boxWidth = Math.Min(fullW - 60, (int)maxTextWidth + 60);
            var boxHeight = lines.Length * lineHeight + glyphAreaHeight + 30;

            var boxX = fullX + (fullW - boxWidth) / 2;
            var boxY = fullY + titleHeight + Math.Max(10, (fullH - titleHeight - boxHeight) / 2);

            using var borderPen = new Pen(Color.Cyan, 2);
            g.DrawRectangle(borderPen, boxX, boxY, boxWidth, boxHeight);
            // small corner accents, ASCII-box-drawing style
            const int accent = 16;
            g.DrawLine(borderPen, boxX, boxY, boxX + accent, boxY);
            g.DrawLine(borderPen, boxX, boxY, boxX, boxY + accent);
            g.DrawLine(borderPen, boxX + boxWidth, boxY, boxX + boxWidth - accent, boxY);
            g.DrawLine(borderPen, boxX + boxWidth, boxY, boxX + boxWidth, boxY + accent);
            g.DrawLine(borderPen, boxX, boxY + boxHeight, boxX + accent, boxY + boxHeight);
            g.DrawLine(borderPen, boxX, boxY + boxHeight, boxX, boxY + boxHeight - accent);
            g.DrawLine(borderPen, boxX + boxWidth, boxY + boxHeight, boxX + boxWidth - accent, boxY + boxHeight);
            g.DrawLine(borderPen, boxX + boxWidth, boxY + boxHeight, boxX + boxWidth, boxY + boxHeight - accent);

            DrawPixelHeart(g, boxX + boxWidth / 2 - 12, boxY + 16);

            var ty = boxY + glyphAreaHeight;
            foreach (var (text, font, brush) in lines)
            {
                if (text.Length > 0)
                {
                    var w = g.MeasureString(text, font).Width;
                    g.DrawString(text, font, brush, boxX + (boxWidth - w) / 2, ty);
                }
                ty += lineHeight;
            }
        }

        // A small original pixel-art heart - "have fun playing" needed a friendly
        // little glyph and this is generic/geometric enough to carry no resemblance
        // to any specific character or brand.
        private static void DrawPixelHeart(Graphics g, int x, int y)
        {
            using var brush = new SolidBrush(Color.FromArgb(255, 90, 140));
            const int p = 4; // pixel size
            int[,] shape =
            {
                {0,1,1,0,1,1,0},
                {1,1,1,1,1,1,1},
                {1,1,1,1,1,1,1},
                {0,1,1,1,1,1,0},
                {0,0,1,1,1,0,0},
                {0,0,0,1,0,0,0}
            };
            for (int row = 0; row < shape.GetLength(0); row++)
                for (int col = 0; col < shape.GetLength(1); col++)
                    if (shape[row, col] == 1)
                        g.FillRectangle(brush, x + col * p, y + row * p, p, p);
        }
    }
}
