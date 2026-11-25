using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CoreSpectre.Services;

public class UserSettingsModel
{
    public string Language { get; set; } = "EN";
    public string Theme { get; set; } = "Dark";
    public bool Use12HourTime { get; set; } = true;
    public double TelemetryInterval { get; set; } = 1.0;
    public double DiskWarningThresholdGb { get; set; } = 7.0;
    public double CpuAlertThreshold { get; set; } = 90.0;
    public double RamAlertThreshold { get; set; } = 85.0;
    public double HudOpacity { get; set; } = 90.0;
    public bool AutoRamPurge { get; set; } = true;
    public bool AutoGamePriority { get; set; } = true;
    public string WatchedDriveLetter { get; set; } = "C";
    public bool IncludeLaunchers { get; set; } = false;
    public string PurgeMode { get; set; } = "RAM";
    public List<string> ActiveTabs { get; set; } = new() { "overview", "ram", "storage", "games", "settings" };
    public List<string> VaultTabs { get; set; } = new();

    // Storage cleanup customization options (Task 7)
    public bool CleanTempFiles { get; set; } = true;
    public bool CleanSoftwareDistribution { get; set; } = true;
    public bool CleanLogsAndReports { get; set; } = true;
    public bool CleanDnsCache { get; set; } = true;
    public bool CleanRecycleBin { get; set; } = false;
    public bool CleanDismComponentCleanup { get; set; } = true;
    public bool CleanDismResetBase { get; set; } = false;
    public string StorageSortMode { get; set; } = "SizeDesc";
    public List<string> DisabledCleanupTargets { get; set; } = new();
}

public static class SettingsPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static event Action<string>? OnSettingsError;
    public static string? LastError { get; private set; }

    /// <summary>
    /// Checks whether portable mode is enabled via the existence of 'portable.flag' in the app directory.
    /// </summary>
    public static bool IsPortableMode()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return File.Exists(Path.Combine(baseDir, "portable.flag"));
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to check portable.flag: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the target path to user_settings.json:
    /// - If portable.flag exists: in app directory (portable mode).
    /// - Otherwise: in %APPDATA%\CoreSpectre\user_settings.json.
    /// </summary>
    public static string GetSettingsPath()
    {
        if (IsPortableMode())
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_settings.json");
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string coreSpectreDir = Path.Combine(appData, "CoreSpectre");
        return Path.Combine(coreSpectreDir, "user_settings.json");
    }

    /// <summary>
    /// Performs a one-time migration of user_settings.json from the app directory to %APPDATA%\CoreSpectre\
    /// if the app is in standard mode and the target settings file does not yet exist.
    /// </summary>
    public static void EnsureMigration()
    {
        if (IsPortableMode()) return;

        try
        {
            string localLegacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_settings.json");
            string appDataPath = GetSettingsPath();

            if (!File.Exists(appDataPath) && File.Exists(localLegacyPath))
            {
                string? targetDir = Path.GetDirectoryName(appDataPath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(localLegacyPath, appDataPath, overwrite: false);
                Logger.Info($"[Settings] Successfully migrated existing settings from '{localLegacyPath}' to '{appDataPath}'");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Settings] Migration of existing settings failed: {ex.Message}");
        }
    }

    public static UserSettingsModel Load()
    {
        try
        {
            EnsureMigration();
            string path = GetSettingsPath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var model = JsonSerializer.Deserialize<UserSettingsModel>(json, JsonOptions);
                if (model != null)
                {
                    model.ActiveTabs ??= new List<string> { "overview", "ram", "storage", "games", "settings" };
                    model.VaultTabs ??= new List<string>();
                    model.DisabledCleanupTargets ??= new List<string>();

                    // Safety: ensure settings tab is never completely lost
                    if (!model.ActiveTabs.Contains("settings"))
                    {
                        model.ActiveTabs.Add("settings");
                    }
                    LastError = null;
                    return model;
                }
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Logger.Error($"Failed to load user settings: {ex.Message}", ex);
            OnSettingsError?.Invoke($"Settings read error: {ex.Message}");
        }

        return new UserSettingsModel();
    }

    public static bool Save(UserSettingsModel model)
    {
        try
        {
            string path = GetSettingsPath();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(model, JsonOptions);
            File.WriteAllText(path, json);
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Logger.Error($"Failed to save user settings: {ex.Message}", ex);
            OnSettingsError?.Invoke($"Settings save error: {ex.Message}");
            return false;
        }
    }
}
