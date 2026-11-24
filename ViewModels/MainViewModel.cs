using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoreSentinel.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public OverviewViewModel Overview { get; } = new();
    public PurgeViewModel Purge { get; } = new();
    public StorageViewModel Storage { get; } = new();
    public GamesViewModel Games { get; } = new();
    public SettingsViewModel Settings { get; } = new();

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _statusFooter = "CoreSentinel ready.";

    public MainViewModel()
    {
        Purge.UpdateLabels(Settings.Language);
    }
}
