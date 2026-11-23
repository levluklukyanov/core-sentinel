using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CoreSentinel.Services;

public record TelemetrySnapshot(
    double CpuLoadPercent,
    double RamUsedPercent,
    long RamUsedBytes,
    long RamTotalBytes,
    string SystemDriveLetter,
    double DiskCFreeGb,
    double DiskCTotalGb,
    string DataDriveLetter,
    double DiskDFreeGb,
    double DiskDTotalGb,
    bool HasSecondaryDataDrive,
    bool IsGameRunning,
    string ActiveGameName
);

public static class HardwareTelemetry
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;

        public ulong ToULong() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    private static ulong _prevIdleTime;
    private static ulong _prevKernelTime;
    private static ulong _prevUserTime;
    private static bool _initialized = false;
    private static double _lastCpuUsage = 0.0;
    private static readonly object _syncLock = new();
    private static string? _cachedCpuName = null;

    private static readonly string[] KnownGames = new[]
    {
        "javaw", "java", "minecraft", "genshinimpact", "terraria", 
        "brawlhalla", "robloxplayerbeta", "cs2", "dota2", "tlauncher"
    };

    public static string GetProcessorName()
    {
        if (_cachedCpuName != null)
            return _cachedCpuName;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string? name = key?.GetValue("ProcessorNameString") as string;
            if (!string.IsNullOrWhiteSpace(name))
            {
                _cachedCpuName = string.Join(" ", name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                return _cachedCpuName;
            }
        }
        catch { }

        string? envCpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        _cachedCpuName = !string.IsNullOrWhiteSpace(envCpu) ? envCpu : "ЦП системы";
        return _cachedCpuName;
    }

    public static string GetSystemDriveLetter()
    {
        try
        {
            string? root = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrEmpty(root))
            {
                return root.TrimEnd('\\', ':');
            }
        }
        catch { }
        return "C";
    }

    public static string GetSecondaryDataDriveLetter()
    {
        try
        {
            string sys = GetSystemDriveLetter();
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed && !d.Name.StartsWith(sys, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (drives.Count > 0)
            {
                return drives[0].Name.TrimEnd('\\', ':');
            }
        }
        catch { }
        return "D";
    }

    public static bool HasSecondaryDrive()
    {
        try
        {
            string sys = GetSystemDriveLetter();
            return DriveInfo.GetDrives().Any(d => d.IsReady && d.DriveType == DriveType.Fixed && !d.Name.StartsWith(sys, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public static TelemetrySnapshot Sample()
    {
        double cpu = GetCpuUsage();
        var mem = MemoryService.GetCurrentStatus();

        string sysLetter = GetSystemDriveLetter();
        string dataLetter = GetSecondaryDataDriveLetter();
        bool hasSecondary = HasSecondaryDrive();

        var diskC = StorageService.GetDriveInfo(sysLetter);
        var diskD = hasSecondary ? StorageService.GetDriveInfo(dataLetter) : null;

        var (isGame, gameName) = CheckActiveGame();

        double diskCFree = diskC != null ? (double)diskC.FreeBytes / (1024 * 1024 * 1024) : 0.0;
        double diskCTotal = diskC != null ? (double)diskC.TotalBytes / (1024 * 1024 * 1024) : 0.0;
        double diskDFree = diskD != null ? (double)diskD.FreeBytes / (1024 * 1024 * 1024) : 0.0;
        double diskDTotal = diskD != null ? (double)diskD.TotalBytes / (1024 * 1024 * 1024) : 0.0;

        return new TelemetrySnapshot(
            Math.Round(cpu, 1),
            mem.UsedPercent,
            mem.UsedPhysicalBytes,
            mem.TotalPhysicalBytes,
            sysLetter,
            Math.Round(diskCFree, 2),
            Math.Round(diskCTotal, 2),
            dataLetter,
            Math.Round(diskDFree, 2),
            Math.Round(diskDTotal, 2),
            hasSecondary,
            isGame,
            gameName
        );
    }

    private static double GetCpuUsage()
    {
        lock (_syncLock)
        {
            if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
                return _lastCpuUsage;

            ulong idle = idleTime.ToULong();
            ulong kernel = kernelTime.ToULong();
            ulong user = userTime.ToULong();

            if (!_initialized)
            {
                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;
                _initialized = true;
                return 0.0;
            }

            ulong usrDiff = user - _prevUserTime;
            ulong kerDiff = kernel - _prevKernelTime;
            ulong idlDiff = idle - _prevIdleTime;

            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;

            ulong totalSys = usrDiff + kerDiff;
            if (totalSys == 0) return _lastCpuUsage;

            // In Windows NT kernel time includes idle time
            ulong nonIdle = totalSys > idlDiff ? totalSys - idlDiff : 0;
            double cpu = (double)nonIdle * 100.0 / totalSys;

            _lastCpuUsage = Math.Clamp(cpu, 0.0, 100.0);
            return _lastCpuUsage;
        }
    }

    public static bool IsRussianLanguage { get; set; }

    private static (bool IsRunning, string GameName) CheckActiveGame()
    {
        try
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    string name = proc.ProcessName;
                    string title = "";
                    try { title = proc.MainWindowTitle; } catch { }

                    var (isGame, displayName) = GameSentinelService.ResolveGameIdentity(name, title, IsRussianLanguage);
                    if (isGame)
                    {
                        return (true, displayName);
                    }
                }
                catch
                {
                    // Access denied
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // Process scan failure
        }

        return (false, string.Empty);
    }
}
