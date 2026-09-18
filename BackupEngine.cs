using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace GamingStackGUI
{
    /// <summary>
    /// One drive GamingStack could point a full image backup at - built by
    /// <see cref="BackupEngine.FindEligibleDrives"/>, a pure read that changes
    /// nothing. Deliberately shows both free space and the rough estimate needed
    /// (see <see cref="EstimatedNeededBytes"/>) rather than silently filtering
    /// drives out - the wizard's selection screen discloses both and lets the user
    /// judge, same principle as the tweaks consent screen.
    /// </summary>
    public readonly record struct DriveOption(string RootPath, string Label, long FreeBytes);

    public enum BackupTiming { Skip, Before, After }

    /// <summary>
    /// The "always always happen" System Restore point, plus an optional full
    /// disk-image backup via `wbadmin` (the engine behind the old "Backup and
    /// Restore (Windows 7)" control panel item - still present and working on
    /// Windows 11; there's no newer scriptable equivalent). Deliberately its own
    /// class, decoupled from InstallerEngine via events/return values the same way
    /// InstallerEngine is decoupled from MainForm - this isn't app installation,
    /// it's a separate safety-net concern with its own log file.
    /// </summary>
    public class BackupEngine
    {
        public event Action<string>? OnLog;

        private readonly string _workDir = Path.Combine(Path.GetTempPath(), "GamingStack");
        private string? _logFilePath;
        public string? LogFilePath => _logFilePath;

        public bool RestorePointCreated { get; private set; }
        public string? RestorePointError { get; private set; }

        public bool ImageBackupSucceeded { get; private set; }
        public string? ImageBackupError { get; private set; }
        public string? ImageBackupDestination { get; private set; }

        private void Log(string text)
        {
            OnLog?.Invoke(text);
            if (_logFilePath != null)
            {
                try { File.AppendAllText(_logFilePath, text + Environment.NewLine); }
                catch { /* logging shouldn't itself be fatal */ }
            }
        }

        private void EnsureLogFile()
        {
            if (_logFilePath != null) return;
            try
            {
                var logsDir = Path.Combine(_workDir, "logs");
                Directory.CreateDirectory(logsDir);
                _logFilePath = Path.Combine(logsDir, $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.WriteAllText(_logFilePath,
                    $"GamingStack backup log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { /* if this fails, Log() just no-ops the file side */ }
        }

        private static string SystemDriveRoot =>
            Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";

        /// <summary>
        /// Rough estimate of what a full image backup needs: the used space on the
        /// system drive. Real backups usually compress somewhat, so this is
        /// deliberately a conservative (slightly high) estimate rather than an
        /// optimistic one - shown next to each drive's free space so the user can
        /// judge for themselves instead of GamingStack silently deciding for them.
        /// </summary>
        public static long EstimatedNeededBytes()
        {
            try
            {
                var sys = new DriveInfo(SystemDriveRoot);
                return sys.TotalSize - sys.AvailableFreeSpace;
            }
            catch
            {
                return 0;
            }
        }

        public static string FormatBytes(long bytes)
        {
            double gb = bytes / 1024.0 / 1024.0 / 1024.0;
            return $"{gb:0.#} GB";
        }

        /// <summary>
        /// Any drive `wbadmin` could plausibly target: ready, fixed or removable,
        /// NTFS (wbadmin refuses anything else), and not the system drive itself
        /// (Windows enforces that regardless). A pure read - nothing here changes
        /// anything on the machine.
        /// </summary>
        public static List<DriveOption> FindEligibleDrives()
        {
            var systemRoot = SystemDriveRoot;
            var options = new List<DriveOption>();

            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady) continue;
                        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                        if (string.Equals(drive.RootDirectory.FullName, systemRoot, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) continue;

                        var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                            ? drive.Name
                            : $"{drive.Name} ({drive.VolumeLabel})";
                        options.Add(new DriveOption(drive.RootDirectory.FullName, label, drive.TotalFreeSpace));
                    }
                    catch
                    {
                        // One drive being unreadable (e.g. a card reader with nothing
                        // in it) shouldn't hide every other eligible drive.
                    }
                }
            }
            catch
            {
                // Fall through with whatever was found (possibly nothing) - the
                // wizard already treats an empty list as "skip cleanly".
            }

            return options;
        }

        /// <summary>
        /// The mandatory safety net. Tries the direct WMI call first (works if
        /// System Restore is already on for the system drive); if that fails,
        /// tries enabling System Restore for the system drive first and retries
        /// once - Windows ships with System Protection off by default on most
        /// consumer machines, and the user's own experience is that restore points
        /// silently failing is a real, repeated problem, so this gets one real
        /// retry rather than a single silent attempt. Never throws - failure is
        /// recorded in <see cref="RestorePointError"/> and disclosed, not hidden,
        /// but doesn't block the rest of the run either.
        /// </summary>
        public void CreateRestorePoint()
        {
            EnsureLogFile();
            Log("[backup] Creating a System Restore point...");

            if (TryCreateRestorePointOnce(out var error))
            {
                RestorePointCreated = true;
                Log("[backup] System Restore point created.");
                return;
            }

            Log($"[backup] First attempt failed ({error}) - System Restore may be off for this drive. Enabling it and retrying...");
            try
            {
                var psi = new ProcessStartInfo("powershell",
                    $"-NoProfile -NonInteractive -Command \"Enable-ComputerRestore -Drive '{SystemDriveRoot}'\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(15000);
            }
            catch (Exception ex)
            {
                Log($"[backup] Couldn't enable System Restore: {ex.Message}");
            }

            if (TryCreateRestorePointOnce(out var secondError))
            {
                RestorePointCreated = true;
                Log("[backup] System Restore point created (after enabling System Restore).");
            }
            else
            {
                RestorePointError = secondError;
                Log($"[backup] Restore point still failed: {secondError}. Continuing without one - " +
                    "check System Protection in Windows' own System settings if this keeps happening.");
            }
        }

        private static bool TryCreateRestorePointOnce(out string? error)
        {
            try
            {
                using var mc = new ManagementClass(@"root\default:SystemRestore");
                using var inParams = mc.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = "GamingStack setup";
                inParams["RestorePointType"] = 0;  // APPLICATION_INSTALL
                inParams["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE

                using var outParams = mc.InvokeMethod("CreateRestorePoint", inParams, null);
                var ret = outParams == null ? 1u : Convert.ToUInt32(outParams["ReturnValue"]);
                if (ret == 0)
                {
                    error = null;
                    return true;
                }
                error = $"CreateRestorePoint returned {ret}";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// A full system-image backup via `wbadmin`, blocking until it finishes -
        /// deliberately not fire-and-forget, since a backup racing against
        /// installs/tweaks in the background would capture a half-changed system
        /// rather than a clean before/after snapshot (the whole point of asking
        /// "before or after" in the first place).
        /// </summary>
        public async Task<bool> RunImageBackupAsync(string destinationRoot)
        {
            EnsureLogFile();
            ImageBackupDestination = destinationRoot;
            Log($"[backup] Starting full disk-image backup to {destinationRoot} - this can take a while...");

            try
            {
                var systemDrive = SystemDriveRoot.TrimEnd('\\');
                var target = destinationRoot.TrimEnd('\\');
                var psi = new ProcessStartInfo("wbadmin",
                    $"start backup -backupTarget:{target} -include:{systemDrive} -allCritical -quiet")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) throw new InvalidOperationException("wbadmin failed to start");

                p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log($"[backup] {e.Data}"); };
                p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log($"[backup] {e.Data}"); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                await p.WaitForExitAsync();

                if (p.ExitCode == 0)
                {
                    ImageBackupSucceeded = true;
                    Log("[backup] Full disk-image backup completed.");
                    return true;
                }

                ImageBackupError = $"wbadmin exited with code {p.ExitCode}";
                Log($"[backup] Full disk-image backup failed: exit code {p.ExitCode}");
                return false;
            }
            catch (Exception ex)
            {
                ImageBackupError = ex.Message;
                Log($"[backup] Full disk-image backup failed: {ex.Message}");
                return false;
            }
        }
    }
}
