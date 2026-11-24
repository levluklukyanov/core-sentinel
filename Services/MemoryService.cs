using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CoreSentinel.Services;

public record MemoryStatus(
    long TotalPhysicalBytes,
    long AvailablePhysicalBytes,
    long UsedPhysicalBytes,
    double UsedPercent
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

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

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

        // Honest unavailable status: do not report fake 8 GB / 50%
        return new MemoryStatus(0L, 0L, 0L, 0.0);
    }

    public static PurgeResult PurgeMemory()
    {
        var before = GetCurrentStatus();
        int optimizedCount = 0;

        var processes = Process.GetProcesses();
        int currentPid = Environment.ProcessId;

        foreach (var proc in processes)
        {
            if (proc.Id <= 4 || proc.Id == currentPid)
                continue;

            try
            {
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

        // Force GC for our own app
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

    public static int OptimizeGamePriorities(string[]? customGameNames = null)
    {
        var gameList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "javaw", "java", "minecraft", "genshinimpact", "terraria", 
            "brawlhalla", "robloxplayerbeta", "cs2", "dota2"
        };

        if (customGameNames != null)
        {
            foreach (var name in customGameNames)
                gameList.Add(name.Replace(".exe", "", StringComparison.OrdinalIgnoreCase));
        }

        int boostedCount = 0;
        var processes = Process.GetProcesses();

        foreach (var proc in processes)
        {
            try
            {
                if (gameList.Contains(proc.ProcessName))
                {
                    if (proc.PriorityClass != ProcessPriorityClass.High)
                    {
                        proc.PriorityClass = ProcessPriorityClass.High;
                        boostedCount++;
                    }
                }
            }
            catch
            {
                // Ignore system or protected processes
            }
            finally
            {
                proc.Dispose();
            }
        }

        return boostedCount;
    }
}
