using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CoreSentinel.Services;

namespace CoreSentinel.Views;

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
        BtnCpuAction.ToolTip = isEn ? "Open Windows Task Manager" : "Открыть Диспетчер задач Windows";
        MetricCpuAction.ToolTip = isEn ? "Open Windows Task Manager" : "Открыть Диспетчер задач Windows";
        BtnRamAction.ToolTip = isEn ? "Open Process Manager (sorted by RAM)" : "Открыть Диспетчер процессов (сортировка по ОЗУ)";
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
        string gameBadge,
        string gameDetail,
        string healthBadge,
        string recommendation)
    {
        // CPU
        DashCpuVal.Text = $"{cpuPercent:0}%";
        DashCpuProgress.Value = Math.Clamp(cpuPercent, 0, 100);
        DashCpuSub.Text = string.IsNullOrWhiteSpace(cpuName) ? "Detecting processor..." : cpuName;

        // RAM
        DashRamVal.Text = $"{ramPercent:0}%";
        DashRamProgress.Value = Math.Clamp(ramPercent, 0, 100);
        DashRamSub.Text = LocalizationService.IsRussian
            ? $"Исп: {ramUsedGb:0.0} / {ramTotalGb:0.0} ГБ"
            : $"Used: {ramUsedGb:0.0} / {ramTotalGb:0.0} GB";

        // Primary Disk
        DashDiskCTitle.Text = LocalizationService.IsRussian
            ? $"ХРАНИЛИЩЕ ({watchedDrive}:)"
            : $"STORAGE ({watchedDrive}:)";
        DashDiskCVal.Text = $"{diskFreeGb:0.0} GB";
        double diskUsedPercent = diskTotalGb > 0 ? ((diskTotalGb - diskFreeGb) / diskTotalGb) * 100.0 : 0;
        DashDiskCProgress.Value = Math.Clamp(diskUsedPercent, 0, 100);
        DashDiskCSub.Text = LocalizationService.IsRussian
            ? $"Свободно {diskFreeGb:0.0} из {diskTotalGb:0.0} ГБ"
            : $"Free {diskFreeGb:0.0} of {diskTotalGb:0.0} GB";

        // Secondary Disk D
        if (!string.IsNullOrEmpty(diskDName))
        {
            DashDiskDRow.Visibility = Visibility.Visible;
            DashDiskDTitle.Text = LocalizationService.IsRussian ? $"Диск данных ({diskDName}:)" : $"Data Drive ({diskDName}:)";
            DashDiskDText.Text = LocalizationService.IsRussian ? $"Свободно: {diskDFreeGb:0.0} ГБ" : $"Free: {diskDFreeGb:0.0} GB";
            DashDiskDBadge.Text = $"{diskDName}:";
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
