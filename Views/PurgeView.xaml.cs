using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CoreSpectre.Services;

namespace CoreSpectre.Views;

public partial class PurgeView : UserControl
{
    private bool _isCpuPurgeMode = false;

    public event Action? OnPurgeRequested;
    public event Action? OnSwitchPurgeModeRequested;
    public event Action? OnOpenTaskManagerRequested;

    public bool IsCpuPurgeMode => _isCpuPurgeMode;

    public PurgeView()
    {
        InitializeComponent();
    }

    public void SetPurgeMode(bool isCpuMode)
    {
        _isCpuPurgeMode = isCpuMode;
        UpdateVisuals();
    }

    public void UpdateVisuals()
    {
        bool isEn = LocalizationService.IsEnglish;

        if (_isCpuPurgeMode)
        {
            RamTabHeading.Text = isEn ? "CPU Load Optimization" : "Оптимизация нагрузки ЦП";
            RamTabSub.Text = isEn ? "Normalizes background worker thread priority and throttles idle utilities" : "Нормализует приоритеты фоновых потоков и снижает нагрузку фоновых утилит";
            RamBannerTitle.Text = isEn ? "Background Thread Normalization" : "Нормализация фоновых потоков";
            RamBannerSub.Text = isEn ? "Throttles background non-critical services to BelowNormal priority to free CPU cycles for foreground apps." : "Понижает приоритет фоновых некритичных служб, высвобождая ресурсы процессора для активных окон.";
            BtnFullPurgeText.Text = isEn ? "Optimize CPU Now" : "Оптимизировать ЦП";
            RamDetailHeader.Text = isEn ? "PROCESSOR SPECIFICATION" : "СПЕЦИФИКАЦИЯ ПРОЦЕССОРА";
            RamTotalLabel.Text = isEn ? "Processor Model:" : "Модель процессора:";
            RamUsedLabel.Text = isEn ? "Cores / Threads:" : "Ядра / Потоки:";
            RamAvailLabel.Text = isEn ? "Current CPU Load:" : "Текущая нагрузка ЦП:";
            RamLogTitle.Text = isEn ? "CPU OPTIMIZATION LOG" : "ЖУРНАЛ ОПТИМИЗАЦИИ ЦП";

            RamHeroIcon.Data = (Geometry)FindResource("IconCpu");
            BtnFullPurgeIcon.Data = (Geometry)FindResource("IconLightning");
        }
        else
        {
            RamTabHeading.Text = isEn ? "Physical Memory Optimization" : "Оптимизация оперативной памяти";
            RamTabSub.Text = isEn ? "Working set trimming: signals OS to trim memory pages (temporary effect)" : "Сброс рабочих наборов: кратковременно освобождает физическую память";
            RamBannerTitle.Text = isEn ? "Working Set Memory Trimming" : "Сброс рабочих наборов памяти";
            RamBannerSub.Text = isEn ? "Safely returns unused pages from background processes to physical memory, preventing pagefile inflation." : "Очищает рабочие наборы процессов через EmptyWorkingSet, кратковременно высвобождая ОЗУ.";
            BtnFullPurgeText.Text = isEn ? "Purge Memory Now" : "Очистить память";
            RamDetailHeader.Text = isEn ? "MEMORY SPECIFICATION" : "СПЕЦИФИКАЦИЯ ОЗУ";
            RamTotalLabel.Text = isEn ? "Total RAM:" : "Всего ОЗУ:";
            RamUsedLabel.Text = isEn ? "In Use:" : "Занято:";
            RamAvailLabel.Text = isEn ? "Available:" : "Доступно:";
            RamLogTitle.Text = isEn ? "PURGE ACTIVITY LOG" : "ЖУРНАЛ ОЧИСТКИ ПАМЯТИ";

            RamHeroIcon.Data = (Geometry)FindResource("IconRam");
            BtnFullPurgeIcon.Data = (Geometry)FindResource("IconBroom");
        }

        BtnRamOpenInspectorText.Text = isEn ? "Open Task Manager" : "Открыть диспетчер задач";
    }

    public void UpdateMetrics(double ramUsedGb, double ramTotalGb, double cpuPercent, string cpuName, int physicalCores, int logicalCores)
    {
        bool isEn = LocalizationService.IsEnglish;
        if (_isCpuPurgeMode)
        {
            RamDetailTotal.Text = string.IsNullOrWhiteSpace(cpuName) ? (isEn ? "Detecting..." : "Определение...") : cpuName;
            RamDetailUsed.Text = physicalCores > 0 ? $"{physicalCores} / {logicalCores}" : $"{logicalCores}";
            RamDetailAvail.Text = $"{cpuPercent:0.#}%";
        }
        else
        {
            if (ramTotalGb > 0)
            {
                string unit = isEn ? "GB" : "ГБ";
                double availGb = Math.Max(0, ramTotalGb - ramUsedGb);
                RamDetailTotal.Text = $"{ramTotalGb:0.00} {unit}";
                RamDetailUsed.Text = $"{ramUsedGb:0.00} {unit}";
                RamDetailAvail.Text = $"{availGb:0.00} {unit}";
            }
            else
            {
                RamDetailTotal.Text = "N/A";
                RamDetailUsed.Text = "N/A";
                RamDetailAvail.Text = "N/A";
            }
        }
    }

    public void Log(string line)
    {
        RamLogTextBox.AppendText(line + Environment.NewLine);
        RamLogTextBox.ScrollToEnd();
    }

    private void BtnFullPurge_Click(object sender, RoutedEventArgs e) => OnPurgeRequested?.Invoke();
    private void BtnSwitchPurgeMode_Click(object sender, RoutedEventArgs e) => OnSwitchPurgeModeRequested?.Invoke();
    private void BtnOpenTaskManager_Click(object sender, RoutedEventArgs e) => OnOpenTaskManagerRequested?.Invoke();
}
