using Microsoft.Win32;

namespace GamingStackGUI
{
    /// <summary>One registry value a cleanup item sets (always a per-user DWORD under HKEY_CURRENT_USER).</summary>
    public readonly record struct CleanupValue(string SubKey, string Name, int NewValue);

    /// <summary>
    /// One optional "Windows cleanup" setting. <see cref="Effect"/> says in plain words what
    /// changes on screen; <see cref="Values"/> are the exact registry values (nothing hidden).
    /// Every item follows the project rule: shown with its real current value, ticked by the
    /// user (nothing is ticked by default), recorded in the log, and undone by the undo script.
    /// </summary>
    public sealed record CleanupItem(string Id, string Name, string Effect, IReadOnlyList<CleanupValue> Values);

    /// <summary>
    /// The optional Windows cleanup extra. Ideas inspired by the TBOK Windows Performance
    /// Optimizer by The Beard of Knowledge (https://github.com/TheBeardofKnowledge/TBOK-Win11Optimizer).
    /// That project has no licence, so none of its code or text is used: these are documented,
    /// standard per-user Windows settings, written and checked from scratch. Deliberately limited
    /// to the current user's own settings - no services, no policies for all users, no package
    /// removal (the opt-in debloat screen already covers apps).
    /// </summary>
    public static class CleanupCatalog
    {
        private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        private const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        // Max 9 items: the wizard picks them with the number keys 1-9.
        public static readonly IReadOnlyList<CleanupItem> All = new List<CleanupItem>
        {
            new("endtask", "End Task on the taskbar",
                "Adds \"End task\" to the right-click menu of taskbar apps, so a frozen app can be closed in one click.",
                new[] { new CleanupValue(Adv + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1) }),

            new("websearch", "No web results in Start search",
                "Start search only looks at your PC (apps, files, settings), not the web.",
                new[] { new CleanupValue(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1) }),

            new("lockscreen", "No tips or ads on the lock screen",
                "Hides \"fun facts\" and promotional tips on the lock screen.",
                new[]
                {
                    new CleanupValue(Cdm, "RotatingLockScreenOverlayEnabled", 0),
                    new CleanupValue(Cdm, "SubscribedContent-338387Enabled", 0)
                }),

            new("settingsads", "No suggestions in Settings and notifications",
                "Stops \"tips\", \"suggested\" content in the Settings app and Windows welcome-tip notifications.",
                new[]
                {
                    new CleanupValue(Cdm, "SubscribedContent-338393Enabled", 0),
                    new CleanupValue(Cdm, "SubscribedContent-353694Enabled", 0),
                    new CleanupValue(Cdm, "SubscribedContent-353696Enabled", 0),
                    new CleanupValue(Cdm, "SubscribedContent-338389Enabled", 0)
                }),

            new("autoapps", "Stop Windows quietly installing \"suggested\" apps",
                "Windows will no longer add promoted apps and OEM extras to your account in the background.",
                new[]
                {
                    new CleanupValue(Cdm, "SilentInstalledAppsEnabled", 0),
                    new CleanupValue(Cdm, "PreInstalledAppsEnabled", 0),
                    new CleanupValue(Cdm, "OemPreInstalledAppsEnabled", 0)
                }),

            new("finishsetup", "No \"Finish setting up your PC\" nags",
                "Stops the repeating full-screen and notification prompts to finish Windows setup.",
                new[] { new CleanupValue(@"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0) }),

            new("adtracking", "Turn off the advertising ID and tailored experiences",
                "Apps can no longer use a personal advertising ID, and Windows won't tailor tips and ads from your diagnostic data.",
                new[]
                {
                    new CleanupValue(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0),
                    new CleanupValue(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0)
                }),

            new("thispc", "File Explorer opens to \"This PC\"",
                "Explorer starts on your drives instead of the \"Home\" page with recent and suggested files.",
                new[] { new CleanupValue(Adv, "LaunchTo", 1) }),

            new("taskview", "Hide the Task View button",
                "Removes the Task View button from the taskbar (Win+Tab and Alt+Tab still work).",
                new[] { new CleanupValue(Adv, "ShowTaskViewButton", 0) }),
        };

        /// <summary>The value as it is on this PC right now, or null if it is not set.</summary>
        public static int? ReadCurrent(CleanupValue v)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(v.SubKey);
                var raw = key?.GetValue(v.Name);
                return raw == null ? null : Convert.ToInt32(raw);
            }
            catch { return null; }
        }

        /// <summary>True when every value already has the wanted setting (nothing left to do).</summary>
        public static bool IsAlreadyApplied(CleanupItem item) =>
            item.Values.All(v => ReadCurrent(v) == v.NewValue);

        /// <summary>Items that would actually change something on this PC (read only).</summary>
        public static IReadOnlyList<CleanupItem> Scan() => All.Where(i => !IsAlreadyApplied(i)).ToList();

        /// <summary>Plain-words description of what the item's values are now, for the consent screen.</summary>
        public static string DescribeCurrent(CleanupItem item)
        {
            var current = item.Values.Select(ReadCurrent).ToList();
            if (current.All(c => c == null)) return "not set (Windows default)";
            return string.Join(", ", item.Values.Zip(current, (v, c) => $"{v.Name}={(c?.ToString() ?? "not set")}"));
        }

        /// <summary>Plain-words description of what the item will be set to.</summary>
        public static string DescribeNew(CleanupItem item) =>
            string.Join(", ", item.Values.Select(v => $"{v.Name}={v.NewValue}"));
    }
}
