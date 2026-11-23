using System.IO;
using System.IO.Compression;

namespace CoreSentinel.Services;

public record MinecraftWorld(
    string FolderName,
    string DisplayName,
    string FullPath,
    long SizeBytes,
    DateTime LastModified,
    bool HasLevelDat,
    int BackupCount
);

public record BackupRecord(
    string FileName,
    string FullPath,
    long SizeBytes,
    DateTime CreatedAt,
    string FormattedSize
);

public static class MinecraftService
{
    public static string BackupBaseDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "CoreSentinel",
        "Minecraft_Backups"
    );

    public static string GetActiveSavesDirectory()
    {
        // 1. Стандартный путь в AppData пользователя
        string standardPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft",
            "saves");

        if (Directory.Exists(standardPath))
            return standardPath;

        // 2. Поиск на доступных дисках системы
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

                string root = drive.RootDirectory.FullName;
                string[] candidates =
                [
                    Path.Combine(root, ".minecraft", "saves"),
                    Path.Combine(root, "Games", ".minecraft", "saves"),
                    Path.Combine(root, "Games", ".tlauncher", "saves"),
                    Path.Combine(root, "Games", ".vimeworld", "saves")
                ];

                foreach (var candidate in candidates)
                {
                    if (Directory.Exists(candidate))
                        return candidate;
                }
            }
        }
        catch { }

        // По умолчанию возвращаем стандартный путь
        return standardPath;
    }

    public static List<MinecraftWorld> GetAvailableWorlds(string? customSavesDir = null)
    {
        var worlds = new List<MinecraftWorld>();
        string savesDir = customSavesDir ?? GetActiveSavesDirectory();

        if (!Directory.Exists(savesDir))
            return worlds;

        try
        {
            var root = new DirectoryInfo(savesDir);
            foreach (var dir in root.EnumerateDirectories())
            {
                string levelDat = Path.Combine(dir.FullName, "level.dat");
                bool hasLevel = File.Exists(levelDat);

                long size = 0;
                try
                {
                    foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        size += f.Length;
                    }
                }
                catch
                {
                    // partial read if locked
                }

                int backupCount = GetBackupsForWorld(dir.Name).Count;

                worlds.Add(new MinecraftWorld(
                    dir.Name,
                    dir.Name,
                    dir.FullName,
                    size,
                    dir.LastWriteTime,
                    hasLevel,
                    backupCount
                ));
            }
        }
        catch
        {
            // Directory read error
        }

        return worlds.OrderByDescending(w => w.LastModified).ToList();
    }

    public static List<BackupRecord> GetBackupsForWorld(string worldFolderName)
    {
        var records = new List<BackupRecord>();
        if (!Directory.Exists(BackupBaseDir))
            return records;

        try
        {
            string safeFolderName = Path.GetFileName(worldFolderName);
            var dir = new DirectoryInfo(BackupBaseDir);
            string searchPrefix = $"backup_{safeFolderName}_";

            foreach (var file in dir.EnumerateFiles("*.zip"))
            {
                if (file.Name.StartsWith(searchPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    records.Add(new BackupRecord(
                        file.Name,
                        file.FullName,
                        file.Length,
                        file.CreationTime,
                        StorageService.FormatBytes(file.Length)
                    ));
                }
            }
        }
        catch
        {
            // Read error
        }

        return records.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public static (bool Success, string Message, string? BackupPath) CreateSnapshot(string worldFolderName)
    {
        string safeFolderName = Path.GetFileName(worldFolderName);
        string savesDir = GetActiveSavesDirectory();
        string worldPath = Path.Combine(savesDir, safeFolderName);

        if (!Directory.Exists(worldPath))
            return (false, $"Папка мира не найдена: {worldPath}", null);

        try
        {
            Directory.CreateDirectory(BackupBaseDir);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string zipName = $"backup_{safeFolderName}_{timestamp}.zip";
            string zipPath = Path.Combine(BackupBaseDir, zipName);

            // Zip the world folder (Fastest compression level to avoid long disk stalls)
            ZipFile.CreateFromDirectory(worldPath, zipPath, CompressionLevel.Fastest, false);

            return (true, $"Снимок успешно создан: {zipName}", zipPath);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка архивации: {ex.Message}", null);
        }
    }

    public static (bool Success, string Message) RestoreSnapshot(string worldFolderName, string backupZipPath)
    {
        if (!File.Exists(backupZipPath))
            return (false, "Файл архива не найден.");

        string safeFolderName = Path.GetFileName(worldFolderName);
        string savesDir = GetActiveSavesDirectory();
        string worldPath = Path.Combine(savesDir, safeFolderName);

        try
        {
            // Non-destructive rollback: rename existing world folder as safety net
            if (Directory.Exists(worldPath))
            {
                string safetyBackup = Path.Combine(savesDir, $"{safeFolderName}_safety_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.Move(worldPath, safetyBackup);
            }

            Directory.CreateDirectory(worldPath);
            ZipFile.ExtractToDirectory(backupZipPath, worldPath, true);

            return (true, $"Мир '{safeFolderName}' успешно восстановлен из резервной копии!");
        }
        catch (Exception ex)
        {
            return (false, $"Сбой восстановления: {ex.Message}");
        }
    }
}
