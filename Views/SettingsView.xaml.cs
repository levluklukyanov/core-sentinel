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
using CoreSpectre.Services;

namespace CoreSpectre.Views;

public record TabVaultEntry(string Key, string Title, string IconKey);

public partial class SettingsView : UserControl
{
    private readonly Dictionary<Expander, bool> _accordionCollapsing = new();
    private bool _sysInfoLoaded = false;
    private bool _isInitializing = false;
    private bool _isEditingTabs = false;
    private string _currentLanguage = "EN";
    private bool _isLightTheme = false;
    private bool _use12HourTime = true;
    private bool _autoRamEnabled = true;
    private bool _autoGamePriorityEnabled = true;
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
    public event Action<double>? OnCpuAlertChanged;
    public event Action<double>? OnRamAlertChanged;
    public event Action<bool>? OnAutoRamChanged;
    public event Action<bool>? OnAutoGamePriorityChanged;
    public event Action? OnResetSettingsRequested;
    public event Action<FrameworkElement>? OnIntervalSelectorRequested;
    public event Action<FrameworkElement>? OnThresholdSelectorRequested;
    public event Action? OnToggleTabEditRequested;
    public event Action? OnTabEditShouldDisable;
    public event Action? OnOpenTabVaultRequested;
    public event Action<string>? OnRestoreTabRequested;
    public event Action<string>? OnStatusChanged;

    public bool IsVaultOpen => VaultDrawerPanel.Visibility == Visibility.Visible;

    public SettingsView()
    {
        InitializeComponent();
        ExpCustom.Collapsed += (s, e) => OnTabEditShouldDisable?.Invoke();
    }

    public void ApplyLanguage(string language)
    {
        _currentLanguage = language == "RU" ? "RU" : "EN";
        bool isEn = _currentLanguage == "EN";
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
        TxtToggleTabEdit.Text = _isEditingTabs ? (isEn ? "Done" : "Готово") : (isEn ? "Edit" : "Редактировать");
        TabVaultTitle.Text = LocalizationService.Get("Settings.TabStorage");
        TabVaultDesc.Text = isEn ? "Inactive tabs not added to navigation" : "Вкладки, не добавленные в навигацию. Нажмите Открыть для добавления";
        TxtBtnOpenTabVault.Text = VaultDrawerPanel.Visibility == Visibility.Visible
            ? (isEn ? "Close" : "Закрыть")
            : (isEn ? "Open" : "Открыть");

        // Accordion 2
        ExpMonitor.Header = isEn ? "Monitoring & Tools" : "Мониторинг и инструменты";
        TelemetrySectionTitle.Text = isEn ? "TELEMETRY & SAMPLING" : "ТЕЛЕМЕТРИЯ И ОПРОС";
        IntervalTitle.Text = isEn ? "Metrics sampling interval:" : "Интервал опроса телеметрии:";
        IntervalDesc.Text = isEn ? "Determines update frequency for CPU, RAM and storage metrics" : "Определяет частоту обновления метрик ЦП, ОЗУ и дисков";
        AutoRamTitle.Text = isEn ? "Auto-purge on memory spike (> 85%)" : "Авто-очистка при пиковой нагрузке (> 85%)";
        AutoRamDesc.Text = isEn ? "Background working set trim when RAM is near capacity" : "Фоновая очистка рабочих наборов при критическом заполнении памяти";
        AutoGamePriorityTitle.Text = isEn ? "Automatic Game Priority" : "Автоматический игровой приоритет";
        AutoGamePriorityDesc.Text = isEn ? "Auto-boost javaw, cs2, dota2 upon launch" : "Автоматически повышает приоритет javaw, cs2, dota2 при запуске";
        StorageSectionTitle.Text = isEn ? "STORAGE & THRESHOLDS" : "ХРАНИЛИЩЕ И ПОРОГИ";
        DiskThresholdTitle.Text = isEn ? "Drive warning threshold:" : "Порог предупреждения диска:";
        DiskThresholdDesc.Text = isEn ? "Warning status triggers when free space drops below threshold" : "Предупреждение срабатывает при остатке места меньше заданного";
        AlertSectionTitle.Text = isEn ? "ALERT THRESHOLDS" : "ПОРОГИ ОПОВЕЩЕНИЙ";
        CpuAlertTitle.Text = isEn ? "CPU Alert Threshold (%):" : "Порог нагрузки ЦП (%):";
        RamAlertTitle.Text = isEn ? "RAM Alert Threshold (%):" : "Порог нагрузки ОЗУ (%):";
        StartupSectionTitle.Text = isEn ? "STARTUP MANAGER" : "ДИСПЕТЧЕР АВТОЗАГРУЗКИ";
        BtnRefreshStartupText.Text = isEn ? "Refresh" : "Обновить";
        NetworkSectionTitle.Text = isEn ? "NETWORK ADAPTERS" : "СЕТЕВЫЕ АДАПТЕРЫ";
        BtnPingAllText.Text = isEn ? "Ping (8.8.8.8)" : "Пинг (8.8.8.8)";

        UpdateToggleButtonStyles();
        RefreshStartupList();
        RefreshNetworkList();

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

        UpdateStickyHeader();
    }

    public void InitSettings(UserSettingsModel settings, bool isLightTheme)
    {
        _isInitializing = true;
        try
        {
            _currentLanguage = settings.Language == "RU" ? "RU" : "EN";
            _isLightTheme = isLightTheme;
            _use12HourTime = settings.Use12HourTime;
            _autoRamEnabled = settings.AutoRamPurge;
            _autoGamePriorityEnabled = settings.AutoGamePriority;

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

            UpdateToggleButtonStyles();
            RefreshStartupList();
            RefreshNetworkList();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    public void UpdateToggleButtonStyles()
    {
        var primaryStyle = (Style)FindResource("BtnPrimaryWhite");
        var pillStyle = (Style)FindResource("BtnPill");
        bool isRu = _currentLanguage == "RU";
        bool isEn = !isRu;

        BtnLangRu.Style = isRu ? primaryStyle : pillStyle;
        BtnLangEn.Style = !isRu ? primaryStyle : pillStyle;

        BtnThemeDark.Style = !_isLightTheme ? primaryStyle : pillStyle;
        BtnThemeLight.Style = _isLightTheme ? primaryStyle : pillStyle;

        BtnTime12h.Style = _use12HourTime ? primaryStyle : pillStyle;
        BtnTime24h.Style = !_use12HourTime ? primaryStyle : pillStyle;

        TxtAutoRamStatus.Text = _autoRamEnabled ? (isEn ? "Enabled" : "Включено") : (isEn ? "Disabled" : "Выключено");
        BtnToggleAutoRam.Style = _autoRamEnabled ? primaryStyle : pillStyle;

        TxtAutoGamePriorityStatus.Text = _autoGamePriorityEnabled ? (isEn ? "Enabled" : "Включено") : (isEn ? "Disabled" : "Выключено");
        BtnToggleAutoGamePriority.Style = _autoGamePriorityEnabled ? primaryStyle : pillStyle;

        BtnToggleTabEdit.Style = _isEditingTabs ? primaryStyle : pillStyle;
        TxtToggleTabEdit.Text = _isEditingTabs ? (isEn ? "Done" : "Готово") : (isEn ? "Edit" : "Редактировать");
    }

    public void RefreshThemeState(bool isLightTheme)
    {
        _isLightTheme = isLightTheme;
        UpdateToggleButtonStyles();
    }

    public void SetTabEditState(bool isEditing)
    {
        _isEditingTabs = isEditing;
        UpdateToggleButtonStyles();
    }

    public void SetVaultDrawerOpen(bool isOpen)
    {
        bool isEn = LocalizationService.IsEnglish;
        VaultDrawerPanel.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        TxtBtnOpenTabVault.Text = isOpen ? (isEn ? "Close" : "Закрыть") : (isEn ? "Open" : "Открыть");

        if (IconVaultChevron.RenderTransform is not RotateTransform)
        {
            IconVaultChevron.RenderTransform = new RotateTransform(0);
        }
        ((RotateTransform)IconVaultChevron.RenderTransform).Angle = isOpen ? 180 : 0;
    }

    public void RefreshVaultItems(IEnumerable<TabVaultEntry> vaultTabs)
    {
        bool isEn = LocalizationService.IsEnglish;
        VaultItemsContainer.Children.Clear();
        var list = vaultTabs.ToList();

        if (list.Count == 0)
        {
            TxtVaultEmpty.Visibility = Visibility.Visible;
            TxtVaultEmpty.Text = isEn
                ? "All available tabs are active in navigation."
                : "Все доступные вкладки активны в навигации.";
            return;
        }

        TxtVaultEmpty.Visibility = Visibility.Collapsed;

        foreach (var tab in list)
        {
            var rowBorder = new Border
            {
                Background = (Brush)FindResource("BrushBgBase"),
                BorderBrush = (Brush)FindResource("BrushBorderCard"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var btnAdd = new Button
            {
                Style = (Style)FindResource("BtnPill"),
                Tag = tab.Key,
                Padding = new Thickness(7, 5, 7, 5),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = isEn ? "Add back to navigation (at the end)" : "Добавить в навигацию (в конец списка)"
            };
            var plusPath = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource("IconPlus"),
                Width = 10,
                Height = 10,
                Fill = (Brush)FindResource("BrushTextPrimary"),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnAdd.Content = plusPath;
            btnAdd.Click += (s, ev) => OnRestoreTabRequested?.Invoke(tab.Key);
            Grid.SetColumn(btnAdd, 0);
            grid.Children.Add(btnAdd);

            var infoSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (TryFindResource(tab.IconKey) is Geometry geom)
            {
                var iconPath = new System.Windows.Shapes.Path
                {
                    Data = geom,
                    Width = 14,
                    Height = 14,
                    Fill = (Brush)FindResource("BrushTextPrimary"),
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                infoSp.Children.Add(iconPath);
            }
            var nameTb = new TextBlock
            {
                Text = tab.Title,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("BrushTextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            };
            infoSp.Children.Add(nameTb);
            Grid.SetColumn(infoSp, 1);
            grid.Children.Add(infoSp);

            var hintTb = new TextBlock
            {
                Text = isEn ? "Restore to sidebar" : "Вернуть в меню",
                FontSize = 11,
                Foreground = (Brush)FindResource("BrushTextMuted"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hintTb, 2);
            grid.Children.Add(hintTb);

            rowBorder.Child = grid;
            VaultItemsContainer.Children.Add(rowBorder);
        }
    }

    public void CollapseAllAccordions()
    {
        foreach (var exp in new[] { ExpCustom, ExpMonitor, ExpSysInfo })
        {
            if (exp != null && exp.IsExpanded && exp.Content is FrameworkElement content)
            {
                AnimateCollapse(exp, content);
            }
        }
        if (StickyAccordionBar != null) StickyAccordionBar.Visibility = Visibility.Collapsed;
        SettingsScrollViewer?.ScrollToTop();
    }

    public void UpdateAutoRamVisual(bool enabled)
    {
        _autoRamEnabled = enabled;
        UpdateToggleButtonStyles();
    }

    public void UpdateAutoGamePriorityVisual(bool enabled)
    {
        _autoGamePriorityEnabled = enabled;
        UpdateToggleButtonStyles();
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

            if (_accordionCollapsing.TryGetValue(exp, out bool isCollapsing) && isCollapsing)
            {
                e.Handled = true;
                _accordionCollapsing[exp] = false;
                AnimateExpand(exp, content);
                return;
            }

            if (exp.IsExpanded)
            {
                e.Handled = true;
                AnimateCollapse(exp, content);
            }
        }
    }

    private void Expander_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander exp && exp.Content is FrameworkElement content)
        {
            if (_accordionCollapsing.TryGetValue(exp, out bool isCol) && isCol) return;
            AnimateExpand(exp, content);
            UpdateStickyHeader();
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
        int animToken = Environment.TickCount;
        exp.Tag = animToken;

        content.BeginAnimation(FrameworkElement.HeightProperty, null);
        content.BeginAnimation(UIElement.OpacityProperty, null);

        content.Height = double.NaN;
        double availWidth = exp.ActualWidth > 40 ? exp.ActualWidth - 40 : 800;
        content.Measure(new Size(availWidth, double.PositiveInfinity));
        double target = Math.Max(32, content.DesiredSize.Height);

        double startH = 0;
        if (content.ActualHeight > 0 && content.ActualHeight < target - 5)
        {
            startH = content.ActualHeight;
        }

        content.Height = startH;
        content.Opacity = startH > 0 ? Math.Max(0.1, content.Opacity) : 0;

        var animH = new DoubleAnimation(startH, target, new Duration(TimeSpan.FromMilliseconds(260)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var animO = new DoubleAnimation(content.Opacity, 1.0, new Duration(TimeSpan.FromMilliseconds(220)));

        animH.Completed += (s, ev) =>
        {
            if (exp.Tag is int token && token == animToken)
            {
                content.BeginAnimation(FrameworkElement.HeightProperty, null);
                content.BeginAnimation(UIElement.OpacityProperty, null);
                content.Height = double.NaN;
                content.Opacity = 1;
                UpdateStickyHeader();
            }
        };

        content.BeginAnimation(FrameworkElement.HeightProperty, animH);
        content.BeginAnimation(UIElement.OpacityProperty, animO);
    }

    private void AnimateCollapse(Expander exp, FrameworkElement content)
    {
        if (exp == ExpCustom)
        {
            OnTabEditShouldDisable?.Invoke();
        }

        _accordionCollapsing[exp] = true;
        int animToken = Environment.TickCount;
        exp.Tag = animToken;

        content.BeginAnimation(FrameworkElement.HeightProperty, null);
        content.BeginAnimation(UIElement.OpacityProperty, null);

        double startH = content.ActualHeight;
        if (startH <= 1)
        {
            content.Height = double.NaN;
            content.Opacity = 1;
            exp.IsExpanded = false;
            _accordionCollapsing[exp] = false;
            UpdateStickyHeader();
            return;
        }

        content.Height = startH;
        var animH = new DoubleAnimation(startH, 0, new Duration(TimeSpan.FromMilliseconds(240)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        var animO = new DoubleAnimation(Math.Max(0, content.Opacity), 0, new Duration(TimeSpan.FromMilliseconds(180)));

        animH.Completed += (s, ev) =>
        {
            if (exp.Tag is int token && token == animToken)
            {
                content.BeginAnimation(FrameworkElement.HeightProperty, null);
                content.BeginAnimation(UIElement.OpacityProperty, null);
                content.Height = double.NaN;
                content.Opacity = 1;
                exp.IsExpanded = false;
                _accordionCollapsing[exp] = false;
                UpdateStickyHeader();
            }
        };

        content.BeginAnimation(FrameworkElement.HeightProperty, animH);
        content.BeginAnimation(UIElement.OpacityProperty, animO);
    }

    private void SettingsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateStickyHeader();
    private void SettingsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            SettingsScrollViewer.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 14, 14);
        }
        UpdateStickyHeader();
    }

    private void UpdateStickyHeader()
    {
        if (StickyAccordionBar == null || SettingsScrollViewer == null) return;

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
            double bottomY = p.Y + activeExp.ActualHeight;
            bool mainHeaderScrolledOff = (p.Y + 46 <= 0) && (bottomY > 80);
            if (mainHeaderScrolledOff)
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
        StickyAccordionBar.Visibility = Visibility.Collapsed;
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
        bool isEn = LocalizationService.IsEnglish;
        bool isRu = !isEn;

        Border CreateSection(string title, IEnumerable<KeyValuePair<string, string>> items)
        {
            var border = new Border
            {
                Background = (Brush)FindResource("BrushBgInner"),
                BorderBrush = (Brush)FindResource("BrushBorderCard"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("BrushTextMuted"),
                Margin = new Thickness(0, 0, 0, 10)
            });

            bool first = true;
            foreach (var kv in items)
            {
                if (!first)
                {
                    sp.Children.Add(new Separator
                    {
                        Background = (Brush)FindResource("BrushBorderSubtle"),
                        Margin = new Thickness(0, 6, 0, 6)
                    });
                }
                first = false;

                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var lbl = new TextBlock
                {
                    Text = kv.Key,
                    FontSize = 12,
                    Foreground = (Brush)FindResource("BrushTextSecondary"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 18, 0)
                };
                Grid.SetColumn(lbl, 0);

                var val = new TextBlock
                {
                    Text = string.IsNullOrEmpty(kv.Value) ? "--" : kv.Value,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("BrushTextPrimary"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    TextAlignment = TextAlignment.Right,
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
            string osCaption = _cachedOs.Caption;
            string osArch = _cachedOs.Architecture;
            if (isEn)
            {
                osCaption = osCaption
                    .Replace("Майкрософт", "Microsoft")
                    .Replace("Домашняя", "Home")
                    .Replace("Профессиональная", "Pro")
                    .Replace("Корпоративная", "Enterprise")
                    .Replace("для образовательных учреждений", "Education");

                if (osArch.Contains("64")) osArch = "64-bit";
                else if (osArch.Contains("32")) osArch = "32-bit";
            }
            else
            {
                if (osArch.Contains("64") && !osArch.Contains("разряд")) osArch = "64-разрядная";
                else if (osArch.Contains("32") && !osArch.Contains("разряд")) osArch = "32-разрядная";
            }

            SysInfoPanel.Children.Add(CreateSection(isRu ? "ОПЕРАЦИОННАЯ СИСТЕМА И ПК" : "OPERATING SYSTEM & PC", new[]
            {
                new KeyValuePair<string, string>(isRu ? "Имя компьютера" : "Computer Name", _cachedOs.ComputerName),
                new KeyValuePair<string, string>(isRu ? "Пользователь" : "Current User", _cachedOs.RegisteredUser),
                new KeyValuePair<string, string>(isRu ? "ОС" : "OS", $"{osCaption} ({osArch})"),
                new KeyValuePair<string, string>(isRu ? "Версия / Сборка" : "Version / Build", $"{_cachedOs.Version} (Build {_cachedOs.BuildNumber})"),
                new KeyValuePair<string, string>(isRu ? "Время работы (Uptime)" : "System Uptime", _cachedUptime),
                new KeyValuePair<string, string>(isRu ? "Дата установки" : "Install Date", _cachedOs.InstallDate)
            }));
        }

        // CPU
        if (_cachedCpu != null && _cachedCpu.Count > 0)
        {
            foreach (var cpu in _cachedCpu)
            {
                SysInfoPanel.Children.Add(CreateSection(isRu ? $"ПРОЦЕССОР — {cpu.Name}" : $"PROCESSOR — {cpu.Name}", new[]
                {
                    new KeyValuePair<string, string>(isRu ? "Физических ядер" : "Physical Cores", cpu.Cores.ToString()),
                    new KeyValuePair<string, string>(isRu ? "Логических процессоров" : "Logical Processors", cpu.LogicalProcessors.ToString()),
                    new KeyValuePair<string, string>(isRu ? "Базовая частота" : "Base Clock", cpu.MaxClockMhz),
                    new KeyValuePair<string, string>(isRu ? "Архитектура" : "Architecture", cpu.Architecture),
                    new KeyValuePair<string, string>(isRu ? "Сокет" : "Socket", cpu.SocketDesignation)
                }));
            }
        }

        // RAM Slots
        if (_cachedRam != null && _cachedRam.Count > 0)
        {
            long totalRamBytes = _cachedRam.Sum(r => r.CapacityBytes);
            var list = new List<KeyValuePair<string, string>>
            {
                new(isRu ? "Общий объем" : "Total Installed", StorageService.FormatBytes(totalRamBytes)),
                new(isRu ? "Слотов занято" : "Slots Occupied", _cachedRam.Count.ToString())
            };
            foreach (var slot in _cachedRam)
            {
                list.Add(new KeyValuePair<string, string>(
                    isRu ? $"Слот {slot.BankLabel}" : $"Slot {slot.BankLabel}",
                    $"{slot.FormattedSize} · {slot.Speed} · {slot.Manufacturer} ({slot.MemoryType})"
                ));
            }
            SysInfoPanel.Children.Add(CreateSection(isRu ? "ОПЕРАТИВНАЯ ПАМЯТЬ (RAM)" : "PHYSICAL MEMORY (RAM)", list));
        }

        // GPU
        if (_cachedGpus != null && _cachedGpus.Count > 0)
        {
            foreach (var g in _cachedGpus)
            {
                SysInfoPanel.Children.Add(CreateSection(isRu ? $"ВИДЕОКАРТА — {g.Name}" : $"GRAPHICS ADAPTER — {g.Name}", new[]
                {
                    new KeyValuePair<string, string>(isRu ? "Видеопамять (VRAM)" : "Video Memory (VRAM)", g.FormattedRam),
                    new KeyValuePair<string, string>(isRu ? "Версия драйвера" : "Driver Version", g.DriverVersion)
                }));
            }
        }

        // Motherboard & BIOS
        if (_cachedMb != null && _cachedBios != null)
        {
            SysInfoPanel.Children.Add(CreateSection(isRu ? "МАТЕРИНСКАЯ ПЛАТА И BIOS" : "MOTHERBOARD & BIOS", new[]
            {
                new KeyValuePair<string, string>(isRu ? "Производитель платы" : "Motherboard Vendor", _cachedMb.Manufacturer),
                new KeyValuePair<string, string>(isRu ? "Модель платы" : "Motherboard Model", _cachedMb.Product),
                new KeyValuePair<string, string>(isRu ? "Серийный номер" : "Serial Number", _cachedMb.SerialNumber),
                new KeyValuePair<string, string>(isRu ? "Производитель BIOS" : "BIOS Vendor", _cachedBios.Manufacturer),
                new KeyValuePair<string, string>(isRu ? "Версия BIOS" : "BIOS Version", _cachedBios.Version),
                new KeyValuePair<string, string>(isRu ? "Дата выпуска BIOS" : "BIOS Release Date", _cachedBios.ReleaseDate)
            }));
        }

        // Storage
        if (_cachedDisks != null && _cachedDisks.Count > 0)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var d in _cachedDisks)
            {
                string freeOfTotal = isEn ? $"{d.FormattedFree} free of {d.FormattedTotal}" : $"{d.FormattedFree} свободно из {d.FormattedTotal}";
                string diskLabel = d.Label;
                if (string.IsNullOrWhiteSpace(diskLabel) ||
                    diskLabel.Equals("Локальный диск", StringComparison.OrdinalIgnoreCase) ||
                    diskLabel.Equals("Local Disk", StringComparison.OrdinalIgnoreCase))
                {
                    diskLabel = isEn ? "Local Disk" : "Локальный диск";
                }
                string driveTypeDesc = d.DriveType switch
                {
                    "Fixed" => isEn ? "Fixed Disk" : "Локальный диск",
                    "Removable" => isEn ? "Removable USB" : "Съемный диск",
                    "Network" => isEn ? "Network Drive" : "Сетевой диск",
                    "CDRom" => isEn ? "CD/DVD Drive" : "Дисковод",
                    _ => d.DriveType
                };
                list.Add(new KeyValuePair<string, string>(
                    isEn ? $"Drive {d.DriveLetter}: [{diskLabel}]" : $"Диск {d.DriveLetter}: [{diskLabel}]",
                    $"{freeOfTotal} ({d.FileSystem}, {driveTypeDesc})"
                ));
            }
            SysInfoPanel.Children.Add(CreateSection(isRu ? "ФИЗИЧЕСКИЕ И ЛОГИЧЕСКИЕ НАКОПИТЕЛИ" : "PHYSICAL & LOGICAL DRIVES", list));
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
            _currentLanguage = lang == "RU" ? "RU" : "EN";
            UpdateToggleButtonStyles();
            OnLanguageChanged?.Invoke(_currentLanguage);
        }
    }

    private void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string theme)
        {
            bool wantLight = theme == "Light";
            _isLightTheme = wantLight;
            UpdateToggleButtonStyles();
            OnThemeChanged?.Invoke(wantLight);
        }
    }

    private void BtnTimeFormat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string mode)
        {
            bool is12h = mode == "12";
            _use12HourTime = is12h;
            UpdateToggleButtonStyles();
            OnTimeFormatChanged?.Invoke(is12h);
        }
    }

    private void SliderHudOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtHudOpacityVal != null) TxtHudOpacityVal.Text = $"{e.NewValue:0}%";
        if (!_isInitializing) OnHudOpacityChanged?.Invoke(e.NewValue);
    }

    private void SliderCpuAlert_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtCpuAlertVal != null) TxtCpuAlertVal.Text = $"{e.NewValue:0}%";
        if (!_isInitializing) OnCpuAlertChanged?.Invoke(e.NewValue);
    }

    private void SliderRamAlert_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtRamAlertVal != null) TxtRamAlertVal.Text = $"{e.NewValue:0}%";
        if (!_isInitializing) OnRamAlertChanged?.Invoke(e.NewValue);
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
        _autoRamEnabled = !_autoRamEnabled;
        UpdateToggleButtonStyles();
        OnAutoRamChanged?.Invoke(_autoRamEnabled);
    }

    private void BtnToggleAutoGamePriority_Click(object sender, RoutedEventArgs e)
    {
        _autoGamePriorityEnabled = !_autoGamePriorityEnabled;
        UpdateToggleButtonStyles();
        OnAutoGamePriorityChanged?.Invoke(_autoGamePriorityEnabled);
    }

    private void BtnResetSettings_Click(object sender, RoutedEventArgs e) => OnResetSettingsRequested?.Invoke();
    private void BtnToggleTabEdit_Click(object sender, RoutedEventArgs e) => OnToggleTabEditRequested?.Invoke();
    private void BtnOpenTabVault_Click(object sender, RoutedEventArgs e) => OnOpenTabVaultRequested?.Invoke();
}
