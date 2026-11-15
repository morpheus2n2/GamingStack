using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GamingStackGUI
{
    /// <summary>One place the user can pick for the Captures folder.</summary>
    public sealed record CaptureLocation(string Path, string Label, long FreeBytes);

    /// <summary>
    /// Moves the Windows "Captures" folder (Xbox Game Bar clips and screenshots, normally under the
    /// user's Videos folder, which OneDrive often takes over) and the "Screenshots" folder (Win+PrtScn)
    /// to a folder the user chooses, for example on a big second drive.
    /// It uses Windows' own documented call (SHSetKnownFolderPath), the same thing the folder's
    /// Properties > Location tab does. Existing files are NOT moved or deleted - only where new ones go.
    /// The old value is read first so the undo script can put it back exactly.
    /// </summary>
    public static class CapturesFolder
    {
        public static readonly Guid CapturesId = new("EDC0FE71-98D8-4F4A-B920-C8DC133CB165");
        public static readonly Guid ScreenshotsId = new("B7BEDE81-DF94-4682-A7D8-57A52620B86F");

        private const string UserShellFoldersKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHSetKnownFolderPath(ref Guid id, uint flags, IntPtr token, string path);

        /// <summary>Where Windows currently sends this folder (expanded), or null if it cannot be read.</summary>
        public static string? GetPath(Guid id)
        {
            try
            {
                var g = id;
                var hr = SHGetKnownFolderPath(ref g, 0, IntPtr.Zero, out var ptr);
                if (hr != 0) return null;
                try { return Marshal.PtrToStringUni(ptr); }
                finally { Marshal.FreeCoTaskMem(ptr); }
            }
            catch { return null; }
        }

        /// <summary>The raw (unexpanded, e.g. %USERPROFILE%\Videos\Captures) value, or null if not set.</summary>
        public static string? GetRawSetting(Guid id)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(UserShellFoldersKey);
                return key?.GetValue("{" + id.ToString().ToUpperInvariant() + "}", null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            }
            catch { return null; }
        }

        /// <summary>The undo line for this folder: puts the old raw value back, or removes ours if there was none.</summary>
        public static string UndoLine(Guid id)
        {
            var name = "{" + id.ToString().ToUpperInvariant() + "}";
            var raw = GetRawSetting(id);
            // In a .cmd file a literal % must be written %%
            return raw == null
                ? $@"reg delete ""HKCU\{UserShellFoldersKey}"" /v {name} /f"
                : $@"reg add ""HKCU\{UserShellFoldersKey}"" /v {name} /t REG_EXPAND_SZ /d ""{raw.Replace("%", "%%")}"" /f";
        }

        /// <summary>Creates the folder if needed, then points the Windows known folder at it.</summary>
        public static void Move(Guid id, string newPath)
        {
            Directory.CreateDirectory(newPath);
            var g = id;
            var hr = SHSetKnownFolderPath(ref g, 0, IntPtr.Zero, newPath);
            if (hr != 0) throw new IOException($"Windows refused the change (error 0x{hr:X8})");
        }

        /// <summary>Screenshots go in a "Screenshots" folder inside the chosen Captures folder.</summary>
        public static string ScreenshotsFor(string capturesPath) => Path.Combine(capturesPath, "Screenshots");

        /// <summary>
        /// Suggestions for the picker: one "Captures" folder per fixed drive that is ready, biggest free space
        /// first (a pure read - nothing is created until the user agrees on the final check).
        /// </summary>
        public static List<CaptureLocation> Suggestions()
        {
            var list = new List<CaptureLocation>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    var path = Path.Combine(d.RootDirectory.FullName, "Captures");
                    list.Add(new CaptureLocation(path, $"{path}  ({BackupEngine.FormatBytes(d.AvailableFreeSpace)} free on {d.Name.TrimEnd('\\')}{(string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : " " + d.VolumeLabel)})", d.AvailableFreeSpace));
                }
            }
            catch { /* an unreadable drive just isn't offered */ }
            return list.OrderByDescending(l => l.FreeBytes).ToList();
        }
    }
}
