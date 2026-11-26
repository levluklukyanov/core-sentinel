using CoreSpectre.Services;
using Xunit;

namespace CoreSpectre.Tests;

public class MemoryServiceTests
{
    [Theory]
    [InlineData("dwm", true)]
    [InlineData("dwm.exe", true)]
    [InlineData("audiodg", true)]
    [InlineData("MsMpEng", true)]
    [InlineData("explorer", true)]
    [InlineData("CoreSpectre", true)]
    [InlineData("notepad", false)]
    [InlineData("chrome", false)]
    [InlineData("", false)]
    public void IsProtectedFromWorkingSetTrim_ProtectsCriticalAndAvProcesses(string processName, bool expectedProtected)
    {
        Assert.Equal(expectedProtected, MemoryService.IsProtectedFromWorkingSetTrim(processName));
    }

    [Fact]
    public void GetCurrentStatus_ReturnsValidPhysicalMemoryMetrics()
    {
        var status = MemoryService.GetCurrentStatus();

        Assert.True(status.TotalPhysicalBytes > 0);
        Assert.True(status.AvailablePhysicalBytes >= 0);
        Assert.True(status.UsedPercent >= 0.0 && status.UsedPercent <= 100.0);
    }

    [Fact]
    public void GetPerformanceDetails_ReturnsCommitAndCacheMetrics()
    {
        var details = MemoryService.GetPerformanceDetails();

        Assert.NotNull(details);
        Assert.True(details.CommitLimitBytes > 0);
        Assert.True(details.CommitTotalBytes >= 0);
        Assert.True(details.SystemCacheBytes >= 0);
    }

    [Fact]
    public void PurgeStandbyList_RequiresAdminWhenNotElevated()
    {
        if (StorageService.IsAdministrator())
        {
            return;
        }

        var (success, bytesFreed, message) = MemoryService.PurgeStandbyList(isEn: true);
        Assert.False(success);
        Assert.Equal(0, bytesFreed);
        Assert.Contains("Administrator", message);
    }

    [Theory]
    [InlineData(System.Diagnostics.ProcessPriorityClass.Normal, true)]
    [InlineData(System.Diagnostics.ProcessPriorityClass.AboveNormal, true)]
    [InlineData(System.Diagnostics.ProcessPriorityClass.High, true)]
    [InlineData(System.Diagnostics.ProcessPriorityClass.BelowNormal, false)]
    [InlineData(System.Diagnostics.ProcessPriorityClass.Idle, false)]
    [InlineData(System.Diagnostics.ProcessPriorityClass.RealTime, false)]
    public void ShouldThrottlePriority_MatchesExpectedPriorityClasses(
        System.Diagnostics.ProcessPriorityClass priority, bool expected)
    {
        Assert.Equal(expected, MemoryService.ShouldThrottlePriority(priority));
    }
}
