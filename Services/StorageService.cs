using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace CoreSpectre.Services;

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
    bool IsSelected = true,
    string Key = ""
);

public record CleanResult(
    long BytesFreed,
    int FilesRemoved,
    int FilesSkipped,
    int AdminSkippedStages = 0,
    bool HasErrors = false,
    string SummaryMessage = ""
);

public record StorageCleanupOptions(
    bool CleanTemp = true,
    bool CleanSoftwareDistribution = true,
    bool CleanLogsAndReports = true,
    bool CleanDnsCache = true,
    bool CleanRecycleBin = false,
    bool CleanDismComponentCleanup = true,
    bool CleanDismResetBase = false,
    HashSet<string>? DisabledTargetKeys = null
);

public record LargeDirectory(
    string Path,
    long SizeBytes,
    string FormattedSize
);

public record ElevatedCleanupJob(
    bool IsEnglish,
    bool CleanTemp,
    bool CleanSoftwareDistribution,
    bool CleanLogsAndReports,
    bool CleanDismComponentCleanup,
    bool CleanDismResetBase,
    List<string> DisabledTargetKeys,
    string ProgressLogPath,
    string ResultJsonPath
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

    private static bool IsProtectedSelfExtractionPath(string fullPath)
    {
        try
        {
            string dotNetTemp = Path.GetFullPath(Path.Combine(Path.GetTempPath(), ".net")).TrimEnd(Path.DirectorySeparatorChar);
            string normalized = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar);
            return normalized.Equals(dotNetTemp, StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(dotNetTemp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static List<CleanableTarget> ScanTempLocations(bool isEn = true, UserSettingsModel? settings = null)
    {
        var targets = new List<CleanableTarget>();
        bool isAdmin = IsAdministrator();
        settings ??= SettingsPersistenceService.Load();
        var disabled = new HashSet<string>(settings.DisabledCleanupTargets ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        bool IsSelectedFor(string key, bool groupFlag) => groupFlag && !disabled.Contains(key);

        // 1. User Temp (%TEMP%)
        string userTemp = Path.GetTempPath();
        if (Directory.Exists(userTemp))
        {
            var (bytes, count) = GetDirectorySizeSafe(userTemp, cleanableOnly: true);
            string name = isEn ? "User Temporary Files (%TEMP%)" : "Пользовательский кэш (%TEMP%)";
            targets.Add(new CleanableTarget(name, userTemp, bytes, count, RequiresAdmin: false, IsSelected: IsSelectedFor("user_temp", settings.CleanTempFiles), Key: "user_temp"));
        }

        // 2. Windows Temp
        string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        if (Directory.Exists(winTemp))
        {
            if (isAdmin)
            {
                var (bytes, count) = GetDirectorySizeSafe(winTemp, cleanableOnly: true);
                string name = isEn ? "Windows System Temp Cache" : "Системный кэш Windows Temp";
                targets.Add(new CleanableTarget(name, winTemp, bytes, count, RequiresAdmin: false, IsSelected: IsSelectedFor("win_temp", settings.CleanTempFiles), Key: "win_temp"));
            }
            else
            {
                var (totalBytes, count) = GetDirectorySizeSafe(winTemp, cleanableOnly: false);
                string name = isEn ? "Windows System Temp (Admin Required)" : "Системный кэш Windows Temp (Нужны права админа)";
                targets.Add(new CleanableTarget(name, winTemp, totalBytes, count, RequiresAdmin: true, IsSelected: IsSelectedFor("win_temp", settings.CleanTempFiles), Key: "win_temp"));
            }
        }

        // 3. Windows Update Download Cache
        string swDist = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
        if (Directory.Exists(swDist))
        {
            if (isAdmin)
            {
                var (bytes, count) = GetDirectorySizeSafe(swDist, cleanableOnly: true);
                string name = isEn ? "Windows Update Download Cache" : "Кэш обновлений Windows (SoftwareDistribution)";
                targets.Add(new CleanableTarget(name, swDist, bytes, count, RequiresAdmin: false, IsSelected: IsSelectedFor("sw_dist", settings.CleanSoftwareDistribution), Key: "sw_dist"));
            }
            else
            {
                var (totalBytes, count) = GetDirectorySizeSafe(swDist, cleanableOnly: false);
                string name = totalBytes > 0
                    ? (isEn ? "Windows Update Cache (Admin Required)" : "Кэш обновлений Windows (Нужны права админа)")
                    : (isEn ? "Windows Update Download Cache" : "Кэш обновлений Windows (SoftwareDistribution)");
                targets.Add(new CleanableTarget(name, swDist, totalBytes, count, RequiresAdmin: totalBytes > 0, IsSelected: IsSelectedFor("sw_dist", settings.CleanSoftwareDistribution), Key: "sw_dist"));
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
                targets.Add(new CleanableTarget(name, delivOpt, bytes, count, RequiresAdmin: false, IsSelected: IsSelectedFor("deliv_opt", settings.CleanSoftwareDistribution), Key: "deliv_opt"));
            }
            else
            {
                var (totalBytes, count) = GetDirectorySizeSafe(delivOpt, cleanableOnly: false);
                if (totalBytes > 0)
                {
                    string name = isEn ? "Delivery Optimization (Admin Required)" : "Кэш доставки обновлений (Нужны права админа)";
                    targets.Add(new CleanableTarget(name, delivOpt, totalBytes, count, RequiresAdmin: true, IsSelected: IsSelectedFor("deliv_opt", settings.CleanSoftwareDistribution), Key: "deliv_opt"));
                }
            }
        }

        // 5. Crash Dumps
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string crashDumps = Path.Combine(localAppData, "CrashDumps");
        if (Directory.Exists(crashDumps))
        {
            var (bytes, count) = GetDirectorySizeSafe(crashDumps, cleanableOnly: true);
            targets.Add(new CleanableTarget(isEn ? "System Crash Dumps (CrashDumps)" : "Дампы системных сбоев (CrashDumps)", crashDumps, bytes, count, RequiresAdmin: false, IsSelected: IsSelectedFor("crash_dumps", settings.CleanLogsAndReports), Key: "crash_dumps"));
        }

        // 6. Windows Error Reporting (WER)
        string wer = Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportArchive");
        if (Directory.Exists(wer))
        {
            var (bytes, count) = GetDirectorySizeSafe(wer, cleanableOnly: true);
            targets.Add(new CleanableTarget(isEn ? "Error Reporting Archive (WER)" : "Архив отчетов об ошибках (WER)", wer, bytes, count, RequiresAdmin: false, IsSelected: IsSelectedFor("wer", settings.CleanLogsAndReports), Key: "wer"));
        }

        // 7. System Diagnostic Logs (CBS - live CBS.log excluded)
        string cbsLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS");
        if (Directory.Exists(cbsLogs))
        {
            if (isAdmin)
            {
                var (bytes, count) = GetDirectorySizeSafe(cbsLogs, cleanableOnly: true);
                string name = isEn ? "Windows CBS Diagnostic Logs" : "Системные логи Windows (CBS)";
                targets.Add(new CleanableTarget(name, cbsLogs, bytes, count, RequiresAdmin: false, IsSelected: IsSelectedFor("cbs_logs", settings.CleanLogsAndReports), Key: "cbs_logs"));
            }
            else
            {
                var (totalBytes, count) = GetDirectorySizeSafe(cbsLogs, cleanableOnly: false);
                string name = isEn ? "Windows CBS Logs (Admin Required)" : "Системные логи Windows (CBS) (Нужны права админа)";
                targets.Add(new CleanableTarget(name, cbsLogs, totalBytes, count, RequiresAdmin: true, IsSelected: IsSelectedFor("cbs_logs", settings.CleanLogsAndReports), Key: "cbs_logs"));
            }
        }

        // 8. Recycle Bin
        var (binBytes, binCount) = GetRecycleBinSize();
        targets.Add(new CleanableTarget(isEn ? "Windows Recycle Bin (All Drives)" : "Корзина Windows (Все диски)", "$Recycle.Bin", binBytes, binCount, RequiresAdmin: false, IsSelected: IsSelectedFor("recycle_bin", settings.CleanRecycleBin), Key: "recycle_bin"));

        return targets;
    }

    private static (long bytes, int count) GetRecycleBinSize()
    {
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
            catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
        }
        return (binBytes, binCount);
    }

    public static CleanResult CleanDirectoryContents(string path)
    {
        long freed = 0;
        int count = 0;
        int skipped = 0;

        if (!Directory.Exists(path)) return new CleanResult(0, 0, 0);

        var enumOpts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.EnumerateFiles("*", enumOpts))
            {
                try
                {
                    if (IsProtectedSelfExtractionPath(file.FullName))
                    {
                        continue;
                    }

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
                    if ((sub.Attributes & FileAttributes.ReparsePoint) != 0 || IsProtectedSelfExtractionPath(sub.FullName))
                    {
                        continue;
                    }
                    sub.Delete(true);
                }
                catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
            }
        }
        catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }

        return new CleanResult(freed, count, skipped);
    }

    public static async Task<CleanResult> ExecuteDeepCleanupAsync(bool isEn, Action<string> logCallback, StorageCleanupOptions? options = null, bool adminStagesOnly = false)
    {
        var loadedSettings = SettingsPersistenceService.Load();
        if (options == null)
        {
            options = new StorageCleanupOptions(
                loadedSettings.CleanTempFiles,
                loadedSettings.CleanSoftwareDistribution,
                loadedSettings.CleanLogsAndReports,
                loadedSettings.CleanDnsCache,
                loadedSettings.CleanRecycleBin,
                loadedSettings.CleanDismComponentCleanup,
                loadedSettings.CleanDismResetBase,
                new HashSet<string>(loadedSettings.DisabledCleanupTargets ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
            );
        }

        var disabled = options.DisabledTargetKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        long totalFreed = 0;
        int totalRemoved = 0;
        int totalSkipped = 0;
        int adminSkippedStages = 0;
        bool hasErrors = false;
        bool isAdmin = IsAdministrator();

        // Stage 0: Restore Point creation
        if (isAdmin)
        {
            logCallback(isEn
                ? "[0/6] Creating Windows System Restore Point for safe rollback..."
                : "[0/6] Создание системной точки восстановления Windows для безопасного отката...");
            var (created, reason) = await CreateRestorePointDetailedAsync("CoreSpectre Before Deep Cleanup");
            if (created)
            {
                logCallback(isEn
                    ? "[Restore Point] Successfully created 'CoreSpectre Before Deep Cleanup'."
                    : "[Точка восстановления] Успешно создана: 'CoreSpectre Before Deep Cleanup'.");
            }
            else
            {
                string detail = string.IsNullOrWhiteSpace(reason)
                    ? (isEn ? "System Protection disabled or 24h checkpoint limit reached" : "защита системы отключена или сработал суточный лимит Windows")
                    : reason;
                logCallback(isEn
                    ? $"[Restore Point] Skipped ({detail})."
                    : $"[Точка восстановления] Пропущено ({detail}).");
            }
        }
        else
        {
            logCallback(isEn
                ? "[0/6] System Restore Point: skipped (administrator privileges required)."
                : "[0/6] Точка восстановления: пропущено (для автоматического создания точки перед очисткой нужны права администратора).");
        }

        // Stage 1: Temp Folders
        bool cleanUserTemp = !adminStagesOnly && options.CleanTemp && !disabled.Contains("user_temp");
        bool cleanWinTemp = options.CleanTemp && !disabled.Contains("win_temp");
        if (cleanUserTemp || cleanWinTemp)
        {
            if (!adminStagesOnly)
            {
                logCallback(isEn
                    ? "[1/6] Cleaning temporary folders (%TEMP% and Windows Temp)..."
                    : "[1/6] Очистка временных папок (%TEMP% и Windows Temp)...");
            }
            else if (cleanWinTemp)
            {
                logCallback(isEn
                    ? "[1/6] Cleaning protected Windows Temp folder..."
                    : "[1/6] Очистка системной папки Windows Temp...");
            }

            if (cleanUserTemp)
            {
                string userTemp = Path.GetTempPath();
                var r1 = CleanDirectoryContents(userTemp);
                totalFreed += r1.BytesFreed;
                totalRemoved += r1.FilesRemoved;
                totalSkipped += r1.FilesSkipped;
            }

            if (cleanWinTemp)
            {
                string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                if (isAdmin)
                {
                    var r2 = CleanDirectoryContents(winTemp);
                    totalFreed += r2.BytesFreed;
                    totalRemoved += r2.FilesRemoved;
                    totalSkipped += r2.FilesSkipped;
                }
                else
                {
                    logCallback(isEn
                        ? "[Windows Temp] Skipped: administrator privileges required."
                        : "[Windows Temp] Пропущено: нужны права администратора.");
                    adminSkippedStages++;
                }
            }
        }
        else if (!adminStagesOnly)
        {
            logCallback(isEn
                ? "[1/6] Temporary folders: Skipped by user configuration."
                : "[1/6] Временные папки: Пропущено по настройке пользователя.");
        }

        // Stage 2: Windows Update & Delivery Optimization
        bool cleanSwDist = options.CleanSoftwareDistribution && !disabled.Contains("sw_dist");
        bool cleanDelivOpt = options.CleanSoftwareDistribution && !disabled.Contains("deliv_opt");
        if (cleanSwDist || cleanDelivOpt)
        {
            string swDist = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
            string delivOpt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "DeliveryOptimization");

            if (isAdmin)
            {
                logCallback(isEn
                    ? "[2/6] Cleaning Windows Update and Delivery Optimization caches..."
                    : "[2/6] Очистка кэша обновлений Windows и Delivery Optimization...");

                string netExe = Path.Combine(Environment.SystemDirectory, "net.exe");
                foreach (var svc in new[] { "wuauserv", "dosvc" })
                {
                    try
                    {
                        var psiStop = new ProcessStartInfo(netExe, $"stop {svc}") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
                        using var pStop = Process.Start(psiStop);
                        pStop?.WaitForExit(2500);
                    }
                    catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
                }

                if (cleanSwDist)
                {
                    var r3 = CleanDirectoryContents(swDist);
                    totalFreed += r3.BytesFreed;
                    totalRemoved += r3.FilesRemoved;
                    totalSkipped += r3.FilesSkipped;
                }
                if (cleanDelivOpt)
                {
                    var r4 = CleanDirectoryContents(delivOpt);
                    totalFreed += r4.BytesFreed;
                    totalRemoved += r4.FilesRemoved;
                    totalSkipped += r4.FilesSkipped;
                }

                try
                {
                    var psiStart = new ProcessStartInfo(netExe, "start wuauserv") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
                    using var pStart = Process.Start(psiStart);
                    pStart?.WaitForExit(2500);
                }
                catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
            }
            else
            {
                logCallback(isEn
                    ? "[SoftwareDistribution] Skipped: administrator privileges required."
                    : "[SoftwareDistribution] Пропущено: нужны права администратора.");
                adminSkippedStages++;
            }
        }
        else
        {
            logCallback(isEn
                ? "[2/6] Windows Update cache: Skipped by user configuration."
                : "[2/6] Кэш обновлений Windows: Пропущено по настройке пользователя.");
        }

        // Stage 3: Error Reports and Diagnostic Logs (excluding active CBS.log)
        bool cleanCrashDumps = !adminStagesOnly && options.CleanLogsAndReports && !disabled.Contains("crash_dumps");
        bool cleanWer = options.CleanLogsAndReports && !disabled.Contains("wer");
        bool cleanCbs = options.CleanLogsAndReports && !disabled.Contains("cbs_logs");
        if (cleanCrashDumps || cleanWer || cleanCbs)
        {
            logCallback(isEn
                ? "[3/6] Clearing error reports and system diagnostic logs..."
                : "[3/6] Очистка отчетов об ошибках и логов...");

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            if (cleanCrashDumps)
            {
                var r5 = CleanDirectoryContents(Path.Combine(localAppData, "CrashDumps"));
                totalFreed += r5.BytesFreed;
                totalRemoved += r5.FilesRemoved;
                totalSkipped += r5.FilesSkipped;
            }

            if (cleanWer)
            {
                if (!adminStagesOnly)
                {
                    var r6 = CleanDirectoryContents(Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportArchive"));
                    var r7 = CleanDirectoryContents(Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportQueue"));
                    totalFreed += r6.BytesFreed + r7.BytesFreed;
                    totalRemoved += r6.FilesRemoved + r7.FilesRemoved;
                    totalSkipped += r6.FilesSkipped + r7.FilesSkipped;
                }

                if (isAdmin)
                {
                    var r8 = CleanDirectoryContents(Path.Combine(progData, "Microsoft", "Windows", "WER", "ReportArchive"));
                    var r9 = CleanDirectoryContents(Path.Combine(progData, "Microsoft", "Windows", "WER", "ReportQueue"));
                    totalFreed += r8.BytesFreed + r9.BytesFreed;
                    totalRemoved += r8.FilesRemoved + r9.FilesRemoved;
                    totalSkipped += r8.FilesSkipped + r9.FilesSkipped;
                }
                else
                {
                    logCallback(isEn
                        ? "[System WER Archive] Skipped: administrator privileges required."
                        : "[Системные отчёты WER] Пропущено: нужны права администратора.");
                    adminSkippedStages++;
                }
            }

            if (cleanCbs)
            {
                string cbsLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS");
                if (isAdmin)
                {
                    var r10 = CleanDirectoryContents(cbsLogs);
                    totalFreed += r10.BytesFreed;
                    totalRemoved += r10.FilesRemoved;
                    totalSkipped += r10.FilesSkipped;
                }
                else
                {
                    logCallback(isEn
                        ? "[CBS Logs] Skipped: administrator privileges required."
                        : "[CBS Logs] Пропущено: нужны права администратора.");
                    adminSkippedStages++;
                }
            }
        }
        else if (!adminStagesOnly)
        {
            logCallback(isEn
                ? "[3/6] Error reports and CBS logs: Skipped by user configuration."
                : "[3/6] Отчеты об ошибках и логи CBS: Пропущено по настройке пользователя.");
        }

        // Stage 4: DNS Cache Flush
        if (!adminStagesOnly)
        {
            if (options.CleanDnsCache)
            {
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
                catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
            }
            else
            {
                logCallback(isEn
                    ? "[4/6] DNS resolver cache: Skipped by user configuration."
                    : "[4/6] Сетевой DNS-кэш: Пропущено по настройке пользователя.");
            }
        }

        // Stage 5: Empty Recycle Bin via native Shell32 API
        if (!adminStagesOnly)
        {
            if (options.CleanRecycleBin && !disabled.Contains("recycle_bin"))
            {
                logCallback(isEn
                    ? "[5/6] Emptying Windows Recycle Bin across all drives..."
                    : "[5/6] Очистка Корзины на всех накопителях...");

                var (binBytesBefore, binCountBefore) = GetRecycleBinSize();
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
                    catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
                }
                var (binBytesAfter, binCountAfter) = GetRecycleBinSize();
                totalFreed += Math.Max(0, binBytesBefore - binBytesAfter);
                totalRemoved += Math.Max(0, binCountBefore - binCountAfter);
            }
            else
            {
                logCallback(isEn
                    ? "[5/6] Recycle Bin: Skipped by user configuration (default: off)."
                    : "[5/6] Корзина: Пропущено по настройке пользователя (по умолчанию выкл).");
            }
        }

        // Stage 6: Windows Component Store Cleanup (DISM)
        if (options.CleanDismComponentCleanup)
        {
            string dismArgs = options.CleanDismResetBase
                ? "/Online /Cleanup-Image /StartComponentCleanup /ResetBase"
                : "/Online /Cleanup-Image /StartComponentCleanup";

            logCallback(isEn 
                ? $"[6/6] Cleaning Windows Component Store (DISM{(options.CleanDismResetBase ? " with /ResetBase" : "")})..." 
                : $"[6/6] Очистка хранилища компонентов Windows (DISM{(options.CleanDismResetBase ? " с /ResetBase" : "")})...");

            if (isAdmin)
            {
                try
                {
                    string dismPath = Path.Combine(Environment.SystemDirectory, "dism.exe");
                    var psi = new ProcessStartInfo(dismPath, dismArgs)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
                        try
                        {
                            await p.WaitForExitAsync(cts.Token);
                            if (p.ExitCode != 0)
                            {
                                hasErrors = true;
                                logCallback(isEn 
                                    ? $"[DISM] Failed with ExitCode {p.ExitCode}." 
                                    : $"[DISM] Завершился с ошибкой (код выхода: {p.ExitCode}).");
                            }
                            else
                            {
                                logCallback(isEn 
                                    ? "[DISM] Component cleanup completed successfully." 
                                    : "[DISM] Очистка хранилища компонентов успешно завершена.");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            hasErrors = true;
                            try { p.Kill(); } catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
                            logCallback(isEn 
                                ? "[DISM] Operation timed out (exceeded 180s)." 
                                : "[DISM] Операция прервана по тайм-ауту (более 180 сек).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    hasErrors = true;
                    logCallback(isEn 
                        ? $"[DISM] Execution error: {ex.Message}" 
                        : $"[DISM] Ошибка запуска DISM: {ex.Message}");
                }
            }
            else
            {
                logCallback(isEn 
                    ? "[DISM] Skipped: administrator privileges required." 
                    : "[DISM] Пропущено: нужны права администратора.");
                adminSkippedStages++;
            }
        }
        else
        {
            logCallback(isEn
                ? "[6/6] DISM Component Cleanup: Skipped by user configuration."
                : "[6/6] Очистка компонентов DISM: Пропущено по настройке пользователя.");
        }

        // Format honest summary message
        string summaryMessage;
        if (adminSkippedStages > 0 || totalSkipped > 0 || hasErrors)
        {
            summaryMessage = isEn
                ? $"Cleanup completed with limitations. Freed: {FormatBytes(totalFreed)}, removed {totalRemoved} files, locked/in-use: {totalSkipped}, skipped due to permissions: {adminSkippedStages} step(s)."
                : $"Очистка выполнена с ограничениями. Освобождено: {FormatBytes(totalFreed)}, удалено файлов: {totalRemoved}, занято/заблокировано: {totalSkipped}, пропущено по правам: {adminSkippedStages} шагов.";
        }
        else
        {
            summaryMessage = isEn
                ? $"Cleanup completed successfully. Freed: {FormatBytes(totalFreed)}, removed {totalRemoved} files."
                : $"Очистка успешно завершена. Освобождено: {FormatBytes(totalFreed)}, удалено файлов: {totalRemoved}.";
        }

        logCallback(summaryMessage);

        return new CleanResult(totalFreed, totalRemoved, totalSkipped, adminSkippedStages, hasErrors, summaryMessage);
    }

    public static async Task<int> RunElevatedWorkerAsync(string jobPath)
    {
        if (!File.Exists(jobPath)) return 2;

        string json = await File.ReadAllTextAsync(jobPath);
        var job = System.Text.Json.JsonSerializer.Deserialize<ElevatedCleanupJob>(json);
        if (job == null) return 3;

        var logLock = new object();
        void AppendProgress(string line)
        {
            lock (logLock)
            {
                try
                {
                    File.AppendAllText(job.ProgressLogPath, line + Environment.NewLine, System.Text.Encoding.UTF8);
                }
                catch { }
            }
        }

        var opts = new StorageCleanupOptions(
            job.CleanTemp,
            job.CleanSoftwareDistribution,
            job.CleanLogsAndReports,
            CleanDnsCache: false,
            CleanRecycleBin: false,
            job.CleanDismComponentCleanup,
            job.CleanDismResetBase,
            new HashSet<string>(job.DisabledTargetKeys ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
        );

        var result = await ExecuteDeepCleanupAsync(job.IsEnglish, AppendProgress, opts, adminStagesOnly: true);
        string resJson = System.Text.Json.JsonSerializer.Serialize(result);
        await File.WriteAllTextAsync(job.ResultJsonPath, resJson, System.Text.Encoding.UTF8);
        return 0;
    }

    public static async Task<bool> CreateRestorePointAsync(string description, bool isEn = false)
    {
        var (created, _) = await CreateRestorePointDetailedAsync(description);
        return created;
    }

    public static async Task<(bool Created, string? Reason)> CreateRestorePointDetailedAsync(string description)
    {
        if (!IsAdministrator()) return (false, "Admin required");

        try
        {
            string psPath = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            var psi = new ProcessStartInfo(psPath, $"-NoProfile -NonInteractive -Command \"Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                string err = await p.StandardError.ReadToEndAsync(cts.Token);
                await p.WaitForExitAsync(cts.Token);
                if (p.ExitCode == 0 && string.IsNullOrWhiteSpace(err))
                {
                    return (true, null);
                }
                string cleanErr = err.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? $"ExitCode {p.ExitCode}";
                return (false, cleanErr);
            }
        }
        catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); return (false, ex.Message); }
        return (false, "Unknown error");
    }

    public static bool LaunchSystemRestore()
    {
        try
        {
            string rstruiPath = Path.Combine(Environment.SystemDirectory, "rstrui.exe");
            if (File.Exists(rstruiPath))
            {
                Process.Start(new ProcessStartInfo(rstruiPath) { UseShellExecute = true });
                return true;
            }
        }
        catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
        return false;
    }

    public static bool LaunchSystemProtectionSettings()
    {
        try
        {
            string exePath = Path.Combine(Environment.SystemDirectory, "SystemPropertiesProtection.exe");
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                return true;
            }
        }
        catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
        return false;
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
            var enumOpts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.EnumerateFiles("*", enumOpts))
            {
                try
                {
                    if (IsProtectedSelfExtractionPath(file.FullName))
                        continue;

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

        return $"{size.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }
}
