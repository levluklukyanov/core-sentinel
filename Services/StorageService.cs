using System.Diagnostics;
using System.IO;

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

        // 1. User Temp (%TEMP%)
        string userTemp = Path.GetTempPath();
        if (Directory.Exists(userTemp))
        {
            var (bytes, count) = GetDirectorySizeSafe(userTemp);
            targets.Add(new CleanableTarget(isEn ? "User Temporary Files (%TEMP%)" : "Пользовательский кэш (%TEMP%)", userTemp, bytes, count));
        }

        // 2. Windows Temp
        string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        if (Directory.Exists(winTemp))
        {
            var (bytes, count) = GetDirectorySizeSafe(winTemp);
            targets.Add(new CleanableTarget(isEn ? "Windows System Temp Cache" : "Системный кэш Windows Temp", winTemp, bytes, count));
        }

        // 3. Windows Update Download Cache (clean.bat step 2)
        string swDist = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
        if (Directory.Exists(swDist))
        {
            var (bytes, count) = GetDirectorySizeSafe(swDist);
            targets.Add(new CleanableTarget(isEn ? "Windows Update Download Cache" : "Кэш обновлений Windows (SoftwareDistribution)", swDist, bytes, count));
        }

        // 4. Delivery Optimization Cache (clean.bat step 2)
        string delivOpt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "DeliveryOptimization");
        if (Directory.Exists(delivOpt))
        {
            var (bytes, count) = GetDirectorySizeSafe(delivOpt);
            targets.Add(new CleanableTarget(isEn ? "Delivery Optimization Peer Cache" : "Кэш доставки обновлений (DeliveryOptimization)", delivOpt, bytes, count));
        }

        // 5. Crash Dumps
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string crashDumps = Path.Combine(localAppData, "CrashDumps");
        if (Directory.Exists(crashDumps))
        {
            var (bytes, count) = GetDirectorySizeSafe(crashDumps);
            targets.Add(new CleanableTarget(isEn ? "System Crash Dumps (CrashDumps)" : "Дампы системных сбоев (CrashDumps)", crashDumps, bytes, count));
        }

        // 6. Windows Error Reporting (WER)
        string wer = Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportArchive");
        if (Directory.Exists(wer))
        {
            var (bytes, count) = GetDirectorySizeSafe(wer);
            targets.Add(new CleanableTarget(isEn ? "Error Reporting Archive (WER)" : "Архив отчетов об ошибках (WER)", wer, bytes, count));
        }

        // 7. System Diagnostic Logs (clean.bat step 3)
        string cbsLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS");
        if (Directory.Exists(cbsLogs))
        {
            var (bytes, count) = GetDirectorySizeSafe(cbsLogs);
            targets.Add(new CleanableTarget(isEn ? "Windows CBS & Diagnostic Logs" : "Системные логи Windows (CBS / Logs)", cbsLogs, bytes, count));
        }

        // 8. Recycle Bin (clean.bat step 5)
        long binBytes = 0;
        int binCount = 0;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            try
            {
                string rbPath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                if (Directory.Exists(rbPath))
                {
                    var (b, c) = GetDirectorySizeSafe(rbPath);
                    binBytes += b;
                    binCount += c;
                }
            }
            catch { }
        }
        if (binBytes > 0 || binCount > 0)
        {
            targets.Add(new CleanableTarget(isEn ? "Windows Recycle Bin (All Drives)" : "Корзина Windows (Все диски)", "$Recycle.Bin", binBytes, binCount));
        }

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

        // Stage 1: Temp Folders
        logCallback(isEn 
            ? "[1/6] Cleaning temporary folders (%TEMP% and Windows Temp)..." 
            : "[1/6] Очистка временных папок (%TEMP% и Windows Temp)...");

        string userTemp = Path.GetTempPath();
        var r1 = CleanDirectoryContents(userTemp);
        string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        var r2 = CleanDirectoryContents(winTemp);
        totalFreed += r1.BytesFreed + r2.BytesFreed;
        totalRemoved += r1.FilesRemoved + r2.FilesRemoved;
        totalSkipped += r1.FilesSkipped + r2.FilesSkipped;

        // Stage 2: Windows Update & Delivery Optimization
        logCallback(isEn 
            ? "[2/6] Cleaning Windows Update and Delivery Optimization caches..." 
            : "[2/6] Очистка кэша обновлений Windows и Delivery Optimization...");

        string swDist = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
        var r3 = CleanDirectoryContents(swDist);
        string delivOpt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "DeliveryOptimization");
        var r4 = CleanDirectoryContents(delivOpt);
        totalFreed += r3.BytesFreed + r4.BytesFreed;
        totalRemoved += r3.FilesRemoved + r4.FilesRemoved;
        totalSkipped += r3.FilesSkipped + r4.FilesSkipped;

        // Stage 3: Error Reports and Logs
        logCallback(isEn 
            ? "[3/6] Clearing error reports and system diagnostic logs..." 
            : "[3/6] Очистка системных отчетов об ошибках и логов...");

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

        // Stage 4: DNS Cache Flush
        logCallback(isEn 
            ? "[4/6] Flushing network DNS resolver cache..." 
            : "[4/6] Сброс сетевого DNS-кэша...");

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

        // Stage 5: Empty Recycle Bin
        logCallback(isEn 
            ? "[5/6] Emptying Windows Recycle Bin across all drives..." 
            : "[5/6] Очистка Корзины на всех накопителях...");

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

        // Stage 6: Component Store Cleanup (DISM)
        logCallback(isEn 
            ? "[6/6] Optimizing Windows Component Store (DISM)..." 
            : "[6/6] Оптимизация базы компонентов (DISM)...");

        try
        {
            string dismPath = Path.Combine(Environment.SystemDirectory, "dism.exe");
            if (File.Exists(dismPath))
            {
                var psi = new ProcessStartInfo(dismPath, "/online /cleanup-image /startcomponentcleanup")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                    try
                    {
                        await p.WaitForExitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        try { p.Kill(true); } catch { }
                        logCallback(isEn 
                            ? "[6/6] DISM component cleanup timed out (skipped safely)." 
                            : "[6/6] Очистка базы компонентов DISM превысила таймаут (пропущено).");
                    }
                }
            }
        }
        catch { }

        logCallback(isEn 
            ? $"System cleanup completed! Freed: {FormatBytes(totalFreed)}." 
            : $"Очистка завершена! Освобождено: {FormatBytes(totalFreed)}.");

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

    private static (long totalBytes, int fileCount) GetDirectorySizeSafe(string path)
    {
        long total = 0;
        int count = 0;

        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    total += file.Length;
                    count++;
                }
                catch
                {
                    // Skip individual file error
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
