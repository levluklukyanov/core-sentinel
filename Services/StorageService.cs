using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace CoreSentinel.Services;

public record DiskInfo(
    string Name,
    string VolumeLabel,
    long TotalBytes,
    long FreeBytes,
    long UsedBytes,
    double FreePercent,
    double UsedPercent
);

public record CleanableTarget(
    string Name,
    string Path,
    long Bytes,
    int FileCount,
    bool RequiresAdmin = false,
    bool IsSelected = true
);

public record CleanResult(
    long BytesFreed,
    int FilesRemoved,
    int FilesSkipped
);

public record LargeDirectory(
    string Path,
    long SizeBytes,
    string FormattedSize
);

public static class StorageService
{
    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI   = 0x00000002;
    private const uint SHERB_NOSOUND        = 0x00000004;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint GENERIC_DELETE = 0x00010000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool CanDeleteFile(FileInfo file)
    {
        // Live CBS.log is locked by Windows TrustedInstaller permanently
        if (string.Equals(file.Name, "CBS.log", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            IntPtr hFile = CreateFileW(
                file.FullName,
                GENERIC_DELETE,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero
            );

            if (hFile == IntPtr.Zero || hFile == new IntPtr(-1))
            {
                return false;
            }

            CloseHandle(hFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static DiskInfo? GetDriveInfo(string driveLetter = "C")
    {
        try
        {
            string clean = driveLetter.TrimEnd('\\', ':');
            var drive = new DriveInfo($"{clean}:\\");
            if (!drive.IsReady) return null;

            long total = drive.TotalSize;
            long free = drive.AvailableFreeSpace;
            long used = Math.Max(0, total - free);
            double freePct = total > 0 ? (double)free / total * 100.0 : 0.0;
            double usedPct = total > 0 ? (double)used / total * 100.0 : 0.0;

            string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? (clean == "C" ? "System" : "Data") : drive.VolumeLabel;

            return new DiskInfo(
                Name: clean,
                VolumeLabel: label,
                TotalBytes: total,
                FreeBytes: free,
                UsedBytes: used,
                FreePercent: freePct,
                UsedPercent: usedPct
            );
        }
        catch
        {
            return null;
        }
    }

    public static List<CleanableTarget> ScanTempLocations(bool isEn = true)
    {
        var targets = new List<CleanableTarget>();
        bool isAdmin = IsAdministrator();

        // 1. User Temp (%TEMP%)
        string userTemp = Path.GetTempPath();
        if (Directory.Exists(userTemp))
        {
            var (bytes, count) = GetDirectorySizeSafe(userTemp, cleanableOnly: true);
            string name = isEn ? "User Temporary Files (%TEMP%)" : "Пользовательский кэш (%TEMP%)";
            targets.Add(new CleanableTarget(name, userTemp, bytes, count));
        }

        // 2. Windows Temp
        string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        if (Directory.Exists(winTemp))
        {
            var (bytes, count) = GetDirectorySizeSafe(winTemp, cleanableOnly: true);
            string name = isEn ? "Windows System Temp Cache" : "Системный кэш Windows Temp";
            targets.Add(new CleanableTarget(name, winTemp, bytes, count));
        }

        // 3. Windows Update Download Cache
        string swDist = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
        if (Directory.Exists(swDist))
        {
            if (isAdmin)
            {
                var (bytes, count) = GetDirectorySizeSafe(swDist, cleanableOnly: true);
                string name = isEn ? "Windows Update Download Cache" : "Кэш обновлений Windows (SoftwareDistribution)";
                targets.Add(new CleanableTarget(name, swDist, bytes, count));
            }
            else
            {
                var (totalBytes, count) = GetDirectorySizeSafe(swDist, cleanableOnly: false);
                string name = totalBytes > 0
                    ? (isEn ? "Windows Update Cache (Admin Required)" : "Кэш обновлений Windows (Нужны права админа)")
                    : (isEn ? "Windows Update Download Cache" : "Кэш обновлений Windows (SoftwareDistribution)");
                targets.Add(new CleanableTarget(name, swDist, totalBytes, count, RequiresAdmin: totalBytes > 0));
            }
        }

        // 4. Delivery Optimization Cache
        string delivOpt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "DeliveryOptimization");
        if (Directory.Exists(delivOpt))
        {
            if (isAdmin)
            {
                var (bytes, count) = GetDirectorySizeSafe(delivOpt, cleanableOnly: true);
                string name = isEn ? "Delivery Optimization Peer Cache" : "Кэш доставки обновлений (DeliveryOptimization)";
                targets.Add(new CleanableTarget(name, delivOpt, bytes, count));
            }
            else
            {
                var (totalBytes, count) = GetDirectorySizeSafe(delivOpt, cleanableOnly: false);
                if (totalBytes > 0)
                {
                    string name = isEn ? "Delivery Optimization (Admin Required)" : "Кэш доставки обновлений (Нужны права админа)";
                    targets.Add(new CleanableTarget(name, delivOpt, totalBytes, count, RequiresAdmin: true));
                }
            }
        }

        // 5. Crash Dumps
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string crashDumps = Path.Combine(localAppData, "CrashDumps");
        if (Directory.Exists(crashDumps))
        {
            var (bytes, count) = GetDirectorySizeSafe(crashDumps, cleanableOnly: true);
            targets.Add(new CleanableTarget(isEn ? "System Crash Dumps (CrashDumps)" : "Дампы системных сбоев (CrashDumps)", crashDumps, bytes, count));
        }

        // 6. Windows Error Reporting (WER)
        string wer = Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportArchive");
        if (Directory.Exists(wer))
        {
            var (bytes, count) = GetDirectorySizeSafe(wer, cleanableOnly: true);
            targets.Add(new CleanableTarget(isEn ? "Error Reporting Archive (WER)" : "Архив отчетов об ошибках (WER)", wer, bytes, count));
        }

        // 7. System Diagnostic Logs (CBS - live CBS.log excluded)
        string cbsLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS");
        if (Directory.Exists(cbsLogs))
        {
            var (bytes, count) = GetDirectorySizeSafe(cbsLogs, cleanableOnly: true);
            string name = isEn ? "Windows CBS Diagnostic Logs" : "Системные логи Windows (CBS)";
            targets.Add(new CleanableTarget(name, cbsLogs, bytes, count));
        }

        // 8. Recycle Bin
        long binBytes = 0;
        int binCount = 0;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            try
            {
                string rbPath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                if (Directory.Exists(rbPath))
                {
                    var (b, c) = GetDirectorySizeSafe(rbPath, cleanableOnly: true);
                    binBytes += b;
                    binCount += c;
                }
            }
            catch { }
        }
        targets.Add(new CleanableTarget(isEn ? "Windows Recycle Bin (All Drives)" : "Корзина Windows (Все диски)", "$Recycle.Bin", binBytes, binCount));

        return targets;
    }

    public static CleanResult CleanDirectoryContents(string path)
    {
        long freed = 0;
        int count = 0;
        int skipped = 0;

        if (!Directory.Exists(path)) return new CleanResult(0, 0, 0);

        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    // Live CBS.log is locked by Windows TrustedInstaller and can never be deleted while running
                    if (string.Equals(file.Name, "CBS.log", StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        continue;
                    }

                    // Remove ReadOnly attribute if set
                    if ((file.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        file.Attributes &= ~FileAttributes.ReadOnly;
                    }

                    long sz = file.Length;
                    file.Delete();
                    freed += sz;
                    count++;
                }
                catch
                {
                    skipped++;
                }
            }

            foreach (var sub in dir.EnumerateDirectories())
            {
                try
                {
                    sub.Delete(true);
                }
                catch { }
            }
        }
        catch { }

        return new CleanResult(freed, count, skipped);
    }

    public static async Task<CleanResult> ExecuteDeepCleanupAsync(bool isEn, Action<string> logCallback)
    {
        long totalFreed = 0;
        int totalRemoved = 0;
        int totalSkipped = 0;
        bool isAdmin = IsAdministrator();

        // Stage 1: Temp Folders
        logCallback(isEn 
            ? "[1/5] Cleaning temporary folders (%TEMP% and Windows Temp)..." 
            : "[1/5] Очистка временных папок (%TEMP% и Windows Temp)...");

        string userTemp = Path.GetTempPath();
        var r1 = CleanDirectoryContents(userTemp);
        string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        var r2 = CleanDirectoryContents(winTemp);
        totalFreed += r1.BytesFreed + r2.BytesFreed;
        totalRemoved += r1.FilesRemoved + r2.FilesRemoved;
        totalSkipped += r1.FilesSkipped + r2.FilesSkipped;

        // Stage 2: Windows Update & Delivery Optimization
        string swDist = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
        string delivOpt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "DeliveryOptimization");

        if (isAdmin)
        {
            logCallback(isEn 
                ? "[2/5] Cleaning Windows Update and Delivery Optimization caches..." 
                : "[2/5] Очистка кэша обновлений Windows и Delivery Optimization...");

            // Temporarily unhook Windows Update service to release file locks on downloaded updates
            try
            {
                var psiStop = new ProcessStartInfo("net.exe", "stop wuauserv") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
                using var pStop = Process.Start(psiStop);
                pStop?.WaitForExit(3000);
            }
            catch { }

            var r3 = CleanDirectoryContents(swDist);
            var r4 = CleanDirectoryContents(delivOpt);
            totalFreed += r3.BytesFreed + r4.BytesFreed;
            totalRemoved += r3.FilesRemoved + r4.FilesRemoved;
            totalSkipped += r3.FilesSkipped + r4.FilesSkipped;

            try
            {
                var psiStart = new ProcessStartInfo("net.exe", "start wuauserv") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
                using var pStart = Process.Start(psiStart);
                pStart?.WaitForExit(3000);
            }
            catch { }
        }
        else
        {
            logCallback(isEn 
                ? "[2/5] Windows Update cache requires Administrator privileges (skipped)." 
                : "[2/5] Кэш обновлений Windows требует прав Администратора (пропущено).");
        }

        // Stage 3: Error Reports and Diagnostic Logs (excluding active CBS.log)
        logCallback(isEn 
            ? "[3/5] Clearing error reports and system diagnostic logs..." 
            : "[3/5] Очистка отчетов об ошибках и логов...");

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var r5 = CleanDirectoryContents(Path.Combine(localAppData, "CrashDumps"));
        var r6 = CleanDirectoryContents(Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportArchive"));
        var r7 = CleanDirectoryContents(Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportQueue"));
        var r8 = CleanDirectoryContents(Path.Combine(progData, "Microsoft", "Windows", "WER", "ReportArchive"));
        var r9 = CleanDirectoryContents(Path.Combine(progData, "Microsoft", "Windows", "WER", "ReportQueue"));
        var r10 = CleanDirectoryContents(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS"));
        totalFreed += r5.BytesFreed + r6.BytesFreed + r7.BytesFreed + r8.BytesFreed + r9.BytesFreed + r10.BytesFreed;
        totalRemoved += r5.FilesRemoved + r6.FilesRemoved + r7.FilesRemoved + r8.FilesRemoved + r9.FilesRemoved + r10.FilesRemoved;
        totalSkipped += r5.FilesSkipped + r6.FilesSkipped + r7.FilesSkipped + r8.FilesSkipped + r9.FilesSkipped + r10.FilesSkipped;

        // Stage 4: DNS Cache Flush
        logCallback(isEn 
            ? "[4/5] Flushing network DNS resolver cache..." 
            : "[4/5] Сброс сетевого DNS-кэша...");

        try
        {
            string ipconfigPath = Path.Combine(Environment.SystemDirectory, "ipconfig.exe");
            var psi = new ProcessStartInfo(ipconfigPath, "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
        }
        catch { }

        // Stage 5: Empty Recycle Bin via native Shell32 API (instant, no powershell overhead)
        logCallback(isEn 
            ? "[5/5] Emptying Windows Recycle Bin across all drives..." 
            : "[5/5] Очистка Корзины на всех накопителях...");

        try
        {
            SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        }
        catch
        {
            try
            {
                string psPath = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                var psi = new ProcessStartInfo(psPath, "-NoProfile -NonInteractive -Command \"Clear-RecycleBin -Confirm:$false -ErrorAction SilentlyContinue\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                if (p != null) await p.WaitForExitAsync();
            }
            catch { }
        }

        string skippedDetail = totalSkipped > 0
            ? (isEn ? $" (skipped {totalSkipped} locked/in-use files)" : $" (пропущено {totalSkipped} файлов, занятых системой)")
            : "";

        logCallback(isEn 
            ? $"System cleanup completed! Freed: {FormatBytes(totalFreed)}{skippedDetail}." 
            : $"Очистка завершена! Освобождено: {FormatBytes(totalFreed)}{skippedDetail}.");

        return new CleanResult(totalFreed, totalRemoved, totalSkipped);
    }

    public static List<LargeDirectory> ScanLargeDirectories(string? driveLetter = null, int maxResults = 8)
    {
        var results = new List<LargeDirectory>();
        string drive = !string.IsNullOrWhiteSpace(driveLetter) ? driveLetter.TrimEnd('\\', ':') : HardwareTelemetry.GetSystemDriveLetter();
        string root = $"{drive}:\\";

        try
        {
            var rootDir = new DirectoryInfo(root);
            foreach (var dir in rootDir.EnumerateDirectories())
            {
                // Skip system recovery and hidden volume folders
                if ((dir.Attributes & FileAttributes.System) != 0 && dir.Name.StartsWith("$", StringComparison.Ordinal))
                    continue;

                try
                {
                    long size = GetTopLevelSize(dir);
                    if (size > 50L * 1024 * 1024) // > 50 MB
                    {
                        results.Add(new LargeDirectory(dir.FullName, size, FormatBytes(size)));
                    }
                }
                catch
                {
                    // Skip inaccessible folders
                }
            }
        }
        catch
        {
            // Root enumeration failed
        }

        return results.OrderByDescending(x => x.SizeBytes).Take(maxResults).ToList();
    }

    public static List<LargeDirectory> ScanLargeDirectoriesOnC(int maxResults = 8) =>
        ScanLargeDirectories(null, maxResults);

    private static (long totalBytes, int fileCount) GetDirectorySizeSafe(string path, bool cleanableOnly = true)
    {
        long total = 0;
        int count = 0;

        if (!Directory.Exists(path)) return (0, 0);

        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    if (cleanableOnly && !CanDeleteFile(file))
                        continue;

                    total += file.Length;
                    count++;
                }
                catch
                {
                    // Skip inaccessible individual file
                }
            }
        }
        catch
        {
            // Directory unreadable
        }

        return (total, count);
    }

    private static long GetTopLevelSize(DirectoryInfo dir, int maxFiles = 3000)
    {
        long total = 0;
        int inspected = 0;

        try
        {
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    total += file.Length;
                    inspected++;
                    if (inspected >= maxFiles) break; // Keep scan fast
                }
                catch
                {
                    // Skip locked
                }
            }
        }
        catch
        {
            // Subfolder access denied
        }

        return total;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024.0 && unitIndex < units.Length - 1)
        {
            size /= 1024.0;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }
}
