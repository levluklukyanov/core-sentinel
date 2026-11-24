using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CoreSentinel.Services;

namespace CoreSentinel.Views;

public partial class SettingsView : UserControl
{
    private readonly Dictionary<Expander, bool> _accordionCollapsing = new();
    private bool _sysInfoLoaded = false;
    private List<StartupEntry> _startupEntries = new();
    private List<NetworkAdapterInfo> _networkAdapters = new();

    // Cached hardware specs
    private List<CpuInfo>? _cachedCpu;
    private List<RamSlot>? _cachedRam;
    private List<DiskDetail>? _cachedDisks;
    private List<GpuInfo>? _cachedGpus;
    private MotherboardInfo? _cachedMb;
    private BiosInfo? _cachedBios;
    private OsInfo? _cachedOs;
    private List<NetworkAdapterInfo>? _cachedNet;
    private string _cachedUptime = "";

    public event Action<string>? OnLanguageChanged;
    public event Action<bool>? OnThemeChanged;
    public event Action<bool>? OnTimeFormatChanged;
    public event Action<double>? OnHudOpacityChanged;
    public event Action? OnResetSettingsRequested;
    public event Action? OnPurgeRamRequested;
    public event Action<FrameworkElement>? OnIntervalSelectorRequested;
    public event Action<FrameworkElement>? OnThresholdSelectorRequested;
    public event Action? OnToggleTabEditRequested;
    public event Action? OnOpenTabVaultRequested;
    public event Action<string>? OnStatusChanged;

    public SettingsView()
    {
        InitializeComponent();
    }

    public void ApplyLanguage(string language)
    {
        bool isEn = language == "EN";
        SettingsHeadingTitle.Text = LocalizationService.Get("Settings.Title");
        SettingsHeadingSub.Text = LocalizationService.Get("Settings.Subtitle");
        TxtResetSettings.Text = LocalizationService.Get("Settings.ResetBtn");

        // Accordion 1
        ExpCustom.Header = LocalizationService.Get("Settings.Customization");
        LangSettingTitle.Text = LocalizationService.Get("Settings.Language");
        LangSettingDesc.Text = isEn ? "Default is English. Switchable instantly." : "По умолчанию — English. Переключается на лету.";
        ThemeSettingTitle.Text = LocalizationService.Get("Settings.Theme");
        ThemeSettingDesc.Text = isEn ? "Dark or light theme for the entire workspace" : "Тёмная или светлая тема для всего интерфейса";
        ThemeDarkText.Text = LocalizationService.Get("Settings.ThemeDark");
        ThemeLightText.Text = LocalizationService.Get("Settings.ThemeLight");
        TimeFormatSettingTitle.Text = LocalizationService.Get("Settings.TimeFormat");
        TimeFormatSettingDesc.Text = isEn ? "12-hour (AM/PM) or 24-hour clock display" : "12-часовой (AM/PM) или 24-часовой формат времени";
        BtnTime12hText.Text = isEn ? "12-hour" : "12-часовой";
        BtnTime24hText.Text = isEn ? "24-hour" : "24-часовой";
        HudOpacityTitle.Text = LocalizationService.Get("Settings.HudOpacity");
        TabManagerTitle.Text = LocalizationService.Get("Settings.Tabs");
        TabManagerDesc.Text = isEn ? "Manage navigation tabs, order and removal" : "Управление списком вкладок навигации, удаление и сортировка";
        TabVaultTitle.Text = LocalizationService.Get("Settings.TabStorage");
        TabVaultDesc.Text = isEn ? "Inactive tabs not added to navigation" : "Вкладки, не добавленные в навигацию. Нажмите Открыть для добавления";

        // Accordion 2
        ExpMonitor.Header = isEn ? "Monitoring & Tools" : "Мониторинг и инструменты";
        TelemetrySectionTitle.Text = isEn ? "TELEMETRY & SAMPLING" : "ТЕЛЕМЕТРИЯ И ОПРОС";
        IntervalTitle.Text = isEn ? "Metrics sampling interval:" : "Интервал опроса телеметрии:";
        IntervalDesc.Text = isEn ? "Determines update frequency for CPU, RAM and storage metrics" : "Определяет частоту обновления метрик ЦП, ОЗУ и дисков";
        AutoRamTitle.Text = isEn ? "Auto-purge on memory spike (> 85%)" : "Авто-очистка при пиковой нагрузке (> 85%)";
        AutoRamDesc.Text = isEn ? "Background working set trim when RAM is near capacity" : "Фоновая очистка рабочих наборов при критическом заполнении памяти";
        AutoGamePriorityTitle.Text = isEn ? "Automatic Game Priority" : "Автоматический игровой приоритет";
        AutoGamePriorityDesc.Text = isEn ? "Auto-boost javaw, cs2, dota2 upon launch" : "Автоматически повышает приоритет javaw, cs2, dota2 при запуске";
        ManualRamTitle.Text = isEn ? "Manual memory optimization:" : "Ручная оптимизация памяти:";
        BtnSettingsPurgeRamText.Text = isEn ? "Purge RAM" : "Сбросить память";
        StorageSectionTitle.Text = isEn ? "STORAGE & THRESHOLDS" : "ХРАНИЛИЩЕ И ПОРОГИ";
        DiskThresholdTitle.Text = isEn ? "Drive warning threshold:" : "Порог предупреждения диска:";
        DiskThresholdDesc.Text = isEn ? "Warning status triggers when free space drops below threshold" : "Предупреждение срабатывает при остатке места меньше заданного";
        AlertSectionTitle.Text = isEn ? "ALERT THRESHOLDS" : "ПОРОГИ ОПОВЕЩЕНИЙ";
        CpuAlertTitle.Text = isEn ? "CPU Alert Threshold (%):" : "Порог нагрузки ЦП (%):";
        RamAlertTitle.Text = isEn ? "RAM Alert Threshold (%):" : "Порог нагрузки ОЗУ (%):";
        StartupSectionTitle.Text = isEn ? "STARTUP MANAGER" : "ДИСПЕТЧЕР АВТОЗАГРУЗКИ";
        BtnRefreshStartupText.Text = isEn ? "Refresh" : "Обновить";
        NetworkSectionTitle.Text = isEn ? "NETWORK ADAPTERS" : "СЕТЕВЫЕ АДАПТЕРЫ";

        // Accordion 3
        ExpSysInfo.Header = isEn ? "System Specifications" : "Характеристики системы";
        if (_sysInfoLoaded)
        {
            PopulateSpecsUI();
        }
        else
        {
            SysInfoLoadingText.Text = isEn ? "Scanning system hardware specifications..." : "Поиск характеристик системы...";
        }
    }

    public void InitSettings(UserSettingsModel settings, bool isLightTheme)
    {
        bool isRu = settings.Language == "RU";
        BtnLangRu.Style = (Style)FindResource(isRu ? "BtnPrimaryWhite" : "BtnPill");
        BtnLangEn.Style = (Style)FindResource(!isRu ? "BtnPrimaryWhite" : "BtnPill");

        BtnThemeDark.Style = (Style)FindResource(!isLightTheme ? "BtnPrimaryWhite" : "BtnPill");
        BtnThemeLight.Style = (Style)FindResource(isLightTheme ? "BtnPrimaryWhite" : "BtnPill");

        BtnTime12h.Style = (Style)FindResource(settings.Use12HourTime ? "BtnPrimaryWhite" : "BtnPill");
        BtnTime24h.Style = (Style)FindResource(!settings.Use12HourTime ? "BtnPrimaryWhite" : "BtnPill");

        SliderHudOpacity.Value = settings.HudOpacity;
        TxtHudOpacityVal.Text = $"{settings.HudOpacity:0}%";

        SliderCpuAlert.Value = settings.CpuAlertThreshold;
        TxtCpuAlertVal.Text = $"{settings.CpuAlertThreshold:0}%";

        SliderRamAlert.Value = settings.RamAlertThreshold;
        TxtRamAlertVal.Text = $"{settings.RamAlertThreshold:0}%";

        TxtIntervalCurrent.Text = LocalizationService.IsRussian
            ? $"{settings.TelemetryInterval:0.#} сек"
            : $"{settings.TelemetryInterval:0.#}s";

        TxtThresholdCurrent.Text = LocalizationService.IsRussian
            ? $"{settings.DiskWarningThresholdGb:0.#} ГБ"
            : $"{settings.DiskWarningThresholdGb:0.#} GB";

        UpdateAutoRamVisual(settings.AutoRamPurge);
        UpdateAutoGamePriorityVisual(settings.AutoGamePriority);

        RefreshStartupList();
        RefreshNetworkList();
    }

    public void UpdateAutoRamVisual(bool enabled)
    {
        bool isEn = LocalizationService.IsEnglish;
        TxtAutoRamStatus.Text = enabled ? (isEn ? "Enabled" : "Включено") : (isEn ? "Disabled" : "Выключено");
        BtnToggleAutoRam.Style = (Style)FindResource(enabled ? "BtnPrimaryWhite" : "BtnPill");
    }

    public void UpdateAutoGamePriorityVisual(bool enabled)
    {
        bool isEn = LocalizationService.IsEnglish;
        TxtAutoGamePriorityStatus.Text = enabled ? (isEn ? "Enabled" : "Включено") : (isEn ? "Disabled" : "Выключено");
        BtnToggleAutoGamePriority.Style = (Style)FindResource(enabled ? "BtnPrimaryWhite" : "BtnPill");
    }

    private void Expander_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Expander exp && exp.Content is FrameworkElement content)
        {
            var hit = e.OriginalSource as DependencyObject;
            while (hit != null && hit != exp)
            {
                if (hit is System.Windows.Controls.Primitives.ToggleButton) break;
                hit = VisualTreeHelper.GetParent(hit);
            }
            if (hit == null || hit == exp) return;

            e.Handled = true;
            if (exp.IsExpanded)
            {
                AnimateCollapse(exp, content);
            }
            else
            {
                foreach (var other in new[] { ExpCustom, ExpMonitor, ExpSysInfo })
                {
                    if (other != exp && other.IsExpanded && other.Content is FrameworkElement otherContent)
                    {
                        AnimateCollapse(other, otherContent);
                    }
                }
                exp.IsExpanded = true;
                AnimateExpand(exp, content);
            }
        }
    }

    private void Expander_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander exp && exp.Content is FrameworkElement content)
        {
            if (_accordionCollapsing.TryGetValue(exp, out bool isCol) && isCol) return;
            AnimateExpand(exp, content);
        }
    }

    private async void ExpSysInfo_Expanded(object sender, RoutedEventArgs e)
    {
        Expander_Expanded(sender, e);
        if (!_sysInfoLoaded)
        {
            await LoadSysInfoAsync();
        }
    }

    private void AnimateExpand(Expander exp, FrameworkElement content)
    {
        _accordionCollapsing[exp] = false;
        content.BeginAnimation(FrameworkElement.HeightProperty, null);
        content.BeginAnimation(UIElement.OpacityProperty, null);

        content.Height = double.NaN;
        double availWidth = exp.ActualWidth > 40 ? exp.ActualWidth - 40 : 800;
        content.Measure(new Size(availWidth, double.PositiveInfinity));
        double target = Math.Max(32, content.DesiredSize.Height);

        var animH = new DoubleAnimation(0, target, new Duration(TimeSpan.FromMilliseconds(220)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var animO = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200)));

        animH.Completed += (s, ev) =>
        {
            content.BeginAnimation(FrameworkElement.HeightProperty, null);
            content.Height = double.NaN;
        };

        content.BeginAnimation(FrameworkElement.HeightProperty, animH);
        content.BeginAnimation(UIElement.OpacityProperty, animO);
    }

    private void AnimateCollapse(Expander exp, FrameworkElement content)
    {
        _accordionCollapsing[exp] = true;
        content.BeginAnimation(FrameworkElement.HeightProperty, null);
        content.BeginAnimation(UIElement.OpacityProperty, null);

        double startH = content.ActualHeight > 0 ? content.ActualHeight : 100;
        var animH = new DoubleAnimation(startH, 0, new Duration(TimeSpan.FromMilliseconds(180)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var animO = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(120)));

        animH.Completed += (s, ev) =>
        {
            exp.IsExpanded = false;
            _accordionCollapsing[exp] = false;
            content.BeginAnimation(FrameworkElement.HeightProperty, null);
            content.Height = double.NaN;
            content.Opacity = 1;
        };

        content.BeginAnimation(FrameworkElement.HeightProperty, animH);
        content.BeginAnimation(UIElement.OpacityProperty, animO);
    }

    private void SettingsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateStickyHeader();
    private void SettingsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateStickyHeader();

    private void UpdateStickyHeader()
    {
        if (StickyAccordionBar == null) return;

        Expander? activeExp = null;
        if (ExpCustom?.IsExpanded == true) activeExp = ExpCustom;
        else if (ExpMonitor?.IsExpanded == true) activeExp = ExpMonitor;
        else if (ExpSysInfo?.IsExpanded == true) activeExp = ExpSysInfo;

        if (activeExp == null)
        {
            StickyAccordionBar.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            Point p = activeExp.TransformToAncestor(SettingsScrollViewer).Transform(new Point(0, 0));
            if (p.Y < -30)
            {
                StickyAccordionBar.Visibility = Visibility.Visible;
                StickyAccordionTitle.Text = activeExp.Header?.ToString() ?? "Settings";
            }
            else
            {
                StickyAccordionBar.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            StickyAccordionBar.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnCollapseSticky_Click(object sender, MouseButtonEventArgs e)
    {
        if (ExpCustom?.IsExpanded == true && ExpCustom.Content is FrameworkElement c1)
        {
            AnimateCollapse(ExpCustom, c1);
            SettingsScrollViewer?.ScrollToTop();
        }
        else if (ExpMonitor?.IsExpanded == true && ExpMonitor.Content is FrameworkElement c2)
        {
            AnimateCollapse(ExpMonitor, c2);
            SettingsScrollViewer?.ScrollToTop();
        }
        else if (ExpSysInfo?.IsExpanded == true && ExpSysInfo.Content is FrameworkElement c3)
        {
            AnimateCollapse(ExpSysInfo, c3);
            SettingsScrollViewer?.ScrollToTop();
        }
    }

    private void StickyAccordionBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SettingsScrollViewer?.ScrollToVerticalOffset(SettingsScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    // Hardware Specs
    private async Task LoadSysInfoAsync()
    {
        try
        {
            SysInfoLoadingText.Visibility = Visibility.Visible;
            bool isEn = LocalizationService.IsEnglish;

            await Task.Run(() =>
            {
                _cachedCpu = SystemInfoService.GetCpuDetails(isEn);
                _cachedRam = SystemInfoService.GetRamDetails(isEn);
                _cachedDisks = SystemInfoService.GetAllDiskDetails(isEn);
                _cachedGpus = SystemInfoService.GetGpus();
                _cachedMb = SystemInfoService.GetMotherboard();
                _cachedBios = SystemInfoService.GetBios();
                _cachedOs = SystemInfoService.GetOsInfo();
                _cachedNet = SystemInfoService.GetNetworkAdapters(isEn);
                _cachedUptime = SystemInfoService.GetUptime(isEn);
            });

            _sysInfoLoaded = true;
            PopulateSpecsUI();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load sysinfo", ex);
            SysInfoLoadingText.Text = $"Error: {ex.Message}";
        }
    }

    private void PopulateSpecsUI()
    {
        SysInfoPanel.Children.Clear();
        bool isRu = LocalizationService.IsRussian;

        Border CreateSection(string title, IEnumerable<KeyValuePair<string, string>> items)
        {
            var border = new Border
            {
                Background = (Brush)FindResource("BrushBgInner"),
                BorderBrush = (Brush)FindResource("BrushBorderCard"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("BrushTextMuted"),
                Margin = new Thickness(0, 0, 0, 12)
            });

            bool first = true;
            foreach (var kv in items)
            {
                if (!first)
                {
                    sp.Children.Add(new Separator
                    {
                        Background = (Brush)FindResource("BrushBorderSubtle"),
                        Margin = new Thickness(0, 8, 0, 8)
                    });
                }
                first = false;

                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var lbl = new TextBlock
                {
                    Text = kv.Key,
                    FontSize = 11.5,
                    Foreground = (Brush)FindResource("BrushTextSecondary"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lbl, 0);

                var val = new TextBlock
                {
                    Text = kv.Value,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("BrushTextPrimary"),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(val, 1);

                g.Children.Add(lbl);
                g.Children.Add(val);
                sp.Children.Add(g);
            }

            border.Child = sp;
            return border;
        }

        // OS
        if (_cachedOs != null)
        {
            SysInfoPanel.Children.Add(CreateSection(isRu ? "ОПЕРАЦИОННАЯ СИСТЕМА" : "OPERATING SYSTEM", new[]
            {
                new KeyValuePair<string, string>(isRu ? "ОС:" : "OS:", _cachedOs.Caption),
                new KeyValuePair<string, string>(isRu ? "Версия / Сборка:" : "Version / Build:", $"{_cachedOs.Version} ({_cachedOs.BuildNumber})"),
                new KeyValuePair<string, string>(isRu ? "Архитектура:" : "Architecture:", _cachedOs.Architecture),
                new KeyValuePair<string, string>(isRu ? "Имя компьютера:" : "Computer Name:", _cachedOs.ComputerName),
                new KeyValuePair<string, string>(isRu ? "Время работы:" : "Uptime:", _cachedUptime)
            }));
        }

        // CPU
        if (_cachedCpu != null && _cachedCpu.Count > 0)
        {
            var cpu = _cachedCpu[0];
            SysInfoPanel.Children.Add(CreateSection(isRu ? "ПРОЦЕССОР (CPU)" : "PROCESSOR (CPU)", new[]
            {
                new KeyValuePair<string, string>(isRu ? "Модель:" : "Model:", cpu.Name),
                new KeyValuePair<string, string>(isRu ? "Ядра / Потоки:" : "Cores / Threads:", $"{cpu.Cores} / {cpu.LogicalProcessors}"),
                new KeyValuePair<string, string>(isRu ? "Базовая частота:" : "Base Clock:", cpu.MaxClockMhz),
                new KeyValuePair<string, string>(isRu ? "Сокет:" : "Socket:", cpu.SocketDesignation),
                new KeyValuePair<string, string>(isRu ? "Архитектура:" : "Architecture:", cpu.Architecture)
            }));
        }

        // Motherboard & BIOS
        if (_cachedMb != null && _cachedBios != null)
        {
            SysInfoPanel.Children.Add(CreateSection(isRu ? "МАТЕРИНСКАЯ ПЛАТА И BIOS" : "MOTHERBOARD & BIOS", new[]
            {
                new KeyValuePair<string, string>(isRu ? "Плата:" : "Motherboard:", $"{_cachedMb.Manufacturer} {_cachedMb.Product}"),
                new KeyValuePair<string, string>(isRu ? "BIOS:" : "BIOS:", $"{_cachedBios.Manufacturer} v{_cachedBios.Version}"),
                new KeyValuePair<string, string>(isRu ? "Дата релиза BIOS:" : "BIOS Date:", _cachedBios.ReleaseDate)
            }));
        }

        // RAM Slots
        if (_cachedRam != null && _cachedRam.Count > 0)
        {
            var list = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < _cachedRam.Count; i++)
            {
                var slot = _cachedRam[i];
                list.Add(new KeyValuePair<string, string>(
                    slot.BankLabel,
                    $"{slot.FormattedSize} {slot.MemoryType} @ {slot.Speed} ({slot.Manufacturer})"
                ));
            }
            SysInfoPanel.Children.Add(CreateSection(isRu ? "МОДУЛИ ПАМЯТИ (RAM)" : "MEMORY MODULES (RAM)", list));
        }

        // GPU
        if (_cachedGpus != null && _cachedGpus.Count > 0)
        {
            var list = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < _cachedGpus.Count; i++)
            {
                var g = _cachedGpus[i];
                list.Add(new KeyValuePair<string, string>(
                    $"GPU {i + 1}:",
                    $"{g.Name} ({g.FormattedRam}, Драйвер: {g.DriverVersion})"
                ));
            }
            SysInfoPanel.Children.Add(CreateSection(isRu ? "ВИДЕОКАРТЫ (GPU)" : "GRAPHICS (GPU)", list));
        }

        // Storage
        if (_cachedDisks != null && _cachedDisks.Count > 0)
        {
            var list = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < _cachedDisks.Count; i++)
            {
                var d = _cachedDisks[i];
                list.Add(new KeyValuePair<string, string>(
                    $"Диск {d.DriveLetter}: [{d.Label}]",
                    $"{d.FormattedFree} свободно из {d.FormattedTotal} ({d.FileSystem}, {d.DriveType})"
                ));
            }
            SysInfoPanel.Children.Add(CreateSection(isRu ? "ФИЗИЧЕСКИЕ НАКОПИТЕЛИ" : "STORAGE DRIVES", list));
        }
    }

    // Startup Manager
    public void RefreshStartupList()
    {
        try
        {
            _startupEntries = StartupService.GetAllEntries();
            StartupList.ItemsSource = _startupEntries;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to enumerate startup items", ex);
        }
    }

    private void BtnStartupToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is StartupEntry entry)
        {
            bool isEn = LocalizationService.IsEnglish;
            var res = StartupService.SetEntryEnabled(entry, !entry.IsEnabled, isEn);
            if (res.Success)
            {
                RefreshStartupList();
            }
            else
            {
                OnStatusChanged?.Invoke(res.Message);
            }
        }
    }

    private void BtnStartupDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is StartupEntry entry)
        {
            bool isEn = LocalizationService.IsEnglish;
            var res = StartupService.DeleteEntry(entry, isEn);
            if (res.Success)
            {
                RefreshStartupList();
            }
            else
            {
                OnStatusChanged?.Invoke(res.Message);
            }
        }
    }

    private void BtnRefreshStartup_Click(object sender, RoutedEventArgs e) => RefreshStartupList();

    // Network
    public void RefreshNetworkList()
    {
        try
        {
            _networkAdapters = SystemInfoService.GetNetworkAdapters(LocalizationService.IsEnglish);
            NetworkList.ItemsSource = _networkAdapters;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to enumerate network adapters", ex);
        }
    }

    private async void BtnPingAll_Click(object sender, RoutedEventArgs e)
    {
        BtnPingAllText.Text = "...";
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", 2000);
            if (reply.Status == IPStatus.Success)
            {
                BtnPingAllText.Text = $"{reply.RoundtripTime} ms";
            }
            else
            {
                BtnPingAllText.Text = "Fail";
            }
        }
        catch
        {
            BtnPingAllText.Text = "Err";
        }
    }

    // Handlers
    private void BtnLang_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string lang)
        {
            OnLanguageChanged?.Invoke(lang);
        }
    }

    private void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string theme)
        {
            OnThemeChanged?.Invoke(theme == "Light");
        }
    }

    private void BtnTimeFormat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string mode)
        {
            OnTimeFormatChanged?.Invoke(mode == "12");
        }
    }

    private void SliderHudOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtHudOpacityVal != null) TxtHudOpacityVal.Text = $"{e.NewValue:0}%";
        OnHudOpacityChanged?.Invoke(e.NewValue);
    }

    private void SliderCpuAlert_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtCpuAlertVal != null) TxtCpuAlertVal.Text = $"{e.NewValue:0}%";
    }

    private void SliderRamAlert_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtRamAlertVal != null) TxtRamAlertVal.Text = $"{e.NewValue:0}%";
    }

    private void BtnIntervalSelector_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe) OnIntervalSelectorRequested?.Invoke(fe);
    }

    private void BtnThresholdSelector_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe) OnThresholdSelectorRequested?.Invoke(fe);
    }

    private void BtnToggleAutoRam_Click(object sender, RoutedEventArgs e)
    {
        var settings = SettingsPersistenceService.Load();
        settings.AutoRamPurge = !settings.AutoRamPurge;
        SettingsPersistenceService.Save(settings);
        UpdateAutoRamVisual(settings.AutoRamPurge);
    }

    private void BtnToggleAutoGamePriority_Click(object sender, RoutedEventArgs e)
    {
        var settings = SettingsPersistenceService.Load();
        settings.AutoGamePriority = !settings.AutoGamePriority;
        SettingsPersistenceService.Save(settings);
        UpdateAutoGamePriorityVisual(settings.AutoGamePriority);
    }

    private void BtnSettingsPurgeRam_Click(object sender, RoutedEventArgs e) => OnPurgeRamRequested?.Invoke();
    private void BtnResetSettings_Click(object sender, RoutedEventArgs e) => OnResetSettingsRequested?.Invoke();
    private void BtnToggleTabEdit_Click(object sender, RoutedEventArgs e) => OnToggleTabEditRequested?.Invoke();
    private void BtnOpenTabVault_Click(object sender, RoutedEventArgs e) => OnOpenTabVaultRequested?.Invoke();
}
