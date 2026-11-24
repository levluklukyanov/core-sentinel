using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

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

public static class GameSentinelService
{
    private static readonly string[] MinecraftAliases =
    [
        "javaw", "java", "tlauncher", "openjdk", "minecraft", "craftos", "bedrock", "prismlauncher", "curseforge", "modrinth"
    ];

    public static (bool IsGame, string DisplayName) ResolveGameIdentity(string processName, string windowTitle = "", bool isRu = false)
    {
        string pName = processName.ToLowerInvariant();
        string title = windowTitle.ToLowerInvariant();

        // 1. Minecraft ecosystem (javaw, java, tlauncher, openjdk, etc.)
        foreach (var alias in MinecraftAliases)
        {
            if (pName.Contains(alias))
            {
                // Verify window title if it's generic "java" to avoid false positives on non-game Java apps
                if (pName == "java" && !string.IsNullOrWhiteSpace(title) && !title.Contains("minecraft") && !title.Contains("tlauncher"))
                {
                    continue;
                }
                return (true, isRu ? "Майнкрафт" : "Minecraft");
            }
        }

        if (title.Contains("minecraft") || title.Contains("tlauncher") || title.Contains("lunar client") || title.Contains("badlion") || title.Contains("feather client"))
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

        // 14. Unity games (standalone window title)
        if (title.Contains("unity") && pName != "unity" && pName != "unity hub")
        {
            return (true, !string.IsNullOrWhiteSpace(windowTitle) ? windowTitle : "Unity Game");
        }

        return (false, string.Empty);
    }

    public static List<ActiveGameInfo> GetActiveGames(bool isRu = false)
    {
        var result = new List<ActiveGameInfo>();
        var processes = Process.GetProcesses();

        foreach (var proc in processes)
        {
            if (proc.Id <= 4) continue;

            try
            {
                string procName = proc.ProcessName;
                string winTitle = "";
                try { winTitle = proc.MainWindowTitle; } catch (Exception ex) { Logger.Debug("GameSentinelService.cs: " + ex.Message); }

                var (isGame, displayName) = ResolveGameIdentity(procName, winTitle, isRu);
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
                catch (Exception ex) { Logger.Debug("GameSentinelService.cs: " + ex.Message); }

                // Retrieve CPU estimation if sampled
                double cpu = 0.0;
                try
                {
                    var sample = ProcessTrackerService.SampleProcesses(ProcessSortMode.Cpu, procName);
                    var match = sample.FirstOrDefault(p => p.Pid == proc.Id);
                    if (match != null)
                    {
                        cpu = match.CpuPercent;
                    }
                }
                catch (Exception ex) { Logger.Debug("GameSentinelService.cs: " + ex.Message); }

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

        return result;
    }

    public static GameBoostResult BoostGames(bool isRu = false)
    {
        var processes = Process.GetProcesses();
        int boosted = 0;
        int alreadyHigh = 0;
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var proc in processes)
        {
            if (proc.Id <= 4) continue;

            try
            {
                string procName = proc.ProcessName;
                string winTitle = "";
                try { winTitle = proc.MainWindowTitle; } catch (Exception ex) { Logger.Debug("GameSentinelService.cs: " + ex.Message); }

                var (isGame, displayName) = ResolveGameIdentity(procName, winTitle, isRu);
                if (isGame)
                {
                    affected.Add(displayName);
                    if (proc.PriorityClass != ProcessPriorityClass.High && proc.PriorityClass != ProcessPriorityClass.RealTime)
                    {
                        proc.PriorityClass = ProcessPriorityClass.High;
                        boosted++;
                    }
                    else
                    {
                        alreadyHigh++;
                    }
                }
            }
            catch (Exception ex) { Logger.Debug("GameSentinelService.cs: " + ex.Message); }
            finally
            {
                proc.Dispose();
            }
        }

        return new GameBoostResult(boosted, alreadyHigh, affected.ToList());
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

            // 1. AppData check
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string mcStandard = Path.Combine(appData, ".minecraft");
                if (Directory.Exists(mcStandard) && seenPaths.Add(mcStandard))
                {
                    long sz = GetFolderSizeFast(mcStandard);
                    list.Add(new InstalledGameInfo(
                        isRu ? "Майнкрафт (AppData)" : "Minecraft (AppData)",
                        mcStandard,
                        "Minecraft",
                        Path.GetPathRoot(mcStandard)?.TrimEnd('\\', ':') ?? "C",
                        sz,
                        StorageService.FormatBytes(sz)
                    ));
                }

                if (includeLaunchers)
                {
                    string tlStandard = Path.Combine(appData, ".tlauncher");
                    if (Directory.Exists(tlStandard) && seenPaths.Add(tlStandard))
                    {
                        long sz = GetFolderSizeFast(tlStandard);
                        list.Add(new InstalledGameInfo(
                            "TLauncher (AppData)",
                            tlStandard,
                            "Launcher",
                            Path.GetPathRoot(tlStandard)?.TrimEnd('\\', ':') ?? "C",
                            sz,
                            StorageService.FormatBytes(sz)
                        ));
                    }
                }
            }
            catch (Exception ex) { Logger.Debug("GameSentinelService.cs: " + ex.Message); }

            // 2. Drive scans
            foreach (var drive in drives)
            {
                string root = drive.RootDirectory.FullName;
                string letter = drive.Name.TrimEnd('\\', ':');

                // Root .minecraft / .tlauncher
                CheckDir(list, seenPaths, Path.Combine(root, ".minecraft"), isRu ? "Майнкрафт" : "Minecraft", "Minecraft", letter, includeLaunchers);
                CheckDir(list, seenPaths, Path.Combine(root, ".tlauncher"), "TLauncher", "Launcher", letter, includeLaunchers);
                CheckDir(list, seenPaths, Path.Combine(root, ".vimeworld"), "VimeWorld", "Launcher", letter, includeLaunchers);

                // Games directory
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
                    catch (Exception ex) { Logger.Debug("GameSentinelService.cs: " + ex.Message); }
                }

                // Steam libraries
                string[] steamPaths =
                [
                    Path.Combine(root, "SteamLibrary", "steamapps", "common"),
                    Path.Combine(root, "Program Files (x86)", "Steam", "steamapps", "common"),
                    Path.Combine(root, "Program Files", "Steam", "steamapps", "common")
                ];

                foreach (var sp in steamPaths)
                {
                    if (Directory.Exists(sp))
                    {
                        try
                        {
                            foreach (var sub in Directory.EnumerateDirectories(sp))
                            {
                                string gName = Path.GetFileName(sub);
                                CheckDir(list, seenPaths, sub, gName, "Steam", letter, includeLaunchers);
                            }
                        }
                        catch (Exception ex) { Logger.Debug("GameSentinelService.cs: " + ex.Message); }
                    }
                }

                // XboxGames
                string xboxDir = Path.Combine(root, "XboxGames");
                if (Directory.Exists(xboxDir))
                {
                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(xboxDir))
                        {
                            string gName = Path.GetFileName(sub);
                            CheckDir(list, seenPaths, sub, gName, "Xbox", letter, includeLaunchers);
                        }
                    }
                    catch (Exception ex) { Logger.Debug("GameSentinelService.cs: " + ex.Message); }
                }

                // Epic Games
                string epicDir = Path.Combine(root, "Program Files", "Epic Games");
                if (Directory.Exists(epicDir))
                {
                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(epicDir))
                        {
                            string gName = Path.GetFileName(sub);
                            CheckDir(list, seenPaths, sub, gName, "Epic Games", letter, includeLaunchers);
                        }
                    }
                    catch (Exception ex) { Logger.Debug("GameSentinelService.cs: " + ex.Message); }
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
        if (Directory.Exists(path) && seen.Add(path))
        {
            long sz = GetFolderSizeFast(path);
            list.Add(new InstalledGameInfo(name, path, cat, letter, sz, StorageService.FormatBytes(sz)));
        }
    }

    private static long GetFolderSizeFast(string folderPath)
    {
        long total = 0;
        try
        {
            var dir = new DirectoryInfo(folderPath);
            foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { total += f.Length; } catch (Exception ex) { Logger.Debug("GameSentinelService.cs: " + ex.Message); }
            }
        }
        catch (Exception ex) { Logger.Debug("GameSentinelService.cs: " + ex.Message); }
        return total;
    }
}
