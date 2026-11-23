using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CoreSentinel.Services;

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
}

public static class SettingsPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GetSettingsPath()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        if (!dir.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase))
        {
            dir = @"D:\Prog_Proj\CoreSentinel";
        }
        return Path.Combine(dir, "user_settings.json");
    }

    public static UserSettingsModel Load()
    {
        try
        {
            string path = GetSettingsPath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var model = JsonSerializer.Deserialize<UserSettingsModel>(json, JsonOptions);
                if (model != null)
                {
                    // Safety: ensure settings tab is never completely lost
                    if (!model.ActiveTabs.Contains("settings"))
                    {
                        model.ActiveTabs.Add("settings");
                    }
                    return model;
                }
            }
        }
        catch { }

        return new UserSettingsModel();
    }

    public static void Save(UserSettingsModel model)
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
        }
        catch { }
    }
}
