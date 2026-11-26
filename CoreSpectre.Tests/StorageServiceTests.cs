using System;
using System.IO;
using CoreSpectre.Services;
using Xunit;

namespace CoreSpectre.Tests;

public class StorageServiceTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(1099511627776L, "1 TB")]
    public void FormatBytes_ProducesExpectedOutput(long bytes, string expected)
    {
        string actual = StorageService.FormatBytes(bytes);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StorageCleanupOptions_DefaultValues_AreSafe()
    {
        var options = new StorageCleanupOptions();

        // Essential safe cleanups should be active by default
        Assert.True(options.CleanTemp);
        Assert.True(options.CleanSoftwareDistribution);
        Assert.True(options.CleanLogsAndReports);
        Assert.True(options.CleanDnsCache);
        Assert.True(options.CleanDismComponentCleanup);

        // Risky or irreversible operations must be disabled by default
        Assert.False(options.CleanRecycleBin);
        Assert.False(options.CleanDismResetBase);
    }

    [Fact]
    public void CleanableTarget_ToggleSelection_UpdatesCorrectly()
    {
        var target = new CleanableTarget("Test Target", @"C:\Temp", 1024 * 1024, 15, RequiresAdmin: false, IsSelected: true);

        Assert.Equal("Test Target", target.Name);
        Assert.True(target.IsSelected);

        var toggled = target with { IsSelected = false };
        Assert.False(toggled.IsSelected);
    }

    [Fact]
    public void TargetSorting_SizeDescending_OrdersLargestFirst()
    {
        var targets = new List<CleanableTarget>
        {
            new("Small", @"C:\Small", 100, 1),
            new("Large", @"C:\Large", 5000, 10),
            new("Medium", @"C:\Medium", 1000, 5)
        };

        var sorted = targets.OrderByDescending(t => t.Bytes).ToList();

        Assert.Equal("Large", sorted[0].Name);
        Assert.Equal("Medium", sorted[1].Name);
        Assert.Equal("Small", sorted[2].Name);
    }

    [Fact]
    public void TargetSorting_SizeAscending_OrdersSmallestFirst()
    {
        var targets = new List<CleanableTarget>
        {
            new("Small", @"C:\Small", 100, 1),
            new("Large", @"C:\Large", 5000, 10),
            new("Medium", @"C:\Medium", 1000, 5)
        };

        var sorted = targets.OrderBy(t => t.Bytes).ToList();

        Assert.Equal("Small", sorted[0].Name);
        Assert.Equal("Medium", sorted[1].Name);
        Assert.Equal("Large", sorted[2].Name);
    }

    [Fact]
    public void TargetSorting_NameAscending_OrdersAlphabetically()
    {
        var targets = new List<CleanableTarget>
        {
            new("Zebra Cache", @"C:\Z", 100, 1),
            new("Apple Temp", @"C:\A", 5000, 10),
            new("Banana Logs", @"C:\B", 1000, 5)
        };

        var sorted = targets.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal("Apple Temp", sorted[0].Name);
        Assert.Equal("Banana Logs", sorted[1].Name);
        Assert.Equal("Zebra Cache", sorted[2].Name);
    }

    [Fact]
    public void ScanTempLocations_AssignsUniqueKeysAndRespectsDisabledTargets()
    {
        var settings = new UserSettingsModel
        {
            CleanTempFiles = true,
            CleanRecycleBin = false,
            DisabledCleanupTargets = new List<string> { "user_temp" }
        };

        var targets = StorageService.ScanTempLocations(isEn: true, settings);

        Assert.NotEmpty(targets);
        Assert.All(targets, t => Assert.False(string.IsNullOrWhiteSpace(t.Key)));

        var userTemp = targets.FirstOrDefault(t => t.Key == "user_temp");
        if (userTemp != null)
        {
            Assert.False(userTemp.IsSelected);
        }

        var recycleBin = targets.FirstOrDefault(t => t.Key == "recycle_bin");
        Assert.NotNull(recycleBin);
        Assert.False(recycleBin.IsSelected);
    }

    [Theory]
    [InlineData("CoreSpectre_Elev_0123456789abcdef0123456789abcdef", true)]
    [InlineData("CoreSpectre_Elev_123", false)]
    [InlineData("\\\\attacker\\pipe\\bad", false)]
    [InlineData("", false)]
    public void IsValidPipeName_AcceptsOnlyExpectedGuidFormat(string pipeName, bool expected)
    {
        Assert.Equal(expected, StorageService.IsValidPipeName(pipeName));
    }

    [Fact]
    public void CleanDirectoryContents_DeletesFilesAndRespectsCancellation()
    {
        string testDir = Path.Combine(AppContext.BaseDirectory, $"clean_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
        string subDir = Path.Combine(testDir, "sub");
        Directory.CreateDirectory(subDir);
        string fileA = Path.Combine(subDir, "cache.tmp");
        File.WriteAllText(fileA, "123456");

        try
        {
            using var canceledCts = new System.Threading.CancellationTokenSource();
            canceledCts.Cancel();
            var canceledRes = StorageService.CleanDirectoryContents(testDir, canceledCts.Token);
            Assert.Equal(0, canceledRes.FilesRemoved);
            Assert.True(File.Exists(fileA));

            var res = StorageService.CleanDirectoryContents(testDir);
            Assert.Equal(1, res.FilesRemoved);
            Assert.True(res.BytesFreed >= 6);
            Assert.False(File.Exists(fileA));
            Assert.False(Directory.Exists(subDir));
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void CleanDirectoryContents_PreservesCbsLog()
    {
        string testDir = Path.Combine(AppContext.BaseDirectory, $"cbs_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
        string cbsFile = Path.Combine(testDir, "CBS.log");
        string oldLogFile = Path.Combine(testDir, "CbsPersist_001.log");
        File.WriteAllText(cbsFile, "active log");
        File.WriteAllText(oldLogFile, "archived log");

        try
        {
            var res = StorageService.CleanDirectoryContents(testDir);
            Assert.Equal(1, res.FilesRemoved);
            Assert.True(File.Exists(cbsFile));
            Assert.False(File.Exists(oldLogFile));
            Assert.True(res.FilesSkipped >= 1);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    [Theory]
    [InlineData("d:", "D")]
    [InlineData("C:\\Windows\\System32", "C")]
    [InlineData("invalid", "C")]
    [InlineData("", "C")]
    public void ValidateDriveLetter_ReturnsAsciiDriveOrFallback(string input, string expected)
    {
        Assert.Equal(expected, StorageService.ValidateDriveLetter(input, "C"));
    }

    [Fact]
    public void ElevatedCleanupJob_SerializesTempMinAgeDays()
    {
        var job = new ElevatedCleanupJob(
            IsEnglish: true,
            CleanTemp: true,
            CleanSoftwareDistribution: false,
            CleanLogsAndReports: false,
            CleanGpuShaderCache: false,
            CleanBrowserCache: false,
            CleanDismComponentCleanup: false,
            CleanDismResetBase: false,
            DisabledTargetKeys: new List<string>(),
            Action: "cleanup",
            TempMinAgeDays: 7);

        string json = System.Text.Json.JsonSerializer.Serialize(job);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ElevatedCleanupJob>(json);
        Assert.NotNull(restored);
        Assert.Equal(7, restored.TempMinAgeDays);
    }

    [Fact]
    public void PreviewTargetFiles_HandlesUnknownKeyAndBoundsMaxItems()
    {
        var unknown = StorageService.PreviewTargetFiles("non_existent_key", maxItems: 10);
        Assert.Empty(unknown);

        var tempPreview = StorageService.PreviewTargetFiles("user_temp", maxItems: 5);
        Assert.True(tempPreview.Count <= 5);
        Assert.All(tempPreview, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
            Assert.False(string.IsNullOrWhiteSpace(item.FormattedSize));
        });
    }

    [Fact]
    public void CleanDirectoryContents_RespectsMinAgeDays()
    {
        string testDir = Path.Combine(AppContext.BaseDirectory, $"age_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
        string freshFile = Path.Combine(testDir, "fresh.tmp");
        string oldFile = Path.Combine(testDir, "old.tmp");
        File.WriteAllText(freshFile, "fresh");
        File.WriteAllText(oldFile, "old");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-10));

        try
        {
            var res = StorageService.CleanDirectoryContents(testDir, minAgeDays: 3);
            Assert.Equal(1, res.FilesRemoved);
            Assert.True(File.Exists(freshFile));
            Assert.False(File.Exists(oldFile));
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }
}
