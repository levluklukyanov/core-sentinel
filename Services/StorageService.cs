using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    bool CleanGpuShaderCache = false,
    bool CleanBrowserCache = false,
    bool CleanDismComponentCleanup = true,
    bool CleanDismResetBase = false,
    HashSet<string>? DisabledTargetKeys = null,
    int TempMinAgeDays = 0
);

public record LargeDirectory(
    string Path,
    long SizeBytes,
    string FormattedSize,
    bool IsBackupNote = false
);

public record TargetFilePreview(
    string Name,
    string FullPath,
    string FormattedSize,
    string LastModified
);

public record ElevatedCleanupJob(
    bool IsEnglish,
    bool CleanTemp,
    bool CleanSoftwareDistribution,
    bool CleanLogsAndReports,
    bool CleanGpuShaderCache,
    bool CleanBrowserCache,
    bool CleanDismComponentCleanup,
    bool CleanDismResetBase,
    List<string> DisabledTargetKeys,
    string Action = "cleanup",
    int TempMinAgeDays = 0
);

public record ElevatedWorkerRunResult(
    bool ElevationCancelled,
    CleanResult? Result,
    string? ErrorMessage
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

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandleW(
        IntPtr hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint GENERIC_DELETE = 0x00010000;
    private const uint FILE_READ_ATTRIBUTES = 0x00000080;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    private static readonly Regex ValidPipeRegex = new(@"^CoreSpectre_Elev_[0-9a-fA-F]{32}$", RegexOptions.Compiled);

    public static bool IsValidPipeName(string? pipeName) =>
        !string.IsNullOrWhiteSpace(pipeName) && ValidPipeRegex.IsMatch(pipeName);

    public static NamedPipeServerStream CreateSecurePipeServer(string pipeName)
    {
        if (!IsValidPipeName(pipeName))
        {
            throw new ArgumentException("Invalid elevated pipe name.", nameof(pipeName));
        }

        var pipeSecurity = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User != null)
        {
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                identity.User,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }

        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            adminsSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 8192,
            outBufferSize: 8192,
            pipeSecurity);
    }

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

    public static string ValidateDriveLetter(string? driveLetter, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(driveLetter))
        {
            string trimmed = driveLetter.Trim().TrimEnd('\\', ':');
            if (trimmed.Length == 1 && char.IsAsciiLetter(trimmed[0]))
            {
                return char.ToUpperInvariant(trimmed[0]).ToString();
            }
        }
        return !string.IsNullOrWhiteSpace(fallback) ? fallback : HardwareTelemetry.GetSystemDriveLetter();
    }

    public static bool CanDeleteFile(FileInfo file)
    {
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
                FILE_FLAG_OPEN_REPARSE_POINT,
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

    private static bool IsFinalPathWithinRoot(string path, string normalizedAllowedRoot, bool isDirectory)
    {
        try
        {
            uint flags = FILE_FLAG_OPEN_REPARSE_POINT | (isDirectory ? FILE_FLAG_BACKUP_SEMANTICS : 0u);
            IntPtr handle = CreateFileW(
                path,
                FILE_READ_ATTRIBUTES,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero,
                OPEN_EXISTING,
                flags,
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return false;

            try
            {
                var sb = new StringBuilder(1024);
                uint len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
                if (len == 0) return false;
                if (len >= sb.Capacity)
                {
                    sb = new StringBuilder((int)len + 2);
                    len = GetFinalPathNameByHandleW(handle, sb, (uint)sb.Capacity, 0);
                    if (len == 0) return false;
                }

                string resolved = sb.ToString();
                if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    resolved = @"\\" + resolved[8..];
                else if (resolved.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                    resolved = resolved[4..];

                resolved = Path.GetFullPath(resolved).TrimEnd(Path.DirectorySeparatorChar);
                return resolved.Equals(normalizedAllowedRoot, StringComparison.OrdinalIgnoreCase) ||
                       resolved.StartsWith(normalizedAllowedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CloseHandle(handle);
            }
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
            string clean = ValidateDriveLetter(driveLetter, "C");
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

    private static IEnumerable<string> GetGpuShaderCacheDirectories()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string localLow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
        return new[]
        {
            Path.Combine(localAppData, "D3DSCache"),
            Path.Combine(localAppData, "NVIDIA", "DXCache"),
            Path.Combine(localAppData, "NVIDIA", "GLCache"),
            Path.Combine(localLow, "NVIDIA", "PerDriverVersion", "DXCache"),
            Path.Combine(localAppData, "AMD", "DxCache"),
            Path.Combine(localAppData, "AMD", "GLCache")
        };
    }

    private static IEnumerable<string> GetBrowserCacheDirectories()
    {
        var dirs = new List<string>();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string[] chromiumRoots =
        [
            Path.Combine(localAppData, "Google", "Chrome", "User Data"),
            Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
            Path.Combine(localAppData, "Yandex", "YandexBrowser", "User Data"),
            Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data")
        ];

        foreach (var root in chromiumRoots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var profile in Directory.EnumerateDirectories(root))
                {
                    string name = Path.GetFileName(profile);
                    if (!name.Equals("Default", StringComparison.OrdinalIgnoreCase) &&
                        !name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string c1 = Path.Combine(profile, "Cache", "Cache_Data");
                    string c2 = Path.Combine(profile, "Code Cache");
                    if (Directory.Exists(c1)) dirs.Add(c1);
                    if (Directory.Exists(c2)) dirs.Add(c2);
                }
            }
            catch { }
        }

        string ffProfiles = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(ffProfiles))
        {
            try
            {
                foreach (var prof in Directory.EnumerateDirectories(ffProfiles))
                {
                    string c2 = Path.Combine(prof, "cache2", "entries");
                    if (Directory.Exists(c2)) dirs.Add(c2);
                }
            }
            catch { }
        }

        return dirs;
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

        // 8. GPU & DirectX Shader Caches
        long gpuBytes = 0;
        int gpuFiles = 0;
        foreach (var dir in GetGpuShaderCacheDirectories())
        {
            var (b, c) = GetDirectorySizeSafe(dir, cleanableOnly: true);
            gpuBytes += b;
            gpuFiles += c;
        }
        targets.Add(new CleanableTarget(
            isEn ? "GPU & DirectX Shader Caches" : "Кэш шейдеров GPU и DirectX",
            "D3DSCache / NVIDIA / AMD",
            gpuBytes,
            gpuFiles,
            RequiresAdmin: false,
            IsSelected: IsSelectedFor("gpu_shader", settings.CleanGpuShaderCache),
            Key: "gpu_shader"));

        // 9. Browser HTTP Caches (Chrome, Edge, Yandex, Brave)
        long browserBytes = 0;
        int browserFiles = 0;
        foreach (var dir in GetBrowserCacheDirectories())
        {
            var (b, c) = GetDirectorySizeSafe(dir, cleanableOnly: true);
            browserBytes += b;
            browserFiles += c;
        }
        targets.Add(new CleanableTarget(
            isEn ? "Browser HTTP Caches (Chrome/Edge/Yandex)" : "Кэш браузеров (Chrome/Edge/Yandex)",
            "User Data\\Default\\Cache",
            browserBytes,
            browserFiles,
            RequiresAdmin: false,
            IsSelected: IsSelectedFor("browser_cache", settings.CleanBrowserCache),
            Key: "browser_cache"));

        // 10. Recycle Bin
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

    public static CleanResult CleanDirectoryContents(string path, CancellationToken cancellationToken = default, int minAgeDays = 0)
    {
        long freed = 0;
        int count = 0;
        int skipped = 0;
        DateTime? cutoffUtc = minAgeDays > 0 ? DateTime.UtcNow.AddDays(-minAgeDays) : null;

        if (!Directory.Exists(path)) return new CleanResult(0, 0, 0);

        string allowedRoot;
        try
        {
            allowedRoot = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch
        {
            return new CleanResult(0, 0, 0);
        }

        if (!IsFinalPathWithinRoot(allowedRoot, allowedRoot, isDirectory: true))
        {
            Logger.Warn($"[StorageService] Skipped cleanup root (reparse/path mismatch): {allowedRoot}");
            return new CleanResult(0, 0, 0);
        }

        var dirStack = new Stack<string>();
        var dirsBottomUp = new List<string>();
        dirStack.Push(allowedRoot);

        while (dirStack.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested) break;
            string currentDir = dirStack.Pop();

            try
            {
                var currentInfo = new DirectoryInfo(currentDir);
                if ((currentInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    IsProtectedSelfExtractionPath(currentDir) ||
                    !IsFinalPathWithinRoot(currentDir, allowedRoot, isDirectory: true))
                {
                    continue;
                }

                if (!string.Equals(currentDir, allowedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    dirsBottomUp.Add(currentDir);
                }

                foreach (var file in currentInfo.EnumerateFiles())
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        if ((file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                            IsProtectedSelfExtractionPath(file.FullName))
                        {
                            continue;
                        }

                        if (string.Equals(file.Name, "CBS.log", StringComparison.OrdinalIgnoreCase))
                        {
                            skipped++;
                            continue;
                        }

                        if (!IsFinalPathWithinRoot(file.FullName, allowedRoot, isDirectory: false))
                        {
                            skipped++;
                            continue;
                        }

                        if (cutoffUtc.HasValue && file.LastWriteTimeUtc > cutoffUtc.Value)
                        {
                            continue;
                        }

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

                foreach (var sub in currentInfo.EnumerateDirectories())
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) == 0 &&
                            !IsProtectedSelfExtractionPath(sub.FullName) &&
                            IsFinalPathWithinRoot(sub.FullName, allowedRoot, isDirectory: true))
                        {
                            dirStack.Push(sub.FullName);
                        }
                    }
                    catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
                }
            }
            catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
        }

        // Remove emptied subdirectories bottom-up using non-recursive deletion (no junction traversal)
        for (int i = dirsBottomUp.Count - 1; i >= 0; i--)
        {
            if (cancellationToken.IsCancellationRequested) break;
            string subPath = dirsBottomUp[i];
            try
            {
                if (Directory.Exists(subPath) && IsFinalPathWithinRoot(subPath, allowedRoot, isDirectory: true))
                {
                    var di = new DirectoryInfo(subPath);
                    if ((di.Attributes & FileAttributes.ReparsePoint) == 0 &&
                        !di.EnumerateFileSystemInfos().Any())
                    {
                        Directory.Delete(subPath, recursive: false);
                    }
                }
            }
            catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
        }

        return new CleanResult(freed, count, skipped);
    }

    public static async Task<CleanResult> ExecuteDeepCleanupAsync(
        bool isEn,
        Action<string> logCallback,
        StorageCleanupOptions? options = null,
        bool adminStagesOnly = false,
        CancellationToken cancellationToken = default)
    {
        var loadedSettings = SettingsPersistenceService.Load();
        options ??= new StorageCleanupOptions(
            loadedSettings.CleanTempFiles,
            loadedSettings.CleanSoftwareDistribution,
            loadedSettings.CleanLogsAndReports,
            loadedSettings.CleanDnsCache,
            loadedSettings.CleanRecycleBin,
            loadedSettings.CleanGpuShaderCache,
            loadedSettings.CleanBrowserCache,
            loadedSettings.CleanDismComponentCleanup,
            loadedSettings.CleanDismResetBase,
            new HashSet<string>(loadedSettings.DisabledCleanupTargets ?? new List<string>(), StringComparer.OrdinalIgnoreCase),
            loadedSettings.TempMinAgeDays
        );

        var disabled = options.DisabledTargetKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        long totalFreed = 0;
        int totalRemoved = 0;
        int totalSkipped = 0;
        int adminSkippedStages = 0;
        bool hasErrors = false;
        bool isAdmin = IsAdministrator();

        // Stage 0: Restore Point creation
        if (!cancellationToken.IsCancellationRequested)
        {
            if (isAdmin)
            {
                logCallback(isEn
                    ? "[0/7] Creating Windows System Restore Point for safe rollback..."
                    : "[0/7] Создание системной точки восстановления Windows для безопасного отката...");
                var (created, reason) = await CreateRestorePointDetailedAsync("CoreSpectre Before Deep Cleanup", cancellationToken);
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
                    ? "[0/7] System Restore Point: skipped (administrator privileges required)."
                    : "[0/7] Точка восстановления: пропущено (для автоматического создания точки перед очисткой нужны права администратора).");
            }
        }

        // Stage 1: Temp Folders
        bool cleanUserTemp = !adminStagesOnly && options.CleanTemp && !disabled.Contains("user_temp");
        bool cleanWinTemp = options.CleanTemp && !disabled.Contains("win_temp");
        if (!cancellationToken.IsCancellationRequested && (cleanUserTemp || cleanWinTemp))
        {
            if (!adminStagesOnly)
            {
                logCallback(isEn
                    ? "[1/7] Cleaning temporary folders (%TEMP% and Windows Temp)..."
                    : "[1/7] Очистка временных папок (%TEMP% и Windows Temp)...");
            }
            else if (cleanWinTemp)
            {
                logCallback(isEn
                    ? "[1/7] Cleaning protected Windows Temp folder..."
                    : "[1/7] Очистка системной папки Windows Temp...");
            }

            if (cleanUserTemp)
            {
                var r1 = CleanDirectoryContents(Path.GetTempPath(), cancellationToken, options.TempMinAgeDays);
                totalFreed += r1.BytesFreed;
                totalRemoved += r1.FilesRemoved;
                totalSkipped += r1.FilesSkipped;
            }

            if (cleanWinTemp && !cancellationToken.IsCancellationRequested)
            {
                string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                if (isAdmin)
                {
                    var r2 = CleanDirectoryContents(winTemp, cancellationToken, options.TempMinAgeDays);
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
        else if (!adminStagesOnly && !cancellationToken.IsCancellationRequested)
        {
            logCallback(isEn
                ? "[1/7] Temporary folders: Skipped by user configuration."
                : "[1/7] Временные папки: Пропущено по настройке пользователя.");
        }

        // Stage 2: Windows Update & Delivery Optimization
        bool cleanSwDist = options.CleanSoftwareDistribution && !disabled.Contains("sw_dist");
        bool cleanDelivOpt = options.CleanSoftwareDistribution && !disabled.Contains("deliv_opt");
        if (!cancellationToken.IsCancellationRequested && (cleanSwDist || cleanDelivOpt))
        {
            string swDist = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
            string delivOpt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "DeliveryOptimization");

            if (isAdmin)
            {
                logCallback(isEn
                    ? "[2/7] Cleaning Windows Update and Delivery Optimization caches..."
                    : "[2/7] Очистка кэша обновлений Windows и Delivery Optimization...");

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

                if (cleanSwDist && !cancellationToken.IsCancellationRequested)
                {
                    var r3 = CleanDirectoryContents(swDist, cancellationToken);
                    totalFreed += r3.BytesFreed;
                    totalRemoved += r3.FilesRemoved;
                    totalSkipped += r3.FilesSkipped;
                }
                if (cleanDelivOpt && !cancellationToken.IsCancellationRequested)
                {
                    var r4 = CleanDirectoryContents(delivOpt, cancellationToken);
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
        else if (!adminStagesOnly && !cancellationToken.IsCancellationRequested)
        {
            logCallback(isEn
                ? "[2/7] Windows Update cache: Skipped by user configuration."
                : "[2/7] Кэш обновлений Windows: Пропущено по настройке пользователя.");
        }

        // Stage 3: Error Reports and Diagnostic Logs (excluding active CBS.log)
        bool cleanCrashDumps = !adminStagesOnly && options.CleanLogsAndReports && !disabled.Contains("crash_dumps");
        bool cleanWer = options.CleanLogsAndReports && !disabled.Contains("wer");
        bool cleanCbs = options.CleanLogsAndReports && !disabled.Contains("cbs_logs");
        if (!cancellationToken.IsCancellationRequested && (cleanCrashDumps || cleanWer || cleanCbs))
        {
            logCallback(isEn
                ? "[3/7] Clearing error reports and system diagnostic logs..."
                : "[3/7] Очистка отчетов об ошибках и логов...");

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            if (cleanCrashDumps)
            {
                var r5 = CleanDirectoryContents(Path.Combine(localAppData, "CrashDumps"), cancellationToken);
                totalFreed += r5.BytesFreed;
                totalRemoved += r5.FilesRemoved;
                totalSkipped += r5.FilesSkipped;
            }

            if (cleanWer && !cancellationToken.IsCancellationRequested)
            {
                if (!adminStagesOnly)
                {
                    var r6 = CleanDirectoryContents(Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportArchive"), cancellationToken);
                    var r7 = CleanDirectoryContents(Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportQueue"), cancellationToken);
                    totalFreed += r6.BytesFreed + r7.BytesFreed;
                    totalRemoved += r6.FilesRemoved + r7.FilesRemoved;
                    totalSkipped += r6.FilesSkipped + r7.FilesSkipped;
                }

                if (isAdmin)
                {
                    var r8 = CleanDirectoryContents(Path.Combine(progData, "Microsoft", "Windows", "WER", "ReportArchive"), cancellationToken);
                    var r9 = CleanDirectoryContents(Path.Combine(progData, "Microsoft", "Windows", "WER", "ReportQueue"), cancellationToken);
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

            if (cleanCbs && !cancellationToken.IsCancellationRequested)
            {
                string cbsLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS");
                if (isAdmin)
                {
                    var r10 = CleanDirectoryContents(cbsLogs, cancellationToken);
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
        else if (!adminStagesOnly && !cancellationToken.IsCancellationRequested)
        {
            logCallback(isEn
                ? "[3/7] Error reports and CBS logs: Skipped by user configuration."
                : "[3/7] Отчеты об ошибках и логи CBS: Пропущено по настройке пользователя.");
        }

        // Stage 4: GPU Shader & Browser HTTP Caches
        if (!adminStagesOnly && !cancellationToken.IsCancellationRequested)
        {
            bool cleanGpu = options.CleanGpuShaderCache && !disabled.Contains("gpu_shader");
            bool cleanBrowser = options.CleanBrowserCache && !disabled.Contains("browser_cache");
            if (cleanGpu || cleanBrowser)
            {
                logCallback(isEn
                    ? "[4/7] Cleaning GPU shader and browser caches..."
                    : "[4/7] Очистка кэша шейдеров GPU и браузеров...");

                if (cleanGpu)
                {
                    foreach (var dir in GetGpuShaderCacheDirectories())
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        var r = CleanDirectoryContents(dir, cancellationToken);
                        totalFreed += r.BytesFreed;
                        totalRemoved += r.FilesRemoved;
                        totalSkipped += r.FilesSkipped;
                    }
                }

                if (cleanBrowser)
                {
                    foreach (var dir in GetBrowserCacheDirectories())
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        var r = CleanDirectoryContents(dir, cancellationToken);
                        totalFreed += r.BytesFreed;
                        totalRemoved += r.FilesRemoved;
                        totalSkipped += r.FilesSkipped;
                    }
                }
            }
            else
            {
                logCallback(isEn
                    ? "[4/7] GPU & browser caches: Skipped by user configuration."
                    : "[4/7] Кэш GPU и браузеров: Пропущено по настройке пользователя.");
            }
        }

        // Stage 5: DNS Cache Flush
        if (!adminStagesOnly && !cancellationToken.IsCancellationRequested)
        {
            if (options.CleanDnsCache && !disabled.Contains("dns_cache"))
            {
                logCallback(isEn
                    ? "[5/7] Flushing network DNS resolver cache..."
                    : "[5/7] Сброс сетевого DNS-кэша...");

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
                    if (p != null) await p.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
            }
            else
            {
                logCallback(isEn
                    ? "[5/7] DNS resolver cache: Skipped by user configuration."
                    : "[5/7] Сетевой DNS-кэш: Пропущено по настройке пользователя.");
            }
        }

        // Stage 6: Empty Recycle Bin via native Shell32 API
        if (!adminStagesOnly && !cancellationToken.IsCancellationRequested)
        {
            if (options.CleanRecycleBin && !disabled.Contains("recycle_bin"))
            {
                logCallback(isEn
                    ? "[6/7] Emptying Windows Recycle Bin across all drives..."
                    : "[6/7] Очистка Корзины на всех накопителях...");

                var (binBytesBefore, binCountBefore) = GetRecycleBinSize();
                try
                {
                    uint hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                    if (hr != 0 && hr != 0x8000FFFFu)
                    {
                        throw new COMException($"SHEmptyRecycleBin returned 0x{hr:X8}", unchecked((int)hr));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"SHEmptyRecycleBin fallback triggered: {ex.Message}");
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
                        if (p != null) await p.WaitForExitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception psEx) { Logger.Debug("StorageService.cs: " + psEx.Message); }
                }
                var (binBytesAfter, binCountAfter) = GetRecycleBinSize();
                totalFreed += Math.Max(0, binBytesBefore - binBytesAfter);
                totalRemoved += Math.Max(0, binCountBefore - binCountAfter);
            }
            else
            {
                logCallback(isEn
                    ? "[6/7] Recycle Bin: Skipped by user configuration (default: off)."
                    : "[6/7] Корзина: Пропущено по настройке пользователя (по умолчанию выкл).");
            }
        }

        // Stage 7: Windows Component Store Cleanup (DISM)
        if (!cancellationToken.IsCancellationRequested && options.CleanDismComponentCleanup && !disabled.Contains("dism_cleanup"))
        {
            bool useResetBase = options.CleanDismResetBase && !disabled.Contains("dism_resetbase");
            string dismArgs = useResetBase
                ? "/Online /Cleanup-Image /StartComponentCleanup /ResetBase"
                : "/Online /Cleanup-Image /StartComponentCleanup";

            logCallback(isEn
                ? $"[7/7] Cleaning Windows Component Store (DISM{(useResetBase ? " with /ResetBase" : "")})..."
                : $"[7/7] Очистка хранилища компонентов Windows (DISM{(useResetBase ? " с /ResetBase" : "")})...");

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
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                        try
                        {
                            await p.WaitForExitAsync(linkedCts.Token);
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
                            try { p.Kill(); } catch (Exception ex) { Logger.Debug("StorageService.cs: " + ex.Message); }
                            if (cancellationToken.IsCancellationRequested)
                            {
                                logCallback(isEn ? "[DISM] Cancelled by user." : "[DISM] Отменено пользователем.");
                            }
                            else
                            {
                                hasErrors = true;
                                logCallback(isEn
                                    ? "[DISM] Operation timed out (exceeded 180s)."
                                    : "[DISM] Операция прервана по тайм-ауту (более 180 сек).");
                            }
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
        else if (!adminStagesOnly && !cancellationToken.IsCancellationRequested)
        {
            logCallback(isEn
                ? "[7/7] DISM Component Cleanup: Skipped by user configuration."
                : "[7/7] Очистка компонентов DISM: Пропущено по настройке пользователя.");
        }

        string summaryMessage;
        if (cancellationToken.IsCancellationRequested)
        {
            summaryMessage = isEn
                ? $"Cleanup cancelled. Freed before stop: {FormatBytes(totalFreed)}, removed {totalRemoved} files."
                : $"Очистка прервана пользователем. Успело освободиться: {FormatBytes(totalFreed)}, удалено файлов: {totalRemoved}.";
        }
        else if (adminSkippedStages > 0 || totalSkipped > 0 || hasErrors)
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

    public static async Task<ElevatedWorkerRunResult> ExecuteViaElevatedWorkerAsync(
        ElevatedCleanupJob job,
        Action<string> logCallback,
        CancellationToken cancellationToken = default)
    {
        string pipeName = $"CoreSpectre_Elev_{Guid.NewGuid():N}";
        bool isEn = job.IsEnglish;

        try
        {
            using var pipeServer = CreateSecurePipeServer(pipeName);

            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                exePath = Path.Combine(AppContext.BaseDirectory, "CoreSpectre.exe");
            }

            var psi = new ProcessStartInfo(exePath)
            {
                Arguments = $"--elevated-worker {pipeName}",
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                string msg = isEn ? "Could not start elevated worker process." : "Не удалось запустить фоновый процесс с правами администратора.";
                return new ElevatedWorkerRunResult(false, null, msg);
            }

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(25));
            Task exitMonitor = proc.WaitForExitAsync(connectCts.Token);
            var connectTask = pipeServer.WaitForConnectionAsync(connectCts.Token);
            await Task.WhenAny(connectTask, exitMonitor);

            if (!pipeServer.IsConnected)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch (Exception ex) { Logger.Debug($"Worker kill failed: {ex.Message}"); }
                string errMsg = cancellationToken.IsCancellationRequested
                    ? (isEn ? "Elevated operation cancelled." : "Операция с правами администратора отменена.")
                    : (isEn ? "Elevated worker closed before connecting to secure channel." : "Повышенный процесс завершился до подключения к каналу.");
                return new ElevatedWorkerRunResult(false, null, errMsg);
            }

            using (var writer = new StreamWriter(pipeServer, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(job));
            }

            using var execCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            execCts.CancelAfter(TimeSpan.FromMinutes(4));
            using var reader = new StreamReader(pipeServer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            CleanResult? adminResult = null;
            try
            {
                while (await reader.ReadLineAsync(execCts.Token) is { } frame)
                {
                    if (frame.StartsWith("LOG:", StringComparison.Ordinal))
                    {
                        string logLine = Encoding.UTF8.GetString(Convert.FromBase64String(frame[4..]));
                        logCallback(logLine);
                    }
                    else if (frame.StartsWith("RES:", StringComparison.Ordinal))
                    {
                        string resJson = Encoding.UTF8.GetString(Convert.FromBase64String(frame[4..]));
                        adminResult = JsonSerializer.Deserialize<CleanResult>(resJson);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch (Exception ex) { Logger.Debug($"Worker kill on cancel failed: {ex.Message}"); }
                string cancelMsg = cancellationToken.IsCancellationRequested
                    ? (isEn ? "Elevated operation cancelled by user." : "Операция с правами администратора отменена пользователем.")
                    : (isEn ? "Elevated worker timed out." : "Превышено время ожидания операции администратора.");
                return new ElevatedWorkerRunResult(false, null, cancelMsg);
            }
            catch (IOException) when (adminResult != null)
            {
                // Worker exited cleanly after sending RES frame
            }

            try
            {
                await proc.WaitForExitAsync(execCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch (Exception ex) { Logger.Debug($"Worker kill on exit wait failed: {ex.Message}"); }
            }

            if (adminResult == null && proc.ExitCode != 0)
            {
                string errMsg = isEn
                    ? $"Elevated worker exited with code {proc.ExitCode}."
                    : $"Операция с правами администратора завершилась с кодом {proc.ExitCode}.";
                return new ElevatedWorkerRunResult(false, null, errMsg);
            }

            return new ElevatedWorkerRunResult(false, adminResult, null);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new ElevatedWorkerRunResult(true, null, isEn ? "Administrator elevation cancelled." : "Предоставление прав администратора отменено.");
        }
        catch (Exception ex)
        {
            Logger.Error("Elevated worker failed", ex);
            return new ElevatedWorkerRunResult(false, null, ex.Message);
        }
    }

    public static async Task<int> RunElevatedWorkerAsync(string pipeName)
    {
        if (!IsValidPipeName(pipeName)) return 2;

        using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await pipeClient.ConnectAsync(connectCts.Token);

        using var reader = new StreamReader(pipeClient, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(pipeClient, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        string? jobJson = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(jobJson)) return 3;

        var job = JsonSerializer.Deserialize<ElevatedCleanupJob>(jobJson);
        if (job == null) return 4;

        var pipeLock = new object();
        void SendFrame(string prefix, string payload)
        {
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
            lock (pipeLock)
            {
                try
                {
                    writer.WriteLine($"{prefix}:{encoded}");
                }
                catch { }
            }
        }

        CleanResult result;
        string action = (job.Action ?? "cleanup").Trim().ToLowerInvariant();
        if (action == "standby")
        {
            var (ok, freed, msg) = MemoryService.PurgeStandbyList(job.IsEnglish);
            SendFrame("LOG", msg);
            result = new CleanResult(freed, 0, 0, 0, HasErrors: !ok, SummaryMessage: msg);
        }
        else if (action is "compact_on" or "compact_off")
        {
            bool enable = action == "compact_on";
            var (ok, msg) = await SetCompactOsAsync(enable, job.IsEnglish);
            SendFrame("LOG", $"[CompactOS] {msg}");
            result = new CleanResult(0, 0, 0, 0, HasErrors: !ok, SummaryMessage: msg);
        }
        else
        {
            var opts = new StorageCleanupOptions(
                job.CleanTemp,
                job.CleanSoftwareDistribution,
                job.CleanLogsAndReports,
                CleanDnsCache: false,
                CleanRecycleBin: false,
                CleanGpuShaderCache: false,
                CleanBrowserCache: false,
                job.CleanDismComponentCleanup,
                job.CleanDismResetBase,
                new HashSet<string>(job.DisabledTargetKeys ?? new List<string>(), StringComparer.OrdinalIgnoreCase),
                TempMinAgeDays: job.TempMinAgeDays
            );

            result = await ExecuteDeepCleanupAsync(job.IsEnglish, line => SendFrame("LOG", line), opts, adminStagesOnly: true);
        }

        string resJson = JsonSerializer.Serialize(result);
        SendFrame("RES", resJson);
        try { await writer.FlushAsync(); pipeClient.WaitForPipeDrain(); } catch { }
        return 0;
    }

    public static async Task<(bool Created, string? Reason)> CreateRestorePointDetailedAsync(string description, CancellationToken cancellationToken = default)
    {
        if (!IsAdministrator()) return (false, "Admin required");

        try
        {
            string safeDesc = new string((description ?? "CoreSpectre Restore Point")
                .Where(c => !char.IsControl(c))
                .ToArray())
                .Replace("'", "''");
            if (string.IsNullOrWhiteSpace(safeDesc)) safeDesc = "CoreSpectre Before Deep Cleanup";

            string psScript = $"Checkpoint-Computer -Description '{safeDesc}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop";
            string encodedCmd = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));

            string psPath = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            var psi = new ProcessStartInfo(psPath, $"-NoProfile -NonInteractive -EncodedCommand {encodedCmd}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                string err = await p.StandardError.ReadToEndAsync(linked.Token);
                await p.WaitForExitAsync(linked.Token);
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

    public static List<LargeDirectory> ScanLargeDirectories(string? driveLetter = null, int maxResults = 8)
    {
        var results = new List<LargeDirectory>();
        string drive = ValidateDriveLetter(driveLetter);
        string root = $"{drive}:\\";

        try
        {
            var rootDir = new DirectoryInfo(root);
            foreach (var dir in rootDir.EnumerateDirectories())
            {
                if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((dir.Attributes & FileAttributes.System) != 0 && dir.Name.StartsWith("$", StringComparison.Ordinal))
                    continue;

                try
                {
                    long size = GetTopLevelSize(dir);
                    if (size > 50L * 1024 * 1024)
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

    private static long GetTopLevelSize(DirectoryInfo dir, int maxFiles = 200_000)
    {
        long total = 0;
        int inspected = 0;

        try
        {
            var enumOpts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var file in dir.EnumerateFiles("*", enumOpts))
            {
                try
                {
                    total += file.Length;
                    inspected++;
                    if (inspected >= maxFiles) break;
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

    public static List<LargeDirectory> ScanLargestFiles(string? driveLetter = null, long minBytes = 100L * 1024 * 1024, int maxResults = 15)
    {
        var list = new List<LargeDirectory>();
        string drive = ValidateDriveLetter(driveLetter);
        string root = $"{drive}:\\";

        try
        {
            var enumOpts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", enumOpts))
            {
                try
                {
                    if (f.Length >= minBytes)
                    {
                        list.Add(new LargeDirectory(f.FullName, f.Length, FormatBytes(f.Length)));
                    }
                }
                catch { }
            }
        }
        catch { }

        return list.OrderByDescending(x => x.SizeBytes).Take(maxResults).ToList();
    }

    public static List<LargeDirectory> FindBackupAndArchiveAudits()
    {
        var audits = new List<LargeDirectory>();
        var backupDirs = new[]
        {
            @"D:\.minecraft\backup",
            @"D:\.minecraft\saves_backups",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "backup"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves_backups")
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in backupDirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var di = new DirectoryInfo(dir);
                string resolved = (di.Attributes & FileAttributes.ReparsePoint) != 0
                    ? di.ResolveLinkTarget(true)?.FullName ?? dir
                    : dir;
                if (!seen.Add(resolved)) continue;
                long sz = GetTopLevelSize(new DirectoryInfo(resolved));
                if (sz > 10L * 1024 * 1024)
                    audits.Add(new LargeDirectory(resolved, sz, FormatBytes(sz), IsBackupNote: true));
            }
            catch { }
        }

        foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed))
        {
            try
            {
                foreach (var zip in d.RootDirectory.EnumerateFiles("*.zip"))
                {
                    if (zip.Length >= 50L * 1024 * 1024)
                    {
                        audits.Add(new LargeDirectory(zip.FullName, zip.Length, FormatBytes(zip.Length), IsBackupNote: true));
                    }
                }
            }
            catch { }
        }

        return audits.OrderByDescending(x => x.SizeBytes).ToList();
    }

    public static List<TargetFilePreview> PreviewTargetFiles(string key, int maxItems = 25)
    {
        var dirs = new List<string>();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        switch (key)
        {
            case "user_temp": dirs.Add(Path.GetTempPath()); break;
            case "win_temp": dirs.Add(Path.Combine(winDir, "Temp")); break;
            case "sw_dist": dirs.Add(Path.Combine(winDir, "SoftwareDistribution", "Download")); break;
            case "deliv_opt": dirs.Add(Path.Combine(winDir, "SoftwareDistribution", "DeliveryOptimization")); break;
            case "crash_dumps": dirs.Add(Path.Combine(localAppData, "CrashDumps")); break;
            case "wer": dirs.Add(Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportArchive")); break;
            case "cbs_logs": dirs.Add(Path.Combine(winDir, "Logs", "CBS")); break;
            case "gpu_shader": dirs.AddRange(GetGpuShaderCacheDirectories()); break;
            case "browser_cache": dirs.AddRange(GetBrowserCacheDirectories()); break;
        }

        var files = new List<FileInfo>();
        var enumOpts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var d in dirs)
        {
            if (!Directory.Exists(d)) continue;
            try
            {
                foreach (var f in new DirectoryInfo(d).EnumerateFiles("*", enumOpts))
                {
                    if (!IsProtectedSelfExtractionPath(f.FullName))
                    {
                        files.Add(f);
                    }
                }
            }
            catch { }
        }

        return files
            .OrderByDescending(f => f.Length)
            .Take(maxItems)
            .Select(f => new TargetFilePreview(
                f.Name,
                f.FullName,
                FormatBytes(f.Length),
                f.LastWriteTime.ToString("dd.MM.yyyy HH:mm")))
            .ToList();
    }

    public static async Task<bool> IsCompactOsEnabledAsync()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding oemEnc = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

            string compactExe = Path.Combine(Environment.SystemDirectory, "compact.exe");
            var psi = new ProcessStartInfo(compactExe, "/CompactOS:query")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                StandardOutputEncoding = oemEnc,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            string output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();

            if (output.Contains("not in the Compact", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("не находится в сжатом", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("не сжатом", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return p.ExitCode == 0 ||
                   output.Contains("in the Compact state", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("находится в сжатом состоянии", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.Debug($"IsCompactOsEnabledAsync failed: {ex.Message}");
            return false;
        }
    }

    public static async Task<(bool Success, string Message)> SetCompactOsAsync(bool enable, bool isEn = false)
    {
        if (!IsAdministrator())
        {
            return (false, isEn ? "Administrator privileges required for CompactOS." : "Для настройки CompactOS требуются права администратора.");
        }

        Process? proc = null;
        try
        {
            string compactExe = Path.Combine(Environment.SystemDirectory, "compact.exe");
            string arg = enable ? "/CompactOS:always" : "/CompactOS:never";
            var psi = new ProcessStartInfo(compactExe, arg)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            proc = Process.Start(psi);
            if (proc == null) return (false, "Failed to start compact.exe");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await proc.WaitForExitAsync(cts.Token);
            if (proc.ExitCode == 0)
            {
                return (true, enable
                    ? (isEn ? "CompactOS compression enabled (system files compressed)." : "CompactOS включён (системные файлы сжаты для экономии места).")
                    : (isEn ? "CompactOS compression disabled." : "CompactOS отключён."));
            }
            return (false, $"compact.exe ExitCode {proc.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            try { proc?.Kill(entireProcessTree: true); } catch (Exception ex) { Logger.Debug($"compact.exe kill failed: {ex.Message}"); }
            return (false, isEn ? "CompactOS operation timed out (exceeded 3 minutes)." : "Операция CompactOS превысила лимит ожидания (3 минуты).");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            proc?.Dispose();
        }
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

        return unitIndex == 0
            ? $"{size.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} {units[unitIndex]}"
            : $"{size.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }
}
