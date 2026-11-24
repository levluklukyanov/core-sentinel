using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreSpectre.Services;

namespace CoreSpectre.ViewModels;

public partial class GamesViewModel : ObservableObject
{
    [ObservableProperty]
    private string _driveFilter = "ALL";

    [ObservableProperty]
    private bool _includeLaunchers = false;

    [ObservableProperty]
    private bool _isScanning = false;

    [ObservableProperty]
    private string _scanStatusText = string.Empty;

    [ObservableProperty]
    private string _scanCountText = string.Empty;

    public ObservableCollection<ActiveGameInfo> ActiveGames { get; } = new();
    public ObservableCollection<InstalledGameInfo> InstalledGames { get; } = new();

    public GamesViewModel()
    {
    }

    [RelayCommand]
    public async Task ScanInstalledAsync(bool isRussian)
    {
        if (IsScanning) return;
        IsScanning = true;
        ScanStatusText = isRussian
            ? $"Сканирование накопителей ({DriveFilter})..."
            : $"Scanning storage ({DriveFilter})...";
        ScanCountText = string.Empty;

        try
        {
            var results = await GameSentinelService.ScanInstalledGamesAsync(DriveFilter, isRussian, IncludeLaunchers);
            InstalledGames.Clear();
            foreach (var g in results)
            {
                InstalledGames.Add(g);
            }

            long totalBytes = InstalledGames.Sum(g => g.SizeBytes);
            string formattedSize = StorageService.FormatBytes(totalBytes);

            ScanCountText = isRussian
                ? $"{InstalledGames.Count} игр ({formattedSize})"
                : $"{InstalledGames.Count} games ({formattedSize})";

            ScanStatusText = isRussian
                ? $"Сканирование завершено: найдено {InstalledGames.Count} игр."
                : $"Scan completed: {InstalledGames.Count} games found.";
        }
        catch (Exception ex)
        {
            Logger.Error("GamesViewModel scan error", ex);
            ScanStatusText = isRussian ? $"Ошибка: {ex.Message}" : $"Error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }
}
