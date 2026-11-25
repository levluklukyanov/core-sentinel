using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CoreSpectre.Services;

namespace CoreSpectre.Views;

public partial class OverviewView : UserControl
{
    public event Action? OnCpuClicked;
    public event Action? OnRamClicked;
    public event Action? OnDiskCClicked;
    public event Action? OnDiskDClicked;
    public event Action<FrameworkElement>? OnDiskDropdownClicked;
    public event Action? OnQuickPurgeClicked;
    public event Action? OnToggleHudClicked;
    public event Action? OnCleanJunkClicked;

    public OverviewView()
    {
        InitializeComponent();
    }

    public void ApplyLanguage(string language)
    {
        bool isEn = language == "EN";
        DashHeadingTitle.Text = LocalizationService.Get("Dash.Title");
        DashHeadingSub.Text = LocalizationService.Get("Dash.Subtitle");
        DashCardCpuTitle.Text = LocalizationService.Get("Dash.CardCpu");
        DashCardRamTitle.Text = LocalizationService.Get("Dash.CardRam");
        DashEnvCardTitle.Text = LocalizationService.Get("Dash.CardEnv");
        DashGameStatusTitle.Text = LocalizationService.Get("Dash.GamePriority");
        DashHealthStatusTitle.Text = LocalizationService.Get("Dash.HealthStatus");
        DashQuickActionsTitle.Text = LocalizationService.Get("Dash.QuickActions");
        DashQuickPurgeRamText.Text = LocalizationService.Get("Dash.FlushRam");
        DashToggleHudText.Text = LocalizationService.Get("Dash.ToggleHud");
        DashCleanJunkText.Text = LocalizationService.Get("Dash.CleanJunk");

        // Tooltips
        QuickCpuMetricToolTip(isEn);
    }

    private void QuickCpuMetricToolTip(bool isEn)
    {
        BtnCpuAction.ToolTip = isEn ? "Switch to CPU Optimization tab" : "Перейти на вкладку Оптимизации ЦП";
        MetricCpuAction.ToolTip = isEn ? "Switch to CPU Optimization tab" : "Перейти на вкладку Оптимизации ЦП";
        BtnRamAction.ToolTip = isEn ? "Switch to Memory Purge tab" : "Перейти на вкладку Очистки ОЗУ";
        MetricRamAction.ToolTip = isEn ? "Switch to Memory Purge tab" : "Перейти на вкладку Очистки ОЗУ";
        BtnDiskAction.ToolTip = isEn ? "Open drive in Windows Explorer" : "Открыть накопитель в Проводнике";
        MetricDiskAction.ToolTip = isEn ? "Open drive in Windows Explorer" : "Открыть накопитель в Проводнике";
        DashDiskDRow.ToolTip = isEn ? "Click to open data drive in Windows Explorer" : "Нажмите, чтобы открыть диск данных в Проводнике";
    }

    public void UpdateMetrics(
        double cpuPercent,
        string cpuName,
        double ramPercent,
        double ramUsedGb,
        double ramTotalGb,
        string watchedDrive,
        double diskFreeGb,
        double diskTotalGb,
        string? diskDName,
        double diskDFreeGb,
        double diskDTotalGb,
        string gameBadge,
        string gameDetail,
        string healthBadge,
        string recommendation)
    {
        bool isRu = LocalizationService.IsRussian;

        // CPU
        DashCpuVal.Text = $"{cpuPercent:0}%";
        DashCpuProgress.Value = Math.Clamp(cpuPercent, 0, 100);
        DashCpuSub.Text = string.IsNullOrWhiteSpace(cpuName) ? (isRu ? "Определение процессора..." : "Detecting processor...") : cpuName;

        // RAM
        DashRamVal.Text = $"{ramPercent:0}%";
        DashRamProgress.Value = Math.Clamp(ramPercent, 0, 100);
        if (ramTotalGb > 0)
        {
            DashRamSub.Text = isRu
                ? $"Исп: {ramUsedGb:0.0} / {ramTotalGb:0.0} ГБ"
                : $"Used: {ramUsedGb:0.0} / {ramTotalGb:0.0} GB";
        }
        else
        {
            DashRamVal.Text = "N/A";
            DashRamSub.Text = isRu ? "Данные ОЗУ недоступны" : "Memory data unavailable";
        }

        // Primary Disk
        DashDiskCTitle.Text = isRu
            ? $"НАКОПИТЕЛЬ ({watchedDrive}:)"
            : $"STORAGE ({watchedDrive}:)";
        DashDiskCVal.Text = isRu ? $"{diskFreeGb:0.0} ГБ" : $"{diskFreeGb:0.0} GB";
        double diskUsedPercent = diskTotalGb > 0 ? ((diskTotalGb - diskFreeGb) / diskTotalGb) * 100.0 : 0;
        DashDiskCProgress.Value = Math.Clamp(diskUsedPercent, 0, 100);
        DashDiskCSub.Text = isRu
            ? $"Свободно {diskFreeGb:0.0} из {diskTotalGb:0.0} ГБ"
            : $"Free {diskFreeGb:0.0} of {diskTotalGb:0.0} GB";

        // Secondary Disk D
        if (!string.IsNullOrEmpty(diskDName))
        {
            DashDiskDRow.Visibility = Visibility.Visible;
            DashDiskDTitle.Text = isRu ? $"Диск данных ({diskDName}:)" : $"Data Drive ({diskDName}:)";
            DashDiskDText.Text = isRu
                ? $"Свободно: {diskDFreeGb:0.0} ГБ из {diskDTotalGb:0.0} ГБ"
                : $"Free: {diskDFreeGb:0.0} GB of {diskDTotalGb:0.0} GB";
            DashDiskDBadge.Text = isRu ? $"Диск {diskDName}:" : $"Drive {diskDName}:";
        }
        else
        {
            DashDiskDRow.Visibility = Visibility.Collapsed;
        }

        // Game Status
        DashGameBadge.Text = gameBadge;
        DashGameStatusDetail.Text = gameDetail;

        // Health Recommendation
        DashHealthBadge.Text = healthBadge;
        DashRecommendation.Text = recommendation;
    }

    private void CpuMetric_Click(object sender, MouseButtonEventArgs e) => OnCpuClicked?.Invoke();
    private void RamMetric_Click(object sender, MouseButtonEventArgs e) => OnRamClicked?.Invoke();
    private void DiskCMetric_Click(object sender, MouseButtonEventArgs e) => OnDiskCClicked?.Invoke();
    private void DiskDMetric_Click(object sender, MouseButtonEventArgs e) => OnDiskDClicked?.Invoke();
    private void BtnDiskDropdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe) OnDiskDropdownClicked?.Invoke(fe);
    }
    private void BtnQuickPurge_Click(object sender, RoutedEventArgs e) => OnQuickPurgeClicked?.Invoke();
    private void BtnToggleHud_Click(object sender, RoutedEventArgs e) => OnToggleHudClicked?.Invoke();
    private void BtnCleanJunk_Click(object sender, RoutedEventArgs e) => OnCleanJunkClicked?.Invoke();
}
