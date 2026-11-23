using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreSentinel.Services;

namespace CoreSentinel.ViewModels;

public partial class PurgeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isCpuMode;

    [ObservableProperty]
    private string _title = "Memory Purge";

    [ObservableProperty]
    private string _buttonText = "Purge Working Set";

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> OperationLogs { get; } = new();

    public PurgeViewModel(bool isCpuMode = false)
    {
        IsCpuMode = isCpuMode;
        UpdateLabels("EN");
    }

    public void UpdateLabels(string language)
    {
        bool isEn = language == "EN";
        if (IsCpuMode)
        {
            Title = isEn ? "CPU Optimization" : "Оптимизация ЦП";
            ButtonText = isEn ? "Optimize CPU Load" : "Оптимизировать ЦП";
        }
        else
        {
            Title = isEn ? "Memory Purge" : "Очистка ОЗУ";
            ButtonText = isEn ? "Purge Working Set" : "Очистить рабочие наборы";
        }
    }

    public void Log(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        OperationLogs.Add($"[{timestamp}] {message}");
    }

    [RelayCommand]
    public void ClearLogs()
    {
        OperationLogs.Clear();
    }
}
