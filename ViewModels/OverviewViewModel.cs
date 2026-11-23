using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CoreSentinel.Services;

namespace CoreSentinel.ViewModels;

public partial class OverviewViewModel : ObservableObject
{
    [ObservableProperty]
    private double _cpuLoadPercent;

    [ObservableProperty]
    private string _formattedCpu = "0%";

    [ObservableProperty]
    private double _ramUsedPercent;

    [ObservableProperty]
    private string _formattedRam = "0.0 / 0.0 GB";

    [ObservableProperty]
    private double _driveFreeGb;

    [ObservableProperty]
    private double _driveTotalGb;

    [ObservableProperty]
    private double _driveUsedPercent;

    [ObservableProperty]
    private string _formattedDrive = "0.0 / 0.0 GB";

    [ObservableProperty]
    private string _driveLetter = "C";

    [ObservableProperty]
    private bool _isGameRunning;

    [ObservableProperty]
    private string _activeGameName = string.Empty;

    [ObservableProperty]
    private string _healthBadge = "Optimal";

    [ObservableProperty]
    private bool _cpuAlert;

    [ObservableProperty]
    private bool _ramAlert;

    public void UpdateFromSnapshot(TelemetrySnapshot sample, string watchedDrive, double cpuThreshold, double ramThreshold, bool isEn)
    {
        CpuLoadPercent = Math.Round(sample.CpuLoadPercent, 1);
        FormattedCpu = $"{Math.Round(sample.CpuLoadPercent)}%";

        RamUsedPercent = Math.Round(sample.RamUsedPercent, 1);
        double ramUsedGb = sample.RamUsedBytes / (1024.0 * 1024.0 * 1024.0);
        double ramTotalGb = sample.RamTotalBytes / (1024.0 * 1024.0 * 1024.0);
        FormattedRam = $"{ramUsedGb:F1} / {ramTotalGb:F1} GB";

        DriveLetter = watchedDrive;
        bool isWatchedD = string.Equals(watchedDrive, "D", StringComparison.OrdinalIgnoreCase) && sample.HasSecondaryDataDrive;
        DriveFreeGb = isWatchedD ? sample.DiskDFreeGb : sample.DiskCFreeGb;
        DriveTotalGb = isWatchedD ? sample.DiskDTotalGb : sample.DiskCTotalGb;

        double driveUsed = Math.Max(0, DriveTotalGb - DriveFreeGb);
        DriveUsedPercent = DriveTotalGb > 0 ? (driveUsed / DriveTotalGb) * 100.0 : 0.0;
        FormattedDrive = $"{DriveFreeGb:F1} GB free of {DriveTotalGb:F1} GB";

        IsGameRunning = sample.IsGameRunning;
        ActiveGameName = sample.ActiveGameName;

        CpuAlert = sample.CpuLoadPercent >= cpuThreshold;
        RamAlert = sample.RamUsedPercent >= ramThreshold;

        if (CpuAlert || RamAlert)
        {
            HealthBadge = isEn ? "High Load" : "Высокая нагрузка";
        }
        else
        {
            HealthBadge = isEn ? "Healthy" : "В норме";
        }
    }
}
