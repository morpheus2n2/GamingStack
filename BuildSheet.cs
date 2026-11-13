using System.Linq;
using System.Text;

namespace GamingStackGUI
{
    /// <summary>
    /// Writes a plain-text "build sheet" after a run: what was installed, what
    /// failed, what was skipped, and where the undo script lives. It only ever
    /// writes one text file into Documents\GamingStack - nothing else on the PC is
    /// touched, and the path is shown on the summary screen.
    /// </summary>
    internal static class BuildSheet
    {
        public static string? Write(IReadOnlyCollection<ItemResult> results, bool tweaksApplied,
            string? revertScript, string? logFile, DnsChoice dns, int debloatCount, string profileTitle)
        {
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var dir = Path.Combine(docs, "GamingStack");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"build-sheet_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                var sb = new StringBuilder();
                sb.AppendLine("GamingStack build sheet");
                sb.AppendLine($"Made {DateTime.Now:yyyy-MM-dd HH:mm} on {Environment.MachineName}");
                sb.AppendLine($"Setup type: {profileTitle}");
                sb.AppendLine(new string('=', 48));
                sb.AppendLine();

                void Section(string title, InstallResultKind kind)
                {
                    var items = results.Where(r => r.Kind == kind).ToList();
                    sb.AppendLine($"{title} ({items.Count})");
                    foreach (var r in items)
                        sb.AppendLine($"  - {r.Entry.FriendlyName}" + (string.IsNullOrEmpty(r.Detail) ? "" : $"  [{r.Detail}]"));
                    if (items.Count == 0) sb.AppendLine("  (none)");
                    sb.AppendLine();
                }

                Section("Installed", InstallResultKind.Installed);
                Section("Opened in your browser (finish installing these yourself)", InstallResultKind.Opened);
                Section("Already there / skipped", InstallResultKind.Skipped);
                Section("Needs your attention", InstallResultKind.Failed);

                sb.AppendLine("System changes");
                sb.AppendLine(tweaksApplied ? "  Tweaks: applied (you said yes)" : "  Tweaks: skipped (you said no)");
                if (tweaksApplied)
                {
                    sb.AppendLine($"  DNS: {(dns == DnsChoice.Skip ? "left alone" : dns.ToString())}");
                    sb.AppendLine($"  Preinstalled apps removed: {debloatCount}");
                    if (revertScript != null) sb.AppendLine($"  Undo script: {revertScript}");
                }
                if (logFile != null) sb.AppendLine($"  Full log: {logFile}");

                File.WriteAllText(path, sb.ToString());
                return path;
            }
            catch
            {
                return null;
            }
        }
    }
}
