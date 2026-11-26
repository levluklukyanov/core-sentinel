using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace CoreSpectre.Services;

public record ActiveGameInfo(
    int Pid,
    string DisplayName,
    string ProcessName,
    double CpuPercent,
    string FormattedCpu,
    long RamBytes,
    string FormattedRam,
    string Priority,
    bool IsHighPriority
);

public record InstalledGameInfo(
    string Name,
    string InstallPath,
    string Category,
    string DriveLetter,
    long SizeBytes,
    string FormattedSize
);

public record GameBoostResult(
    int BoostedCount,
    int AlreadyHighCount,
    List<string> AffectedGames
);

public static class GameBoostService
{
    private static readonly ConcurrentDictionary<int, ProcessPriorityClass> _originalPriorities = new();
    private static readonly ConcurrentDictionary<int, byte> _accessDeniedLogged = new();

    public static string? CheckMinecraftXmxWarning(long totalRamBytes, bool isEn)
    {
        if (totalRamBytes <= 0) return null;
        try
        {
            long totalMb = totalRamBytes / (1024 * 1024);
            string[] profileCandidates =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "TlauncherProfiles.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "launcher_profiles.json"),
                @"D:\.minecraft\TlauncherProfiles.json",
                @"D:\.minecraft\launcher_profiles.json"
            ];

            foreach (var file in profileCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(file)) continue;
                string txt = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(txt, @"-Xmx(\d+)\s*([GMgm])"))
                {
                    if (!long.TryParse(m.Groups[1].Value, out long val)) continue;
                    long xmxMb = m.Groups[2].Value.Equals("G", StringComparison.OrdinalIgnoreCase) ? val * 1024 : val;
                    if (xmxMb > totalMb * 0.65)
                    {
                        return isEn
                            ? $"Minecraft -Xmx ({xmxMb / 1024.0:0.#} GB) exceeds 65% of your physical RAM ({totalMb / 1024.0:0.#} GB), which may cause pagefile stutters."
                            : $"В настройках Minecraft выделено -Xmx ({xmxMb / 1024.0:0.#} ГБ) — это > 65% от всей ОЗУ ({totalMb / 1024.0:0.#} ГБ), возможны фризы из-за файла подкачки.";
                    }
                }
                break;
            }
        }
        catch { }
        return null;
    }

    private static readonly string[] DedicatedMinecraftLaunchers =
    [
        "tlauncher", "prismlauncher", "curseforge", "modrinth", "minecraft", "craftos", "bedrock"
    ];

    private static readonly string[] NonGameJavaTitles =
    [
        "intellij", "pycharm", "webstorm", "clion", "rider", "android studio", "eclipse", "netbeans", "jdownloader", "burp suite", "visualvm"
    ];

    private static readonly string[] NonGameWindowProcesses =
    [
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "yandex", "browser", "vivaldi",
        "telegram", "discord", "slack", "teams", "skype", "whatsapp", "zoom",
        "obs", "obs64", "streamlabs", "code", "devenv", "rider64", "idea64", "pycharm64", "webstorm64", "clion64",
        "explorer", "notepad", "notepad++", "winrar", "7zfm", "cmd", "powershell", "pwsh", "windowsterminal", "conhost", "taskmgr"
    ];

    private static bool IsMinecraftTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (NonGameJavaTitles.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase))) return false;
        return title.Contains("minecraft", StringComparison.OrdinalIgnoreCase)
            || title.Contains("tlauncher", StringComparison.OrdinalIgnoreCase)
            || title.Contains("lunar client", StringComparison.OrdinalIgnoreCase)
            || title.Contains("badlion", StringComparison.OrdinalIgnoreCase)
            || title.Contains("feather client", StringComparison.OrdinalIgnoreCase)
            || title.Contains("prism launcher", StringComparison.OrdinalIgnoreCase)
            || title.Contains("modrinth", StringComparison.OrdinalIgnoreCase)
            || title.Contains("optifine", StringComparison.OrdinalIgnoreCase);
    }

    public static (bool IsGame, string DisplayName) ResolveGameIdentity(
        string processName,
        string windowTitle = "",
        bool isRu = false,
        IReadOnlyCollection<string>? customGameProcesses = null,
        IReadOnlyCollection<string>? excludedProcesses = null)
    {
        string pName = processName.ToLowerInvariant();
        string title = windowTitle.ToLowerInvariant();

        if (excludedProcesses != null && excludedProcesses.Any(e =>
            pName.Equals(e.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase)))
        {
            return (false, string.Empty);
        }

        if (customGameProcesses != null)
        {
            foreach (var raw in customGameProcesses)
            {
                string clean = raw.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(clean) && pName.Equals(clean, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, !string.IsNullOrWhiteSpace(windowTitle) ? windowTitle : clean);
                }
            }
        }

        // 1. Minecraft ecosystem (never classify browsers, chats, or IDEs by window title)
        if (NonGameWindowProcesses.Any(n => pName == n || pName.StartsWith(n + ".", StringComparison.OrdinalIgnoreCase)))
        {
            return (false, string.Empty);
        }

        if (pName == "javaw" || pName == "java" || pName.Contains("openjdk"))
        {
            if (IsMinecraftTitle(windowTitle))
            {
                return (true, isRu ? "Майнкрафт" : "Minecraft");
            }
            return (false, string.Empty);
        }

        foreach (var alias in DedicatedMinecraftLaunchers)
        {
            if (pName.Contains(alias))
            {
                return (true, isRu ? "Майнкрафт" : "Minecraft");
            }
        }

        if (IsMinecraftTitle(windowTitle))
        {
            return (true, isRu ? "Майнкрафт" : "Minecraft");
        }

        // 2. Geometry Dash
        if (pName.Contains("geometrydash") || title.Contains("geometry dash"))
            return (true, "Geometry Dash");

        // 3. Counter-Strike 2
        if (pName == "cs2" || pName.Contains("csgo") || title.Contains("counter-strike"))
            return (true, "Counter-Strike 2");

        // 4. Dota 2
        if (pName == "dota2" || title.Contains("dota 2"))
            return (true, "Dota 2");

        // 5. Genshin Impact
        if (pName.Contains("genshinimpact") || pName.Contains("yuanshen") || title.Contains("genshin impact"))
            return (true, "Genshin Impact");

        // 6. Terraria
        if (pName.Contains("terraria") || title.Contains("terraria"))
            return (true, "Terraria");

        // 7. Brawlhalla
        if (pName.Contains("brawlhalla") || title.Contains("brawlhalla"))
            return (true, "Brawlhalla");

        // 8. Roblox
        if (pName.Contains("roblox") || title.Contains("roblox"))
            return (true, "Roblox");

        // 9. Grand Theft Auto V
        if (pName.Contains("gta5") || pName.Contains("playgtav") || title.Contains("grand theft auto"))
            return (true, "Grand Theft Auto V");

        // 10. Cyberpunk 2077
        if (pName.Contains("cyberpunk2077") || title.Contains("cyberpunk 2077"))
            return (true, "Cyberpunk 2077");

        // 11. The Witcher 3
        if (pName.Contains("witcher3") || title.Contains("witcher 3"))
            return (true, "The Witcher 3");

        // 12. Euro Truck Simulator 2
        if (pName.Contains("eurotrucks2") || title.Contains("euro truck simulator"))
            return (true, "Euro Truck Simulator 2");

        // 13. VimeWorld
        if (pName.Contains("vimeworld") || title.Contains("vimeworld"))
            return (true, "VimeWorld");

        return (false, string.Empty);
    }

    public static List<ActiveGameInfo> GetActiveGames(
        bool isRu = false,
        IReadOnlyCollection<string>? customGameProcesses = null,
        IReadOnlyCollection<string>? excludedProcesses = null,
        Dictionary<int, double>? cpuByPid = null)
    {
        var result = new List<ActiveGameInfo>();

        // Reuse single CPU sample if caller didn't already provide one
        cpuByPid ??= ProcessTrackerService.SampleProcesses(ProcessSortMode.Cpu)
            .ToDictionary(p => p.Pid, p => p.CpuPercent);

        var processes = Process.GetProcesses();
        var activePids = new HashSet<int>();

        foreach (var proc in processes)
        {
            try
            {
                if (proc.Id <= 4) continue;
                activePids.Add(proc.Id);

                string procName = proc.ProcessName;
                string winTitle = "";
                try { winTitle = proc.MainWindowTitle; } catch { }

                var (isGame, displayName) = ResolveGameIdentity(procName, winTitle, isRu, customGameProcesses, excludedProcesses);
                if (!isGame) continue;

                long ramBytes = 0;
                string priorityStr = isRu ? "Обычный" : "Normal";
                bool isHigh = false;

                try
                {
                    ramBytes = proc.WorkingSet64;
                    var pClass = proc.PriorityClass;
                    isHigh = pClass == ProcessPriorityClass.High || pClass == ProcessPriorityClass.RealTime;
                    priorityStr = pClass switch
                    {
                        ProcessPriorityClass.High => isRu ? "Высокий" : "High",
                        ProcessPriorityClass.RealTime => isRu ? "Реального времени" : "RealTime",
                        ProcessPriorityClass.AboveNormal => isRu ? "Выше среднего" : "Above Normal",
                        ProcessPriorityClass.BelowNormal => isRu ? "Ниже среднего" : "Below Normal",
                        ProcessPriorityClass.Idle => isRu ? "Низкий" : "Low",
                        _ => isRu ? "Обычный" : "Normal"
                    };
                }
                catch (Exception ex) { Logger.Debug("GameBoostService.cs: " + ex.Message); }

                double cpu = cpuByPid.TryGetValue(proc.Id, out double c) ? c : 0.0;

                result.Add(new ActiveGameInfo(
                    proc.Id,
                    displayName,
                    $"{procName}.exe",
                    cpu,
                    $"{cpu:0.0}%",
                    ramBytes,
                    StorageService.FormatBytes(ramBytes),
                    priorityStr,
                    isHigh
                ));
            }
            catch
            {
                // Access denied or exited
            }
            finally
            {
                proc.Dispose();
            }
        }

        // Purge exited PIDs from cached dictionaries
        foreach (var pid in _originalPriorities.Keys)
        {
            if (!activePids.Contains(pid)) _originalPriorities.TryRemove(pid, out _);
        }
        foreach (var pid in _accessDeniedLogged.Keys)
        {
            if (!activePids.Contains(pid)) _accessDeniedLogged.TryRemove(pid, out _);
        }

        return result;
    }

    public static GameBoostResult BoostGames(
        bool isRu = false,
        IReadOnlyCollection<string>? customGameProcesses = null,
        IReadOnlyCollection<string>? excludedProcesses = null)
    {
        var processes = Process.GetProcesses();
        int boosted = 0;
        int alreadyHigh = 0;
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var proc in processes)
        {
            string safeProcName = $"PID {proc.Id}";
            int safePid = proc.Id;
            try
            {
                if (safePid <= 4) continue;

                string procName = proc.ProcessName;
                safeProcName = procName;
                string winTitle = "";
                try { winTitle = proc.MainWindowTitle; } catch { }

                var (isGame, displayName) = ResolveGameIdentity(procName, winTitle, isRu, customGameProcesses, excludedProcesses);
                if (isGame)
                {
                    affected.Add(displayName);
                    var currentPriority = proc.PriorityClass;
                    if (currentPriority != ProcessPriorityClass.High && currentPriority != ProcessPriorityClass.RealTime)
                    {
                        _originalPriorities.TryAdd(proc.Id, currentPriority);
                        proc.PriorityClass = ProcessPriorityClass.High;
                        boosted++;
                    }
                    else
                    {
                        alreadyHigh++;
                    }
                }
            }
            catch (Exception ex)
            {
                if (_accessDeniedLogged.TryAdd(safePid, 1))
                {
                    Logger.Warn($"GameBoost boost denied/failed for '{safeProcName}' (PID {safePid}): {ex.Message}");
                }
            }
            finally
            {
                proc.Dispose();
            }
        }

        return new GameBoostResult(boosted, alreadyHigh, affected.ToList());
    }

    public static int RestoreAllGamePriorities()
    {
        int restored = 0;
        foreach (var kvp in _originalPriorities.ToArray())
        {
            try
            {
                using var proc = Process.GetProcessById(kvp.Key);
                if (proc.PriorityClass == ProcessPriorityClass.High)
                {
                    proc.PriorityClass = kvp.Value;
                    restored++;
                }
            }
            catch { }
            finally
            {
                _originalPriorities.TryRemove(kvp.Key, out _);
            }
        }
        return restored;
    }

    public static bool IsMinecraftLauncher(string name, string path, string cat)
    {
        if (cat.Equals("Launcher", StringComparison.OrdinalIgnoreCase)) return true;
        string n = name.ToLowerInvariant();
        string p = path.ToLowerInvariant();

        string[] terms = ["tlauncher", "vimeworld", "prismlauncher", "multimc", "atlauncher", "curseforge", "modrinth", "sklauncher", "tl.exe", "tlauncher.exe", "legacy launcher", "klauncher", "tl legacy"];
        foreach (var t in terms)
        {
            if (n.Contains(t) || p.Contains(t)) return true;
        }

        if (p.EndsWith(".tlauncher") || p.Contains("\\.tlauncher") || p.EndsWith(".vimeworld") || p.Contains("\\.vimeworld"))
            return true;

        return false;
    }

    public static async Task<List<InstalledGameInfo>> ScanInstalledGamesAsync(string? driveFilter = null, bool isRu = false, bool includeLaunchers = false)
    {
        return await Task.Run(() =>
        {
            var list = new List<InstalledGameInfo>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                .ToList();

            if (!string.IsNullOrEmpty(driveFilter) && driveFilter != "ALL")
            {
                drives = drives.Where(d => d.Name.StartsWith(driveFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // 1. AppData check (resolves Junctions cleanly so D:\Games\.tlauncher isn't duplicated on C:)
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string appDrive = Path.GetPathRoot(appData)?.TrimEnd('\\', ':') ?? "C";
                CheckDir(list, seenPaths, Path.Combine(appData, ".minecraft"), isRu ? "Майнкрафт (AppData)" : "Minecraft (AppData)", "Minecraft", appDrive, includeLaunchers);
                if (includeLaunchers)
                {
                    CheckDir(list, seenPaths, Path.Combine(appData, ".tlauncher"), "TLauncher (AppData)", "Launcher", appDrive, includeLaunchers);
                    CheckDir(list, seenPaths, Path.Combine(appData, ".vimeworld"), "VimeWorld (AppData)", "Launcher", appDrive, includeLaunchers);
                }

                if (!string.IsNullOrEmpty(driveFilter) && driveFilter != "ALL")
                {
                    list.RemoveAll(g => !g.DriveLetter.Equals(driveFilter, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex) { Logger.Debug("GameBoostService.cs: " + ex.Message); }

            // 2. Drive scans
            foreach (var drive in drives)
            {
                string root = drive.RootDirectory.FullName;
                string letter = drive.Name.TrimEnd('\\', ':');

                CheckDir(list, seenPaths, Path.Combine(root, ".minecraft"), isRu ? "Майнкрафт" : "Minecraft", "Minecraft", letter, includeLaunchers);
                CheckDir(list, seenPaths, Path.Combine(root, ".tlauncher"), "TLauncher", "Launcher", letter, includeLaunchers);
                CheckDir(list, seenPaths, Path.Combine(root, ".vimeworld"), "VimeWorld", "Launcher", letter, includeLaunchers);

                string gamesDir = Path.Combine(root, "Games");
                if (Directory.Exists(gamesDir))
                {
                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(gamesDir))
                        {
                            string dirName = Path.GetFileName(sub);
                            if (dirName.Equals("_CommonRedist", StringComparison.OrdinalIgnoreCase)) continue;
                            if (dirName.Equals("Unity Hub", StringComparison.OrdinalIgnoreCase)) continue;

                            string cat = dirName.Contains("minecraft", StringComparison.OrdinalIgnoreCase) ||
                                         dirName.Contains("tlauncher", StringComparison.OrdinalIgnoreCase)
                                         ? "Minecraft" : "Standalone";

                            string name = dirName.StartsWith(".") ? dirName.TrimStart('.') : dirName;
                            if (name.Equals("minecraft", StringComparison.OrdinalIgnoreCase))
                                name = isRu ? "Майнкрафт" : "Minecraft";

                            CheckDir(list, seenPaths, sub, name, cat, letter, includeLaunchers);
                        }
                    }
                    catch (Exception ex) { Logger.Debug("GameBoostService.cs: " + ex.Message); }
                }

                string[] steamPaths =
                [
                    Path.Combine(root, "SteamLibrary", "steamapps", "common"),
                    Path.Combine(root, "Program Files (x86)", "Steam", "steamapps", "common"),
                    Path.Combine(root, "Program Files", "Steam", "steamapps", "common")
                ];

                var extraSteamLibs = new HashSet<string>(steamPaths, StringComparer.OrdinalIgnoreCase);
                foreach (var sp in steamPaths)
                {
                    string vdf = Path.Combine(Directory.GetParent(sp)?.FullName ?? "", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                    {
                        try
                        {
                            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            {
                                string libPath = m.Groups[1].Value.Replace("\\\\", "\\");
                                string common = Path.Combine(libPath, "steamapps", "common");
                                extraSteamLibs.Add(common);
                            }
                        }
                        catch { }
                    }
                }

                foreach (var sp in extraSteamLibs)
                {
                    if (Directory.Exists(sp))
                    {
                        try
                        {
                            foreach (var sub in Directory.EnumerateDirectories(sp))
                            {
                                string libLetter = Path.GetPathRoot(sub)?.TrimEnd('\\', ':') ?? letter;
                                if (!string.IsNullOrEmpty(driveFilter) && driveFilter != "ALL" &&
                                    !libLetter.Equals(driveFilter, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                                CheckDir(list, seenPaths, sub, Path.GetFileName(sub), "Steam", libLetter, includeLaunchers);
                            }
                        }
                        catch (Exception ex) { Logger.Debug("GameBoostService.cs: " + ex.Message); }
                    }
                }

                string xboxDir = Path.Combine(root, "XboxGames");
                if (Directory.Exists(xboxDir))
                {
                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(xboxDir))
                        {
                            CheckDir(list, seenPaths, sub, Path.GetFileName(sub), "Xbox", letter, includeLaunchers);
                        }
                    }
                    catch (Exception ex) { Logger.Debug("GameBoostService.cs: " + ex.Message); }
                }

                string epicDir = Path.Combine(root, "Program Files", "Epic Games");
                if (Directory.Exists(epicDir))
                {
                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(epicDir))
                        {
                            CheckDir(list, seenPaths, sub, Path.GetFileName(sub), "Epic Games", letter, includeLaunchers);
                        }
                    }
                    catch (Exception ex) { Logger.Debug("GameBoostService.cs: " + ex.Message); }
                }
            }

            var finalQuery = list.Where(g => g.SizeBytes > 0);
            if (!includeLaunchers)
            {
                finalQuery = finalQuery.Where(g => !IsMinecraftLauncher(g.Name, g.InstallPath, g.Category));
            }
            return finalQuery.OrderByDescending(g => g.SizeBytes).ToList();
        });
    }

    private static void CheckDir(List<InstalledGameInfo> list, HashSet<string> seen, string path, string name, string cat, string letter, bool includeLaunchers = true)
    {
        if (!includeLaunchers && IsMinecraftLauncher(name, path, cat)) return;
        if (!Directory.Exists(path)) return;

        string resolvedPath = path;
        string actualLetter = letter;
        try
        {
            var di = new DirectoryInfo(path);
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = di.ResolveLinkTarget(returnFinalTarget: true);
                if (target != null && Directory.Exists(target.FullName))
                {
                    resolvedPath = target.FullName;
                    actualLetter = Path.GetPathRoot(resolvedPath)?.TrimEnd('\\', ':') ?? letter;
                }
            }
        }
        catch { }

        if (seen.Add(resolvedPath))
        {
            long sz = GetFolderSizeFast(resolvedPath);
            list.Add(new InstalledGameInfo(name, resolvedPath, cat, actualLetter, sz, StorageService.FormatBytes(sz)));
        }
    }

    private static long GetFolderSizeFast(string folderPath)
    {
        long total = 0;
        try
        {
            var enumOpts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            var dir = new DirectoryInfo(folderPath);
            foreach (var f in dir.EnumerateFiles("*", enumOpts))
            {
                try { total += f.Length; } catch (Exception ex) { Logger.Debug("GameBoostService.cs: " + ex.Message); }
            }
        }
        catch (Exception ex) { Logger.Debug("GameBoostService.cs: " + ex.Message); }
        return total;
    }
}
