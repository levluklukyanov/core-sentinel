using CoreSentinel.Services;
using Xunit;

namespace CoreSentinel.Tests;

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
}
