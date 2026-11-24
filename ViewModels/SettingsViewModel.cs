using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreSpectre.Services;

namespace CoreSpectre.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly UserSettingsModel _model;
    private readonly Action? _onSettingsChanged;

    [ObservableProperty]
    private string _language = "EN";

    [ObservableProperty]
    private string _theme = "Dark";

    [ObservableProperty]
    private bool _use12HourTime = true;

    [ObservableProperty]
    private double _telemetryInterval = 1.0;

    [ObservableProperty]
    private double _diskWarningThresholdGb = 7.0;

    [ObservableProperty]
    private double _cpuAlertThreshold = 90.0;

    [ObservableProperty]
    private double _ramAlertThreshold = 85.0;

    [ObservableProperty]
    private double _hudOpacity = 90.0;

    [ObservableProperty]
    private bool _autoRamPurge = true;

    [ObservableProperty]
    private bool _autoGamePriority = true;

    [ObservableProperty]
    private string _watchedDriveLetter = "C";

    [ObservableProperty]
    private bool _includeLaunchers = false;

    [ObservableProperty]
    private string _purgeMode = "RAM";

    public SettingsViewModel(UserSettingsModel? model = null, Action? onSettingsChanged = null)
    {
        _model = model ?? SettingsPersistenceService.Load();
        _onSettingsChanged = onSettingsChanged;
        LoadFromModel();
    }

    public void LoadFromModel()
    {
        Language = _model.Language;
        Theme = _model.Theme;
        Use12HourTime = _model.Use12HourTime;
        TelemetryInterval = _model.TelemetryInterval > 0 ? _model.TelemetryInterval : 1.0;
        DiskWarningThresholdGb = _model.DiskWarningThresholdGb > 0 ? _model.DiskWarningThresholdGb : 7.0;
        CpuAlertThreshold = _model.CpuAlertThreshold > 0 ? _model.CpuAlertThreshold : 90.0;
        RamAlertThreshold = _model.RamAlertThreshold > 0 ? _model.RamAlertThreshold : 85.0;
        HudOpacity = _model.HudOpacity;
        AutoRamPurge = _model.AutoRamPurge;
        AutoGamePriority = _model.AutoGamePriority;
        WatchedDriveLetter = !string.IsNullOrWhiteSpace(_model.WatchedDriveLetter) ? _model.WatchedDriveLetter : "C";
        IncludeLaunchers = _model.IncludeLaunchers;
        PurgeMode = _model.PurgeMode;
    }

    public void SaveToModel()
    {
        _model.Language = Language;
        _model.Theme = Theme;
        _model.Use12HourTime = Use12HourTime;
        _model.TelemetryInterval = TelemetryInterval;
        _model.DiskWarningThresholdGb = DiskWarningThresholdGb;
        _model.CpuAlertThreshold = CpuAlertThreshold;
        _model.RamAlertThreshold = RamAlertThreshold;
        _model.HudOpacity = HudOpacity;
        _model.AutoRamPurge = AutoRamPurge;
        _model.AutoGamePriority = AutoGamePriority;
        _model.WatchedDriveLetter = WatchedDriveLetter;
        _model.IncludeLaunchers = IncludeLaunchers;
        _model.PurgeMode = PurgeMode;

        SettingsPersistenceService.Save(_model);
        _onSettingsChanged?.Invoke();
    }

    [RelayCommand]
    public void ToggleLanguage()
    {
        Language = Language == "RU" ? "EN" : "RU";
        SaveToModel();
    }

    [RelayCommand]
    public void ToggleTheme()
    {
        Theme = Theme == "Dark" ? "Light" : "Dark";
        SaveToModel();
    }
}
