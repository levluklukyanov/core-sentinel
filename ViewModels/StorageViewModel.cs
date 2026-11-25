using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreSpectre.Services;

namespace CoreSpectre.ViewModels;

public record CleanTargetItem(string Key, string Name, string Path, string FormattedSize, long Bytes, bool IsSelected = true, bool RequiresAdmin = false);

public partial class StorageViewModel : ObservableObject
{
    [ObservableProperty]
    private string _watchedDrive = "C";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private int _cooldownSeconds;

    [ObservableProperty]
    private string _sortMode = "SizeDesc";

    [ObservableProperty]
    private string _totalSizeFormatted = "0 B";

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private bool _isAdmin;

    // Task 7: Configurable Clean Options
    [ObservableProperty]
    private bool _cleanTempFiles = true;

    [ObservableProperty]
    private bool _cleanSoftwareDistribution = true;

    [ObservableProperty]
    private bool _cleanLogsAndReports = true;

    [ObservableProperty]
    private bool _cleanDnsCache = true;

    [ObservableProperty]
    private bool _cleanRecycleBin = false;

    [ObservableProperty]
    private bool _cleanDismComponentCleanup = true;

    [ObservableProperty]
    private bool _cleanDismResetBase = false;

    public ObservableCollection<CleanTargetItem> Targets { get; } = new();
    public ObservableCollection<string> StorageLogs { get; } = new();

    public StorageViewModel()
    {
        IsAdmin = StorageService.IsAdministrator();
        LoadFromSettings();
    }

    public void LoadFromSettings()
    {
        var settings = SettingsPersistenceService.Load();
        CleanTempFiles = settings.CleanTempFiles;
        CleanSoftwareDistribution = settings.CleanSoftwareDistribution;
        CleanLogsAndReports = settings.CleanLogsAndReports;
        CleanDnsCache = settings.CleanDnsCache;
        CleanRecycleBin = settings.CleanRecycleBin;
        CleanDismComponentCleanup = settings.CleanDismComponentCleanup;
        CleanDismResetBase = settings.CleanDismResetBase;
        SortMode = settings.StorageSortMode ?? "SizeDesc";
    }

    public void SaveOptionsToSettings()
    {
        var settings = SettingsPersistenceService.Load();
        settings.CleanTempFiles = CleanTempFiles;
        settings.CleanSoftwareDistribution = CleanSoftwareDistribution;
        settings.CleanLogsAndReports = CleanLogsAndReports;
        settings.CleanDnsCache = CleanDnsCache;
        settings.CleanRecycleBin = CleanRecycleBin;
        settings.CleanDismComponentCleanup = CleanDismComponentCleanup;
        settings.CleanDismResetBase = CleanDismResetBase;
        settings.StorageSortMode = SortMode;
        SettingsPersistenceService.Save(settings);
    }

    public void ApplySorting()
    {
        var list = Targets.ToList();
        list = SortMode switch
        {
            "SizeAsc" => list.OrderBy(t => t.Bytes).ThenBy(t => t.Name).ToList(),
            "NameAsc" => list.OrderBy(t => t.Name).ToList(),
            _ => list.OrderByDescending(t => t.Bytes).ThenBy(t => t.Name).ToList()
        };

        Targets.Clear();
        foreach (var item in list)
        {
            Targets.Add(item);
        }
    }

    public void Log(string msg)
    {
        string time = DateTime.Now.ToString("HH:mm:ss");
        StorageLogs.Add($"[{time}] {msg}");
    }
}
