using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CoreSpectre.Services;

public record ProcessItemInfo
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string WindowTitle { get; init; } = "";
    public long WorkingSetBytes { get; init; }
    public string FormattedRam { get; init; } = "";
    public double CpuPercent { get; init; }
    public string FormattedCpu { get; init; } = "";
    public int ThreadCount { get; init; }
    public string Priority { get; init; } = "Обычный";
    public bool IsCritical { get; init; }
    public bool HasWindow { get; init; }
}

public enum ProcessSortMode
{
    Cpu,
    Ram
}

public static class ProcessTrackerService
{
    private static readonly Dictionary<int, (TimeSpan TotalTime, DateTime SampleTime)> _previousTimes = new();
    private static readonly object _lock = new();

    private static readonly HashSet<string> CriticalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "services", 
        "lsass", "winlogon", "fontdrvhost", "dwm", "explorer", "corespectre",
        "svchost", "sihost", "audiodg", "spoolsv"
    };

    public static List<ProcessItemInfo> SampleProcesses(ProcessSortMode sortMode, string filter = "")
    {
        var now = DateTime.UtcNow;
        var processes = Process.GetProcesses();
        int processorCount = Math.Max(1, Environment.ProcessorCount);
        var list = new List<ProcessItemInfo>(processes.Length);
        var activePids = new HashSet<int>(processes.Length);

        lock (_lock)
        {
            foreach (var proc in processes)
            {
                int pid = proc.Id;
                activePids.Add(pid);
                string name = proc.ProcessName;
                string windowTitle = "";
                long workingSet = 0;
                int threadCount = 1;
                string priority = "Обычный";
                bool isCritical = pid <= 4 || CriticalProcessNames.Contains(name);
                double cpuPercent = 0.0;

                try
                {
                    workingSet = proc.WorkingSet64;
                    threadCount = proc.Threads.Count;
                    windowTitle = proc.MainWindowTitle;
                    priority = MapPriority(proc.BasePriority);

                    var totalTime = proc.TotalProcessorTime;
                    if (_previousTimes.TryGetValue(pid, out var prev))
                    {
                        double timeDiffMs = (now - prev.SampleTime).TotalMilliseconds;
                        if (timeDiffMs > 100)
                        {
                            double cpuDiffMs = (totalTime - prev.TotalTime).TotalMilliseconds;
                            cpuPercent = Math.Clamp((cpuDiffMs / (timeDiffMs * processorCount)) * 100.0, 0.0, 100.0);
                        }
                    }
                    _previousTimes[pid] = (totalTime, now);
                }
                catch
                {
                    // Access denied on protected system/kernel processes
                }

                // Filtering by search term
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    bool matchName = name.Contains(filter, StringComparison.OrdinalIgnoreCase);
                    bool matchPid = pid.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);
                    bool matchTitle = !string.IsNullOrEmpty(windowTitle) && windowTitle.Contains(filter, StringComparison.OrdinalIgnoreCase);
                    if (!matchName && !matchPid && !matchTitle)
                        continue;
                }

                list.Add(new ProcessItemInfo
                {
                    Pid = pid,
                    Name = name,
                    DisplayName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.exe",
                    WindowTitle = string.IsNullOrWhiteSpace(windowTitle) ? "-" : windowTitle,
                    WorkingSetBytes = workingSet,
                    FormattedRam = StorageService.FormatBytes(workingSet),
                    CpuPercent = Math.Round(cpuPercent, 1),
                    FormattedCpu = $"{cpuPercent:0.0}%",
                    ThreadCount = threadCount,
                    Priority = priority,
                    IsCritical = isCritical,
                    HasWindow = !string.IsNullOrWhiteSpace(windowTitle)
                });
            }

            // Clean up dead PIDs
            var deadPids = _previousTimes.Keys.Where(k => !activePids.Contains(k)).ToList();
            foreach (var d in deadPids)
            {
                _previousTimes.Remove(d);
            }
        }

        // Sorting
        if (sortMode == ProcessSortMode.Cpu)
        {
            list.Sort((a, b) =>
            {
                int c = b.CpuPercent.CompareTo(a.CpuPercent);
                return c != 0 ? c : b.WorkingSetBytes.CompareTo(a.WorkingSetBytes);
            });
        }
        else
        {
            list.Sort((a, b) =>
            {
                int c = b.WorkingSetBytes.CompareTo(a.WorkingSetBytes);
                return c != 0 ? c : b.CpuPercent.CompareTo(a.CpuPercent);
            });
        }

        return list;
    }

    public static (bool Success, string Message) TerminateProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (proc.Id <= 4 || CriticalProcessNames.Contains(proc.ProcessName))
            {
                return (false, $"Процесс '{proc.ProcessName}' (PID {pid}) защищен системой. Завершение заблокировано.");
            }

            string name = proc.ProcessName;
            proc.Kill(true);
            return (true, $"Процесс '{name}' (PID {pid}) успешно завершен.");
        }
        catch (ArgumentException)
        {
            return (false, $"Процесс с PID {pid} не найден или уже завершен.");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка завершения процесса: {ex.Message}");
        }
    }

    public static (bool Success, string Message) PurgeProcessMemory(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            long before = proc.WorkingSet64;
            
            const uint PROCESS_QUERY_INFORMATION = 0x0400;
            const uint PROCESS_SET_QUOTA = 0x0100;
            
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    bool ok = EmptyWorkingSet(hProcess);
                    if (ok)
                    {
                        proc.Refresh();
                        long after = proc.WorkingSet64;
                        long freed = Math.Max(0, before - after);
                        return (true, $"ОЗУ процесса '{proc.ProcessName}' очищено. Высвобождено {StorageService.FormatBytes(freed)}.");
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            return (false, "Не удалось выполнить сброс рабочего набора процесса.");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка оптимизации: {ex.Message}");
        }
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static string MapPriority(int basePriority) => basePriority switch
    {
        >= 13 => "Высокий",
        >= 8 => "Обычный",
        >= 4 => "Низкий",
        _ => "Фоновый"
    };
}
