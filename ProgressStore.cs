using System.Text.Json;

namespace GamingStackGUI
{
    /// <summary>
    /// GamingStack's own little save file: which trophies you have earned, how many
    /// times you have run it, your Snake high score and so on. It lives in
    /// %APPDATA%\GamingStack\progress.json - ordinary app data, not a Windows setting -
    /// and exists so there is something to come back to after the PC is set up.
    /// Every call is wrapped so a missing or locked file never breaks the app.
    /// </summary>
    internal static class ProgressStore
    {
        private sealed class Data
        {
            public string FirstRun { get; set; } = "";
            public string LastRunDay { get; set; } = "";
            public int Runs { get; set; }
            public int TotalInstalled { get; set; }
            public int SnakeBest { get; set; }
            public List<string> Unlocked { get; set; } = new();
            public int Streak { get; set; }
            public int BestStreak { get; set; }
            public string LastProfile { get; set; } = "";
            public List<string> LastApps { get; set; } = new();
            public Dictionary<string, int> Scores { get; set; } = new();
            public Dictionary<string, int> IconSlots { get; set; } = new();   // desktop icon positions
            public List<string> SecretsFound { get; set; } = new();           // Command Prompt secrets
            // Display Properties
            public string BootTheme { get; set; } = "auto";      // auto | dawn | day | evening | night
            public string BootStyle { get; set; } = "classic";   // classic | landscape
            public string UiScale { get; set; } = "auto";        // auto | 1 | 1.5 | 2 | 3
            public int ScreensaverMinutes { get; set; } = 3;     // 0 = off
        }

        private static Data _d = new();
        private static readonly object Gate = new();

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingStack", "progress.json");

        public static int Runs { get { lock (Gate) return _d.Runs; } }
        public static int TotalInstalled { get { lock (Gate) return _d.TotalInstalled; } }
        public static int SnakeBest { get { lock (Gate) return _d.SnakeBest; } }

        public static DateTime? FirstRun
        {
            get
            {
                lock (Gate)
                    return DateTime.TryParse(_d.FirstRun, out var d) ? d : null;
            }
        }

        public static int Streak { get { lock (Gate) return _d.Streak; } }
        public static int BestStreak { get { lock (Gate) return _d.BestStreak; } }
        public static string LastProfile { get { lock (Gate) return _d.LastProfile; } }
        public static IReadOnlyList<string> LastApps { get { lock (Gate) return _d.LastApps.ToList(); } }

        public static string BootTheme { get { lock (Gate) return _d.BootTheme; } set { lock (Gate) { _d.BootTheme = value; Save(); } } }
        public static string BootStyle { get { lock (Gate) return _d.BootStyle; } set { lock (Gate) { _d.BootStyle = value; Save(); } } }
        public static string UiScaleSetting { get { lock (Gate) return _d.UiScale; } set { lock (Gate) { _d.UiScale = value; Save(); } } }
        public static int ScreensaverMinutes { get { lock (Gate) return _d.ScreensaverMinutes; } set { lock (Gate) { _d.ScreensaverMinutes = value; Save(); } } }

        /// <summary>Remembers what the last setup was, so "repeat last setup" can offer it again.</summary>
        public static void SaveLastSetup(string profile, IEnumerable<string> appNames)
        {
            lock (Gate) { _d.LastProfile = profile; _d.LastApps = appNames.ToList(); Save(); }
        }

        public static IReadOnlyList<string> SecretsFound { get { lock (Gate) return _d.SecretsFound.ToList(); } }
        public static bool AddSecret(string command)
        {
            lock (Gate)
            {
                if (_d.SecretsFound.Contains(command)) return false;
                _d.SecretsFound.Add(command);
                Save();
                return true;
            }
        }

        public static Dictionary<string, int> IconSlots { get { lock (Gate) return new Dictionary<string, int>(_d.IconSlots); } }
        public static void SaveIconSlots(Dictionary<string, int> slots) { lock (Gate) { _d.IconSlots = new Dictionary<string, int>(slots); Save(); } }
        public static void ResetIconSlots() { lock (Gate) { _d.IconSlots = new Dictionary<string, int>(); Save(); } }

        public static int GetScore(string game) { lock (Gate) return _d.Scores.TryGetValue(game, out var v) ? v : 0; }

        /// <summary>Stores a score if it beats the old one (for games where bigger is better).</summary>
        public static void SetBestScore(string game, int score)
        {
            lock (Gate)
            {
                if (_d.Scores.TryGetValue(game, out var old) && old >= score) return;
                _d.Scores[game] = score;
                Save();
            }
        }

        public static bool IsUnlocked(string id) { lock (Gate) return _d.Unlocked.Contains(id); }

        /// <summary>Loads the file (if any) and counts this run. Returns true when this is
        /// a different calendar day from the last run - i.e. someone came back.</summary>
        public static bool StartSession()
        {
            lock (Gate)
            {
                try
                {
                    if (File.Exists(FilePath))
                        _d = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
                }
                catch { _d = new Data(); }

                var today = DateTime.Now.ToString("yyyy-MM-dd");
                if (string.IsNullOrEmpty(_d.FirstRun)) _d.FirstRun = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                var returning = !string.IsNullOrEmpty(_d.LastRunDay) && _d.LastRunDay != today;
                var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
                if (_d.LastRunDay == yesterday) _d.Streak++;
                else if (_d.LastRunDay != today) _d.Streak = 1;
                else if (_d.Streak == 0) _d.Streak = 1;
                _d.BestStreak = Math.Max(_d.BestStreak, _d.Streak);
                _d.LastRunDay = today;
                _d.Runs++;
                Save();
                return returning;
            }
        }

        /// <summary>Records a trophy. Returns true if it was new.</summary>
        public static bool Unlock(string id)
        {
            lock (Gate)
            {
                if (_d.Unlocked.Contains(id)) return false;
                _d.Unlocked.Add(id);
                Save();
                return true;
            }
        }

        public static void AddInstalled(int count)
        {
            if (count <= 0) return;
            lock (Gate) { _d.TotalInstalled += count; Save(); }
        }

        public static void SetSnakeBest(int score)
        {
            lock (Gate)
            {
                if (score <= _d.SnakeBest) return;
                _d.SnakeBest = score;
                Save();
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_d, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* progress is a nice-to-have - never let it break anything */ }
        }
    }
}
