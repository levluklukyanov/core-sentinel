using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CoreSpectre.Services;

public record MemoryStatus(
    long TotalPhysicalBytes,
    long AvailablePhysicalBytes,
    long UsedPhysicalBytes,
    double UsedPercent
);

public record ExtendedMemoryInfo(
    long CommitTotalBytes,
    long CommitLimitBytes,
    long SystemCacheBytes,
    long KernelPagedBytes,
    long KernelNonpagedBytes
);

public record PurgeResult(
    long BytesFreed,
    int ProcessesOptimized,
    double UsedPercentBefore,
    double UsedPercentAfter
);

public static class MemoryService
{
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint PROCESS_SET_INFORMATION = 0x0200;

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int SystemMemoryListInformation = 80;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryPurgeLowPriorityStandbyList = 5;

    private const int ProcessPowerThrottling = 4;
    private const int ProcessMemoryPriority = 5;
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    private const uint MEMORY_PRIORITY_LOW = 2;
    private const uint MEMORY_PRIORITY_NORMAL = 5;

    private static readonly ConcurrentDictionary<int, ProcessPriorityClass> _throttledOriginals = new();

    private static readonly HashSet<string> ProtectedSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "services",
        "lsass", "winlogon", "fontdrvhost", "dwm", "explorer", "corespectre",
        "svchost", "sihost", "audiodg", "spoolsv", "msmpeng", "securityhealthservice",
        "securityhealthsystray", "nissrv", "searchindexer", "ctfmon", "taskhostw",
        "runtimebroker", "shellexperiencehost", "startmenuexperiencehost"
    };

    public static bool IsProtectedFromWorkingSetTrim(string? processName) =>
        !string.IsNullOrWhiteSpace(processName) &&
        ProtectedSystemProcesses.Contains(processName.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase));

    public static int ActiveThrottledProcessCount => _throttledOriginals.Count;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public static MEMORYSTATUSEX Create()
        {
            var result = new MEMORYSTATUSEX();
            result.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public UIntPtr CommitTotal;
        public UIntPtr CommitLimit;
        public UIntPtr CommitPeak;
        public UIntPtr PhysicalTotal;
        public UIntPtr PhysicalAvailable;
        public UIntPtr SystemCache;
        public UIntPtr KernelTotal;
        public UIntPtr KernelPaged;
        public UIntPtr KernelNonpaged;
        public UIntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_PRIORITY_INFORMATION
    {
        public uint MemoryPriority;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass, IntPtr processInformation, int processInformationSize);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    public static MemoryStatus GetCurrentStatus()
    {
        var memStatus = MEMORYSTATUSEX.Create();
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            long total = (long)memStatus.ullTotalPhys;
            long avail = (long)memStatus.ullAvailPhys;
            long used = total - avail;
            double percent = total > 0 ? (double)used / total * 100.0 : 0.0;
            return new MemoryStatus(total, avail, used, Math.Round(percent, 1));
        }

        return new MemoryStatus(0L, 0L, 0L, 0.0);
    }

    public static ExtendedMemoryInfo? GetPerformanceDetails()
    {
        try
        {
            uint cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>();
            if (GetPerformanceInfo(out var info, cb))
            {
                long pageSize = (long)info.PageSize;
                return new ExtendedMemoryInfo(
                    CommitTotalBytes: (long)info.CommitTotal * pageSize,
                    CommitLimitBytes: (long)info.CommitLimit * pageSize,
                    SystemCacheBytes: (long)info.SystemCache * pageSize,
                    KernelPagedBytes: (long)info.KernelPaged * pageSize,
                    KernelNonpagedBytes: (long)info.KernelNonpaged * pageSize
                );
            }
        }
        catch (Exception ex) { Logger.Debug($"GetPerformanceDetails failed: {ex.Message}"); }
        return null;
    }

    private static bool EnablePrivilege(string privilegeName)
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            if (!OpenProcessToken(current.Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
                return false;

            try
            {
                if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                    return false;

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED
                };

                return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) &&
                       Marshal.GetLastWin32Error() == 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch
        {
            return false;
        }
    }

    public static (bool Success, long BytesFreed, string Message) PurgeStandbyList(bool isEn = false, bool includeModified = false)
    {
        if (!StorageService.IsAdministrator())
        {
            return (false, 0, isEn ? "Administrator privileges required for Standby List purge." : "Для очистки Standby List требуются права администратора.");
        }

        var before = GetCurrentStatus();
        if (!EnablePrivilege("SeProfileSingleProcessPrivilege"))
        {
            return (false, 0, isEn ? "Could not enable SeProfileSingleProcessPrivilege." : "Не удалось активировать привилегию SeProfileSingleProcessPrivilege.");
        }

        int cmdLow = MemoryPurgeLowPriorityStandbyList;
        int cmdStandby = MemoryPurgeStandbyList;
        int st1 = NtSetSystemInformation(SystemMemoryListInformation, ref cmdLow, sizeof(int));
        int st2 = NtSetSystemInformation(SystemMemoryListInformation, ref cmdStandby, sizeof(int));

        if (includeModified)
        {
            int cmdMod = MemoryFlushModifiedList;
            NtSetSystemInformation(SystemMemoryListInformation, ref cmdMod, sizeof(int));
        }

        if (st2 != 0 && st1 != 0)
        {
            return (false, 0, isEn ? $"NtSetSystemInformation failed (NTSTATUS 0x{st2:X8})." : $"Ошибка сброса Standby List (NTSTATUS 0x{st2:X8}).");
        }

        var after = GetCurrentStatus();
        long freed = Math.Max(0, after.AvailablePhysicalBytes - before.AvailablePhysicalBytes);
        string msg = isEn
            ? $"Standby List purged (available RAM: {StorageService.FormatBytes(after.AvailablePhysicalBytes)})."
            : $"Standby List очищен (доступно ОЗУ: {StorageService.FormatBytes(after.AvailablePhysicalBytes)}).";
        return (true, freed, msg);
    }

    public static PurgeResult PurgeMemory(IReadOnlyCollection<string>? processWhitelist = null)
    {
        var before = GetCurrentStatus();
        int optimizedCount = 0;

        HashSet<string>? excluded = null;
        if (processWhitelist != null && processWhitelist.Count > 0)
        {
            excluded = new HashSet<string>(
                processWhitelist
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);
        }

        var processes = Process.GetProcesses();
        int currentPid = Environment.ProcessId;

        foreach (var proc in processes)
        {
            try
            {
                if (proc.Id <= 4 || proc.Id == currentPid)
                    continue;

                if (IsProtectedFromWorkingSetTrim(proc.ProcessName))
                    continue;

                if (excluded != null && excluded.Contains(proc.ProcessName))
                    continue;

                IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, proc.Id);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        if (EmptyWorkingSet(hProcess))
                        {
                            optimizedCount++;
                        }
                    }
                    finally
                    {
                        CloseHandle(hProcess);
                    }
                }
            }
            catch
            {
                // Access denied or process terminated, skip safely
            }
            finally
            {
                proc.Dispose();
            }
        }

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();

        var after = GetCurrentStatus();
        long freed = Math.Max(0, after.AvailablePhysicalBytes - before.AvailablePhysicalBytes);

        return new PurgeResult(
            freed,
            optimizedCount,
            before.UsedPercent,
            after.UsedPercent
        );
    }

    public static bool ShouldThrottlePriority(ProcessPriorityClass priority) =>
        priority is ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal or ProcessPriorityClass.High;

    public static int OptimizeBackgroundProcesses(IReadOnlyCollection<string>? processWhitelist = null)
    {
        string[] targetPatterns =
        [
            "onedrive", "dropbox", "googledrivesync", "searchindexer",
            "compattelrunner", "mscorsvw", "adobeupdateservice", "armsvc", "epicwebhelper"
        ];

        var excluded = new HashSet<string>(
            (processWhitelist ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        int optimized = 0;
        var procs = Process.GetProcesses();
        foreach (var p in procs)
        {
            try
            {
                if (p.Id <= 4) continue;
                string pName = p.ProcessName;
                if (excluded.Contains(pName)) continue;

                string lower = pName.ToLowerInvariant();
                if (!targetPatterns.Any(t => lower.Contains(t))) continue;

                var prev = p.PriorityClass;
                if (ShouldThrottlePriority(prev))
                {
                    _throttledOriginals.TryAdd(p.Id, prev);
                    p.PriorityClass = ProcessPriorityClass.BelowNormal;
                    ApplyEcoQosAndMemoryPriority(p.Id, enableEco: true);
                    optimized++;
                }
            }
            catch (Exception ex) { Logger.Debug($"Background CPU opt skipped: {ex.Message}"); }
            finally { p.Dispose(); }
        }
        return optimized;
    }

    public static int RestoreBackgroundProcesses()
    {
        int restored = 0;
        foreach (var kvp in _throttledOriginals.ToArray())
        {
            try
            {
                using var p = Process.GetProcessById(kvp.Key);
                p.PriorityClass = kvp.Value;
                ApplyEcoQosAndMemoryPriority(p.Id, enableEco: false);
                restored++;
            }
            catch (Exception ex) { Logger.Debug($"Restore background process skipped: {ex.Message}"); }
            finally
            {
                _throttledOriginals.TryRemove(kvp.Key, out _);
            }
        }
        return restored;
    }

    private static void ApplyEcoQosAndMemoryPriority(int pid, bool enableEco)
    {
        IntPtr hProc = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
        if (hProc == IntPtr.Zero) return;

        try
        {
            var throttle = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = enableEco ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0u
            };
            IntPtr pThrottle = Marshal.AllocHGlobal(Marshal.SizeOf(throttle));
            try
            {
                Marshal.StructureToPtr(throttle, pThrottle, false);
                SetProcessInformation(hProc, ProcessPowerThrottling, pThrottle, Marshal.SizeOf(throttle));
            }
            finally { Marshal.FreeHGlobal(pThrottle); }

            var memPrio = new MEMORY_PRIORITY_INFORMATION
            {
                MemoryPriority = enableEco ? MEMORY_PRIORITY_LOW : MEMORY_PRIORITY_NORMAL
            };
            IntPtr pMem = Marshal.AllocHGlobal(Marshal.SizeOf(memPrio));
            try
            {
                Marshal.StructureToPtr(memPrio, pMem, false);
                SetProcessInformation(hProc, ProcessMemoryPriority, pMem, Marshal.SizeOf(memPrio));
            }
            finally { Marshal.FreeHGlobal(pMem); }
        }
        catch (Exception ex)
        {
            Logger.Debug($"ApplyEcoQosAndMemoryPriority skipped for PID {pid}: {ex.Message}");
        }
        finally
        {
            CloseHandle(hProc);
        }
    }
}
