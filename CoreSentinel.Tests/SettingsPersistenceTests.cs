using System.Text.Json;
using CoreSentinel.Services;
using Xunit;

namespace CoreSentinel.Tests;

public class SettingsPersistenceTests
{
    [Fact]
    public void UserSettingsModel_DefaultValues_AdhereToStandards()
    {
        var model = new UserSettingsModel();

        Assert.Equal("EN", model.Language);
        Assert.Equal("Dark", model.Theme);
        Assert.True(model.Use12HourTime);
        Assert.Equal(1.0, model.TelemetryInterval);
        Assert.Equal(7.0, model.DiskWarningThresholdGb);
        Assert.Equal(90.0, model.CpuAlertThreshold);
        Assert.Equal(85.0, model.RamAlertThreshold);
        Assert.True(model.AutoRamPurge);
        Assert.True(model.AutoGamePriority);
        Assert.Equal("C", model.WatchedDriveLetter);
        Assert.False(model.IncludeLaunchers);
        Assert.Equal("RAM", model.PurgeMode);

        // Core tabs must all be present
        Assert.Contains("overview", model.ActiveTabs);
        Assert.Contains("ram", model.ActiveTabs);
        Assert.Contains("storage", model.ActiveTabs);
        Assert.Contains("games", model.ActiveTabs);
        Assert.Contains("settings", model.ActiveTabs);

        // Storage cleanup options
        Assert.True(model.CleanTempFiles);
        Assert.True(model.CleanSoftwareDistribution);
        Assert.True(model.CleanLogsAndReports);
        Assert.True(model.CleanDnsCache);
        Assert.False(model.CleanRecycleBin);
        Assert.True(model.CleanDismComponentCleanup);
        Assert.False(model.CleanDismResetBase);
        Assert.Equal("SizeDesc", model.StorageSortMode);
    }

    [Fact]
    public void SerializationRoundTrip_PreservesAllSettings()
    {
        var original = new UserSettingsModel
        {
            Language = "RU",
            Theme = "Light",
            Use12HourTime = false,
            TelemetryInterval = 2.5,
            DiskWarningThresholdGb = 15.0,
            CpuAlertThreshold = 95.0,
            RamAlertThreshold = 92.0,
            HudOpacity = 80.0,
            AutoRamPurge = false,
            AutoGamePriority = false,
            WatchedDriveLetter = "D",
            IncludeLaunchers = true,
            PurgeMode = "STANDBY",
            ActiveTabs = new List<string> { "overview", "storage" },
            VaultTabs = new List<string> { "games", "settings" },
            CleanTempFiles = false,
            CleanSoftwareDistribution = false,
            CleanLogsAndReports = false,
            CleanDnsCache = false,
            CleanRecycleBin = true,
            CleanDismComponentCleanup = false,
            CleanDismResetBase = true,
            StorageSortMode = "NameAsc"
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        string json = JsonSerializer.Serialize(original, options);
        var restored = JsonSerializer.Deserialize<UserSettingsModel>(json, options);

        Assert.NotNull(restored);
        Assert.Equal("RU", restored.Language);
        Assert.Equal("Light", restored.Theme);
        Assert.False(restored.Use12HourTime);
        Assert.Equal(2.5, restored.TelemetryInterval);
        Assert.Equal(15.0, restored.DiskWarningThresholdGb);
        Assert.Equal(95.0, restored.CpuAlertThreshold);
        Assert.Equal(92.0, restored.RamAlertThreshold);
        Assert.Equal(80.0, restored.HudOpacity);
        Assert.False(restored.AutoRamPurge);
        Assert.False(restored.AutoGamePriority);
        Assert.Equal("D", restored.WatchedDriveLetter);
        Assert.True(restored.IncludeLaunchers);
        Assert.Equal("STANDBY", restored.PurgeMode);
        Assert.Equal(2, restored.ActiveTabs.Count);
        Assert.Equal(2, restored.VaultTabs.Count);
        Assert.False(restored.CleanTempFiles);
        Assert.False(restored.CleanSoftwareDistribution);
        Assert.False(restored.CleanLogsAndReports);
        Assert.False(restored.CleanDnsCache);
        Assert.True(restored.CleanRecycleBin);
        Assert.False(restored.CleanDismComponentCleanup);
        Assert.True(restored.CleanDismResetBase);
        Assert.Equal("NameAsc", restored.StorageSortMode);
    }
}
