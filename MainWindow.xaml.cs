using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CoreSentinel.Services;

namespace CoreSentinel;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _telemetryTimer;
    private TelemetryHudWindow? _hudWindow;
    private double _telemetryIntervalSeconds = 1.0;
    private bool _autoRamPurgeEnabled = true;
    private DateTime _lastAutoPurgeTime = DateTime.MinValue;
    private bool _autoGamePriorityEnabled = true;
    private double _diskCThresholdGb = 7.0;
    private bool _isCpuPurgeMode = false;
    private bool _includeLaunchers = false;

    // Storage & Purge Cooldown & Re-entrancy guards
    private bool _isStorageOperationRunning = false;
    private DispatcherTimer? _storageCooldownTimer;
    private int _storageCooldownRemaining = 0;
    private DispatcherTimer? _ramCooldownTimer;
    private int _ramCooldownRemaining = 0;

    // Configuration state & Persistent user settings
    private UserSettingsModel _settings = new();
    private string _watchedDriveLetter = HardwareTelemetry.GetSystemDriveLetter();
    private string _currentLanguage = "EN"; // Default English
    private bool _isLightTheme = false;
    private double _cpuAlertThreshold = 90.0;
    private double _ramAlertThreshold = 85.0;
    private bool _sysInfoLoaded = false;
    private List<StartupEntry> _startupEntries = new();
    private List<NetworkAdapterInfo> _networkAdapters = new();
    private readonly Dictionary<Expander, bool> _accordionCollapsing = new();
    private bool _use12HourTime = true;
    private string _gameDriveFilter = "ALL";
    private List<InstalledGameInfo> _installedGames = new();
    private bool _isScanningGames = false;

    // Navigation Tabs & Vault state
    public class TabItemModel
    {
        public string Key { get; set; } = "";
        public int TagIndex { get; set; }
        public string EnTitle { get; set; } = "";
        public string RuTitle { get; set; } = "";
        public string IconKey { get; set; } = "";
        public Button? PillButton { get; set; }
        public TextBlock? TitleTextBlock { get; set; }
    }

    private readonly List<TabItemModel> _activeTabs = new();
    private readonly List<TabItemModel> _vaultTabs = new();
    private bool _isEditingTabs = false;
    private int _currentTabIndex = 0;
    private TabItemModel? _draggedTab;
    private Point _dragStartPoint;
    private TaskCompletionSource<bool>? _modalTcs;

    // Cached hardware specs for instant multilingual translation
    private List<CpuInfo>? _cachedCpu;
    private List<RamSlot>? _cachedRam;
    private List<DiskDetail>? _cachedDisks;
    private List<GpuInfo>? _cachedGpus;
    private MotherboardInfo? _cachedMb;
    private BiosInfo? _cachedBios;
    private OsInfo? _cachedOs;
    private List<NetworkAdapterInfo>? _cachedNet;
    private string _cachedUptime = "";

    public MainWindow()
    {
        InitializeComponent();
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;

        LoadAndApplyUserSettings();

        _telemetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_telemetryIntervalSeconds)
        };
        _telemetryTimer.Tick += (s, e) => UpdateTelemetry();
        _telemetryTimer.Start();

        // Initial setup
        InitNavigationTabs();
        ApplyLanguageStrings();
        SetTheme(_isLightTheme);
        UpdateTelemetry();
        HighlightTab(0);
        LogRam(_currentLanguage == "EN" ? "CoreSentinel initialized. Background telemetry and security monitoring active." : "CoreSentinel инициализирован. Фоновая телеметрия и мониторинг активны.");
        InitStorageTab();
        InitSettingsTab();
        RefreshVaultUI();

        if (SliderHudOpacity != null) SliderHudOpacity.Value = _settings.HudOpacity;
        if (SliderCpuAlert != null) SliderCpuAlert.Value = _cpuAlertThreshold;
        if (SliderRamAlert != null) SliderRamAlert.Value = _ramAlertThreshold;
    }


    private void LoadAndApplyUserSettings()
    {
        _settings = SettingsPersistenceService.Load();

        _currentLanguage = _settings.Language == "RU" ? "RU" : "EN";
        HardwareTelemetry.IsRussianLanguage = (_currentLanguage == "RU");
        _isLightTheme = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        _use12HourTime = _settings.Use12HourTime;
        _telemetryIntervalSeconds = _settings.TelemetryInterval > 0 ? _settings.TelemetryInterval : 1.0;
        _diskCThresholdGb = _settings.DiskWarningThresholdGb > 0 ? _settings.DiskWarningThresholdGb : 7.0;
        _cpuAlertThreshold = _settings.CpuAlertThreshold > 0 ? _settings.CpuAlertThreshold : 90.0;
        _ramAlertThreshold = _settings.RamAlertThreshold > 0 ? _settings.RamAlertThreshold : 85.0;
        _autoRamPurgeEnabled = _settings.AutoRamPurge;
        _autoGamePriorityEnabled = _settings.AutoGamePriority;
        if (!string.IsNullOrWhiteSpace(_settings.WatchedDriveLetter))
        {
            _watchedDriveLetter = _settings.WatchedDriveLetter;
        }
        _includeLaunchers = _settings.IncludeLaunchers;
        _isCpuPurgeMode = string.Equals(_settings.PurgeMode, "CPU", StringComparison.OrdinalIgnoreCase);
    }

    private void SaveCurrentUserSettings()
    {
        try
        {
            _settings.Language = _currentLanguage;
            _settings.Theme = _isLightTheme ? "Light" : "Dark";
            _settings.Use12HourTime = _use12HourTime;
            _settings.TelemetryInterval = _telemetryIntervalSeconds;
            _settings.DiskWarningThresholdGb = _diskCThresholdGb;
            _settings.CpuAlertThreshold = _cpuAlertThreshold;
            _settings.RamAlertThreshold = _ramAlertThreshold;
            _settings.HudOpacity = SliderHudOpacity?.Value ?? 90.0;
            _settings.AutoRamPurge = _autoRamPurgeEnabled;
            _settings.AutoGamePriority = _autoGamePriorityEnabled;
            _settings.WatchedDriveLetter = _watchedDriveLetter;
            _settings.IncludeLaunchers = _includeLaunchers;
            _settings.PurgeMode = _isCpuPurgeMode ? "CPU" : "RAM";
            _settings.ActiveTabs = _activeTabs.Select(t => t.Key).ToList();
            _settings.VaultTabs = _vaultTabs.Select(t => t.Key).ToList();

            SettingsPersistenceService.Save(_settings);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to persist runtime settings: {ex.Message}");
        }
    }

    #region Drive Dropdown & Switcher Logic

    private void BtnDiskDropdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var cm = new ContextMenu();
        bool isEn = _currentLanguage == "EN";



        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            string letter = drive.Name.TrimEnd('\\', ':');
            string vol = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? (letter == "C" ? (isEn ? "System" : "Системный") : (isEn ? "Data" : "Данные"))
                : drive.VolumeLabel;
            string freeStr = StorageService.FormatBytes(drive.AvailableFreeSpace);
            bool isCurrent = string.Equals(letter, _watchedDriveLetter, StringComparison.OrdinalIgnoreCase);

            var item = new MenuItem
            {
                Header = isEn ? $"Drive {letter}: [{vol}] · {freeStr} free" : $"Диск {letter}: [{vol}] · свободно {freeStr}",
                FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal
            };
            item.Click += (s, ev) => SelectWatchedDrive(letter);
            cm.Items.Add(item);
        }

        cm.PlacementTarget = btn;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private void SelectWatchedDrive(string letter)
    {
        _watchedDriveLetter = letter;
        bool isEn = _currentLanguage == "EN";

        var storageTab = _activeTabs.Concat(_vaultTabs).FirstOrDefault(t => t.Key == "storage");
        if (storageTab != null)
        {
            storageTab.EnTitle = $"Storage ({letter}:)";
            storageTab.RuTitle = $"Хранилище ({letter}:)";
        }
        RebuildNavPills();
        RefreshVaultUI();

        if (DashDiskCTitle != null) DashDiskCTitle.Text = isEn ? $"STORAGE ({letter}:)" : $"НАКОПИТЕЛЬ ({letter}:)";
        if (StorageGaugeTitle != null) StorageGaugeTitle.Text = isEn ? $"Drive Capacity ({letter}:):" : $"Емкость накопителя ({letter}:):";
        UpdateTelemetry();
        _ = ScanJunkAsync(showLog: false);
        SaveCurrentUserSettings();
    }

    #endregion

    private void UpdateTelemetry()
    {
        var sample = HardwareTelemetry.Sample();
        string sys = sample.SystemDriveLetter;
        string data = sample.DataDriveLetter;
        bool isEn = _currentLanguage == "EN";

        // Footer Live Time & UTC + N Offset
        DateTime now = DateTime.Now;
        var offset = TimeZoneInfo.Local.GetUtcOffset(now);
        string offsetSign = offset >= TimeSpan.Zero ? "+" : "-";
        string offsetStr = offset.Minutes == 0
            ? $"UTC{offsetSign}{Math.Abs(offset.Hours)}"
            : $"UTC{offsetSign}{Math.Abs(offset.Hours)}:{Math.Abs(offset.Minutes):D2}";

        string localClock = _use12HourTime
            ? now.ToString("hh:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture)
            : now.ToString("HH:mm:ss");
        if (FooterClockText != null)
        {
            FooterClockText.Text = $"{localClock} ({offsetStr})";
        }

        // Quick badges
        QuickCpuText.Text = $"{sample.CpuLoadPercent:0}%";
        QuickDiskCLabel.Text = isEn ? $"Drive {sys}: " : $"Диск {sys}: ";
        QuickDiskCText.Text = isEn ? $"{sample.DiskCFreeGb:0.#} GB" : $"{sample.DiskCFreeGb:0.#} ГБ";

        // Dashboard CPU
        DashCpuVal.Text = $"{sample.CpuLoadPercent:0}%";
        DashCpuProgress.Value = sample.CpuLoadPercent;
        DashCpuSub.Text = isEn
            ? $"{HardwareTelemetry.GetProcessorName()} · {Environment.ProcessorCount} threads"
            : $"{HardwareTelemetry.GetProcessorName()} · {Environment.ProcessorCount} потоков";

        // Dashboard RAM & Detail
        if (sample.RamTotalBytes > 0)
        {
            QuickRamText.Text = $"{sample.RamUsedPercent:0}%";
            DashRamVal.Text = $"{sample.RamUsedPercent:0}%";
            DashRamProgress.Value = sample.RamUsedPercent;
            double usedGb = (double)sample.RamUsedBytes / (1024 * 1024 * 1024);
            double totalGb = (double)sample.RamTotalBytes / (1024 * 1024 * 1024);
            DashRamSub.Text = isEn
                ? $"Used: {usedGb:0.##} / {totalGb:0.##} GB"
                : $"Занято: {usedGb:0.##} / {totalGb:0.##} ГБ";

            if (!_isCpuPurgeMode)
            {
                RamDetailTotal.Text = isEn ? $"{totalGb:0.##} GB" : $"{totalGb:0.##} ГБ";
                RamDetailUsed.Text = isEn ? $"{usedGb:0.##} GB ({sample.RamUsedPercent:0}%)" : $"{usedGb:0.##} ГБ ({sample.RamUsedPercent:0}%)";
                RamDetailAvail.Text = isEn ? $"{(totalGb - usedGb):0.##} GB" : $"{(totalGb - usedGb):0.##} ГБ";
            }
        }
        else
        {
            QuickRamText.Text = "N/A";
            DashRamVal.Text = "N/A";
            DashRamProgress.Value = 0;
            DashRamSub.Text = isEn ? "Memory data unavailable" : "Данные ОЗУ недоступны";

            if (!_isCpuPurgeMode)
            {
                RamDetailTotal.Text = "N/A";
                RamDetailUsed.Text = "N/A";
                RamDetailAvail.Text = "N/A";
            }
        }

        // CPU Purge Mode Detail
        if (_isCpuPurgeMode)
        {
            RamDetailTotal.Text = HardwareTelemetry.GetProcessorName();
            int cores = HardwareTelemetry.GetPhysicalCoreCount();
            RamDetailUsed.Text = cores > 0 
                ? $"{cores}C / {Environment.ProcessorCount}T" 
                : $"{Environment.ProcessorCount}T";
            RamDetailAvail.Text = $"{sample.CpuLoadPercent:0.#}%";
        }

        // Watched Drive calculation for Dashboard Card 3 and Storage Tab
        var watchedInfo = StorageService.GetDriveInfo(_watchedDriveLetter);
        double watchedTotalGb = watchedInfo != null ? (double)watchedInfo.TotalBytes / (1024 * 1024 * 1024) : 0.0;
        double watchedFreeGb = watchedInfo != null ? (double)watchedInfo.FreeBytes / (1024 * 1024 * 1024) : 0.0;
        double watchedUsedGb = Math.Max(0, watchedTotalGb - watchedFreeGb);
        double watchedUsedPercent = watchedTotalGb > 0 ? (watchedUsedGb / watchedTotalGb) * 100.0 : 0.0;

        // Dashboard Disk Card 3
        DashDiskCTitle.Text = isEn ? $"STORAGE {_watchedDriveLetter}:" : $"НАКОПИТЕЛЬ {_watchedDriveLetter}:";
        DashDiskCVal.Text = isEn ? $"{watchedFreeGb:0.#} GB" : $"{watchedFreeGb:0.#} ГБ";
        DashDiskCProgress.Value = watchedUsedPercent;
        DashDiskCSub.Text = isEn
            ? $"Free {watchedFreeGb:0.#} of {watchedTotalGb:0.#} GB"
            : $"Свободно {watchedFreeGb:0.#} из {watchedTotalGb:0.#} ГБ";

        // Disk Secondary (Data)
        if (sample.HasSecondaryDataDrive)
        {
            DashDiskDRow.Visibility = Visibility.Visible;
            DashDiskDTitle.Text = isEn ? $"Data Drive {data}:" : $"Диск данных {data}:";
            DashDiskDBadge.Text = isEn ? $"Drive {data}:" : $"Диск {data}:";
            DashDiskDText.Text = isEn
                ? $"Free: {sample.DiskDFreeGb:0.#} GB of {sample.DiskDTotalGb:0.#} GB"
                : $"Свободно: {sample.DiskDFreeGb:0.#} ГБ из {sample.DiskDTotalGb:0.#} ГБ";
        }
        else
        {
            DashDiskDRow.Visibility = Visibility.Collapsed;
        }

        // Storage Tab Progress & Labels
        StorageGaugeTitle.Text = isEn ? $"Storage Capacity {_watchedDriveLetter}:" : $"Емкость накопителя {_watchedDriveLetter}:";
        StorageProgressC.Value = watchedUsedPercent;
        StorageHeaderStatus.Text = isEn
            ? $"Free: {watchedFreeGb:0.#} GB of {watchedTotalGb:0.#} GB"
            : $"Свободно: {watchedFreeGb:0.#} ГБ из {watchedTotalGb:0.#} ГБ";

        // Color threshold for Disk (Calm Monochrome)
        QuickDiskCText.Foreground = (SolidColorBrush)FindResource("BrushTextPrimary");
        DashDiskCVal.Foreground = (SolidColorBrush)FindResource("BrushTextPrimary");

        if (watchedFreeGb < 4.0)
        {
            DashRecommendation.Text = isEn
                ? $"Warning: Drive {_watchedDriveLetter}: is low on space (< 4 GB). Cleanup recommended."
                : $"Внимание: На накопителе {_watchedDriveLetter}: мало свободного места (< 4 ГБ). Рекомендуется очистить кэш.";
            DashRecommendation.Foreground = (SolidColorBrush)FindResource("BrushTextPrimary");
            DashHealthBadge.Text = isEn ? "Low Space" : "Мало места";
            DashHealthBadge.Foreground = (SolidColorBrush)FindResource("BrushTextPrimary");
        }
        else if (watchedFreeGb < _diskCThresholdGb)
        {
            DashRecommendation.Text = isEn
                ? $"Drive {_watchedDriveLetter}: free space is approaching limit (< {_diskCThresholdGb:0} GB)."
                : $"Свободное место на накопителе {_watchedDriveLetter}: подходит к лимиту (< {_diskCThresholdGb:0} ГБ).";
            DashRecommendation.Foreground = (SolidColorBrush)FindResource("BrushTextSecondary");
            DashHealthBadge.Text = isEn ? $"Limit {_watchedDriveLetter}:" : $"Лимит {_watchedDriveLetter}:";
            DashHealthBadge.Foreground = (SolidColorBrush)FindResource("BrushTextSecondary");
        }
        else
        {
            DashRecommendation.Text = isEn
                ? "All systems operating normally. Disks and memory healthy."
                : "Система работает стабильно. Накопители и память в норме.";
            DashRecommendation.Foreground = (SolidColorBrush)FindResource("BrushTextSecondary");
            DashHealthBadge.Text = isEn ? "Healthy" : "В норме";
            DashHealthBadge.Foreground = (SolidColorBrush)FindResource("BrushTextSecondary");
        }

        // Game Mode status (Calm Monochrome)
        if (sample.IsGameRunning)
        {
            GameModeDot.Fill = (SolidColorBrush)FindResource("BrushTextPrimary");
            GameModeText.Text = isEn ? "Active" : "Активен";
            GameModeText.ToolTip = isEn ? $"Active game: {sample.ActiveGameName}" : $"Активная игра: {sample.ActiveGameName}";
            GameModeText.Foreground = (SolidColorBrush)FindResource("BrushTextPrimary");
            DashGameStatusDetail.Text = isEn
                ? $"Game process '{sample.ActiveGameName}' detected. Game Priority active."
                : $"Обнаружен игровой процесс '{sample.ActiveGameName}'. Game Priority активен.";
            DashGameBadge.Text = isEn ? "Active" : "Активен";
            DashGameBadge.Foreground = (SolidColorBrush)FindResource("BrushTextPrimary");

            if (_autoGamePriorityEnabled)
            {
                MemoryService.OptimizeGamePriorities();
            }
        }
        else
        {
            GameModeDot.Fill = (SolidColorBrush)FindResource("BrushTextMuted");
            GameModeText.Text = isEn ? "Waiting for games" : "Ожидание игр";
            GameModeText.ToolTip = null;
            GameModeText.Foreground = (SolidColorBrush)FindResource("BrushTextSecondary");
            DashGameStatusDetail.Text = isEn
                ? "No games detected. Background monitoring active."
                : "Игры не обнаружены. Фоновый мониторинг активен.";
            DashGameBadge.Text = isEn ? "Waiting" : "В ожидании";
            DashGameBadge.Foreground = (SolidColorBrush)FindResource("BrushTextSecondary");
        }

        // Automatic RAM Purge Trigger (> threshold)
        if (_autoRamPurgeEnabled && sample.RamUsedPercent >= _ramAlertThreshold && (DateTime.Now - _lastAutoPurgeTime).TotalSeconds > 60)
        {
            _lastAutoPurgeTime = DateTime.Now;
            try
            {
                var res = MemoryService.PurgeMemory();
                string freedStr = StorageService.FormatBytes(res.BytesFreed);
                string nowTime = FormatTimeLog(DateTime.Now);
                string autoMsg = isEn
                    ? $"[AUTO-PURGE {nowTime}] RAM load {sample.RamUsedPercent:0}%. Released {freedStr} (optimized {res.ProcessesOptimized} processes)."
                    : $"[АВТО-СБРОС {nowTime}] Нагрузка ОЗУ {sample.RamUsedPercent:0}%. Высвобождено {freedStr} (оптимизировано {res.ProcessesOptimized} процессов).";
                LogRam(autoMsg);
                if (SettingsRamStatusText != null)
                {
                    SettingsRamStatusText.Text = isEn
                        ? $"Auto-purge: {nowTime} (-{freedStr})"
                        : $"Автоочистка: {nowTime} (-{freedStr})";
                }
            }
            catch
            {
                // Non-blocking telemetry
            }
        }

        // Live Active Games scanning when on Games Tab
        if (_currentTabIndex == 3)
        {
            RefreshGamesTabActiveList();
        }
    }

    #region Tab Navigation & Vault Management

    private void InitNavigationTabs()
    {
        _activeTabs.Clear();
        _vaultTabs.Clear();

        string storageTitleEn = $"Storage ({_watchedDriveLetter}:)";
        string storageTitleRu = $"Хранилище ({_watchedDriveLetter}:)";

        var allKnownTabs = new Dictionary<string, TabItemModel>
        {
            ["overview"] = new TabItemModel { Key = "overview", TagIndex = 0, EnTitle = "Overview", RuTitle = "Обзор", IconKey = "IconSquaresFour" },
            ["ram"] = new TabItemModel { Key = "ram", TagIndex = 1, EnTitle = _isCpuPurgeMode ? "CPU Optimization" : "Memory Purge", RuTitle = _isCpuPurgeMode ? "Оптимизация ЦП" : "Очистка ОЗУ", IconKey = _isCpuPurgeMode ? "IconCpu" : "IconRam" },
            ["storage"] = new TabItemModel { Key = "storage", TagIndex = 2, EnTitle = storageTitleEn, RuTitle = storageTitleRu, IconKey = "IconHardDrive" },
            ["games"] = new TabItemModel { Key = "games", TagIndex = 3, EnTitle = "Games", RuTitle = "Игры", IconKey = "IconGamepad" },
            ["settings"] = new TabItemModel { Key = "settings", TagIndex = 4, EnTitle = "Settings", RuTitle = "Настройки", IconKey = "IconGear" }
        };

        if (_settings?.ActiveTabs != null && _settings.ActiveTabs.Count > 0)
        {
            foreach (var key in _settings.ActiveTabs)
            {
                if (allKnownTabs.TryGetValue(key, out var tab))
                {
                    _activeTabs.Add(tab);
                    allKnownTabs.Remove(key);
                }
            }
            if (!_activeTabs.Any(t => t.Key == "settings"))
            {
                if (allKnownTabs.TryGetValue("settings", out var settingsTab))
                {
                    _activeTabs.Add(settingsTab);
                    allKnownTabs.Remove("settings");
                }
            }
            if (_settings.VaultTabs != null)
            {
                foreach (var key in _settings.VaultTabs)
                {
                    if (allKnownTabs.TryGetValue(key, out var tab))
                    {
                        _vaultTabs.Add(tab);
                        allKnownTabs.Remove(key);
                    }
                }
            }
            foreach (var kvp in allKnownTabs)
            {
                _activeTabs.Add(kvp.Value);
            }
        }
        else
        {
            _activeTabs.Add(allKnownTabs["overview"]);
            _activeTabs.Add(allKnownTabs["ram"]);
            _activeTabs.Add(allKnownTabs["storage"]);
            _activeTabs.Add(allKnownTabs["games"]);
            _activeTabs.Add(allKnownTabs["settings"]);
        }
    }

    private void ResetTabsToDefaults()
    {
        _isEditingTabs = false;
        if (BtnToggleTabEdit != null)
        {
            BtnToggleTabEdit.Style = (Style)FindResource("BtnPill");
            if (TxtToggleTabEdit != null) TxtToggleTabEdit.Text = _currentLanguage == "EN" ? "Edit" : "Редактировать";
        }
        if (VaultDrawerPanel != null)
        {
            VaultDrawerPanel.Visibility = Visibility.Collapsed;
            if (TxtBtnOpenTabVault != null) TxtBtnOpenTabVault.Text = _currentLanguage == "EN" ? "Open" : "Открыть";
        }
        _settings.ActiveTabs = new() { "overview", "ram", "storage", "games", "settings" };
        _settings.VaultTabs = new();
        InitNavigationTabs();
        RebuildNavPills();
        RefreshVaultUI();
        SaveCurrentUserSettings();
    }

    private void RebuildNavPills()
    {
        if (NavPillsContainer == null) return;
        NavPillsContainer.Children.Clear();
        bool isEn = _currentLanguage == "EN";

        var activeBrush = (SolidColorBrush)FindResource("BrushBgButton");
        var activeBorder = (SolidColorBrush)FindResource("BrushBorderButton");
        var activeText = (SolidColorBrush)FindResource("BrushTextPrimary");
        var inactiveBrush = Brushes.Transparent;
        var inactiveBorder = Brushes.Transparent;
        var inactiveText = (SolidColorBrush)FindResource("BrushTextSecondary");
        var navPillStyle = (Style)FindResource("BtnNavPill");

        for (int i = 0; i < _activeTabs.Count; i++)
        {
            var tab = _activeTabs[i];
            bool isSelected = tab.TagIndex == _currentTabIndex;

            var rowGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (_isEditingTabs)
            {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            // Main navigation pill button
            var btn = new Button
            {
                Style = navPillStyle,
                Tag = tab.TagIndex.ToString(),
                Background = isSelected ? activeBrush : inactiveBrush,
                BorderBrush = isSelected ? activeBorder : inactiveBorder,
                Foreground = isSelected ? activeText : inactiveText,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            btn.Click += TabNav_Click;

            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (FindResource(tab.IconKey) is Geometry geom)
            {
                var path = new System.Windows.Shapes.Path
                {
                    Data = geom,
                    Width = 14,
                    Height = 14,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                path.SetBinding(System.Windows.Shapes.Path.FillProperty, new System.Windows.Data.Binding("Foreground") { Source = btn });
                sp.Children.Add(path);
            }

            string title = isEn ? tab.EnTitle : tab.RuTitle;
            var tb = new TextBlock
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            sp.Children.Add(tb);
            btn.Content = sp;
            tab.PillButton = btn;
            tab.TitleTextBlock = tb;

            Grid.SetColumn(btn, 0);
            rowGrid.Children.Add(btn);

            if (_isEditingTabs)
            {
                // Delete button (✕) - Never allow deleting the Settings tab!
                if (tab.Key != "settings")
                {
                    var btnDel = new Button
                    {
                        Style = (Style)FindResource("BtnIconCircle"),
                        Tag = tab,
                        Width = 26,
                        Height = 26,
                        Margin = new Thickness(4, 0, 2, 0),
                        ToolTip = isEn ? "Remove to Tab Storage" : "Удалить в Хранилище вкладок",
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var delPath = new System.Windows.Shapes.Path
                    {
                        Data = (Geometry)FindResource("IconClose"),
                        Width = 9,
                        Height = 9,
                        Fill = (SolidColorBrush)FindResource("BrushTextSecondary"),
                        Stretch = Stretch.Uniform
                    };
                    btnDel.Content = delPath;
                    btnDel.Click += BtnRemoveTab_Click;
                    Grid.SetColumn(btnDel, 1);
                    rowGrid.Children.Add(btnDel);
                }

                // Reorder grip handle (☰) - Pointer/hand cursor, live drag tracking
                var gripBorder = new Border
                {
                    Tag = tab,
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(6),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    ToolTip = isEn ? "Hold and drag to reorder" : "Зажмите для перемещения",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 0)
                };
                var gripPath = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource("IconGripLines"),
                    Width = 12,
                    Height = 12,
                    Fill = (SolidColorBrush)FindResource("BrushTextMuted"),
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                gripBorder.Child = gripPath;
                gripBorder.PreviewMouseLeftButtonDown += TabGrip_PreviewMouseLeftButtonDown;
                gripBorder.PreviewMouseMove += TabGrip_PreviewMouseMove;
                gripBorder.PreviewMouseLeftButtonUp += TabGrip_PreviewMouseLeftButtonUp;
                gripBorder.LostMouseCapture += TabGrip_LostMouseCapture;
                Grid.SetColumn(gripBorder, 2);
                rowGrid.Children.Add(gripBorder);
            }

            NavPillsContainer.Children.Add(rowGrid);
        }
    }

    private void TabNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int tabIndex))
        {
            HighlightTab(tabIndex);
        }
    }

    private void HighlightTab(int index)
    {
        _currentTabIndex = index;

        // Auto-disable tab editing when leaving the Settings tab
        if (index != 4)
        {
            DisableTabEditMode();
        }

        TabContentDashboard.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        TabContentRam.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        TabContentStorage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        TabContentGames.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        TabContentSettings.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;

        var activeBrush = (SolidColorBrush)FindResource("BrushBgButton");
        var activeBorder = (SolidColorBrush)FindResource("BrushBorderButton");
        var activeText = (SolidColorBrush)FindResource("BrushTextPrimary");
        var inactiveBrush = Brushes.Transparent;
        var inactiveBorder = Brushes.Transparent;
        var inactiveText = (SolidColorBrush)FindResource("BrushTextSecondary");

        foreach (var tab in _activeTabs)
        {
            if (tab.PillButton != null)
            {
                bool isSelected = tab.TagIndex == index;
                tab.PillButton.Background = isSelected ? activeBrush : inactiveBrush;
                tab.PillButton.BorderBrush = isSelected ? activeBorder : inactiveBorder;
                tab.PillButton.Foreground = isSelected ? activeText : inactiveText;
            }
        }

        if (index == 3)
        {
            RefreshGamesTabActiveList();
        }
    }

    private void DisableTabEditMode()
    {
        if (!_isEditingTabs) return;
        _isEditingTabs = false;
        if (BtnToggleTabEdit != null)
        {
            BtnToggleTabEdit.Style = (Style)FindResource("BtnPill");
            if (TxtToggleTabEdit != null)
            {
                TxtToggleTabEdit.Text = _currentLanguage == "EN" ? "Edit" : "Редактировать";
            }
        }
        RebuildNavPills();
    }

    private FrameworkElement? _draggedRow;
    private int _dragStartIndex = -1;
    private int _targetDropIndex = -1;

    private void TabGrip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement grip && grip.Tag is TabItemModel tab)
        {
            _draggedTab = tab;
            _dragStartIndex = _activeTabs.IndexOf(tab);
            _targetDropIndex = _dragStartIndex;
            _draggedRow = FindVisualParent<Grid>(grip);
            if (_draggedRow != null)
            {
                _dragStartPoint = e.GetPosition(NavPillsContainer);
                _draggedRow.Opacity = 0.85;
                Panel.SetZIndex(_draggedRow, 999);
                grip.CaptureMouse();
                e.Handled = true;
            }
        }
    }

    private void TabGrip_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedTab == null || _draggedRow == null || _dragStartIndex < 0 || e.LeftButton != MouseButtonState.Pressed) return;

        Point curPos = e.GetPosition(NavPillsContainer);
        double deltaY = curPos.Y - _dragStartPoint.Y;
        _draggedRow.RenderTransform = new TranslateTransform(0, deltaY);

        double rowH = _draggedRow.ActualHeight > 0 ? _draggedRow.ActualHeight + 3 : 39;
        int slotOffset = (int)Math.Round(deltaY / rowH);
        int newTarget = Math.Clamp(_dragStartIndex + slotOffset, 0, _activeTabs.Count - 1);
        _targetDropIndex = newTarget;

        for (int i = 0; i < NavPillsContainer.Children.Count; i++)
        {
            if (i == _dragStartIndex) continue;
            var child = (UIElement)NavPillsContainer.Children[i];
            if (_dragStartIndex < _targetDropIndex)
            {
                if (i > _dragStartIndex && i <= _targetDropIndex)
                {
                    child.RenderTransform = new TranslateTransform(0, -rowH);
                }
                else
                {
                    child.RenderTransform = null;
                }
            }
            else if (_dragStartIndex > _targetDropIndex)
            {
                if (i >= _targetDropIndex && i < _dragStartIndex)
                {
                    child.RenderTransform = new TranslateTransform(0, rowH);
                }
                else
                {
                    child.RenderTransform = null;
                }
            }
            else
            {
                child.RenderTransform = null;
            }
        }
    }

    private void CommitDrop()
    {
        if (_draggedTab != null && _dragStartIndex >= 0 && _targetDropIndex >= 0 && _dragStartIndex != _targetDropIndex)
        {
            var tab = _draggedTab;
            int from = _dragStartIndex;
            int to = _targetDropIndex;
            _draggedTab = null;
            _draggedRow = null;
            _dragStartIndex = -1;
            _targetDropIndex = -1;

            _activeTabs.RemoveAt(from);
            _activeTabs.Insert(to, tab);
            RebuildNavPills();
            SaveCurrentUserSettings();
            return;
        }
        if (_draggedTab != null)
        {
            _draggedTab = null;
            _draggedRow = null;
            _dragStartIndex = -1;
            _targetDropIndex = -1;
            RebuildNavPills();
        }
    }

    private void TabGrip_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CommitDrop();
        if (sender is FrameworkElement grip && grip.IsMouseCaptured)
        {
            grip.ReleaseMouseCapture();
        }
    }

    private void TabGrip_LostMouseCapture(object sender, MouseEventArgs e)
    {
        CommitDrop();
    }

    private async void BtnRemoveTab_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.Tag is TabItemModel tab)
            {
                bool isEn = _currentLanguage == "EN";
                string title = isEn ? "Remove Tab?" : "Удалить вкладку?";
                string msg = isEn
                    ? $"Move tab '{tab.EnTitle}' to Tab Storage? You can restore it anytime in Settings."
                    : $"Переместить вкладку «{tab.RuTitle}» в Хранилище вкладок? Вы сможете вернуть её в любой момент через настройки.";
                string confirm = isEn ? "Remove" : "Удалить";
                string cancel = isEn ? "Cancel" : "Отмена";

                bool ok = await ShowModernConfirmDialogAsync(title, msg, confirm, cancel, "IconClose");
                if (!ok) return;

                _activeTabs.Remove(tab);
                if (!_vaultTabs.Contains(tab))
                {
                    _vaultTabs.Add(tab);
                }

                if (_currentTabIndex == tab.TagIndex)
                {
                    var nextTab = _activeTabs.FirstOrDefault();
                    if (nextTab != null)
                    {
                        HighlightTab(nextTab.TagIndex);
                    }
                }

                RebuildNavPills();
                RefreshVaultUI();
                SaveCurrentUserSettings();
                StatusFooterText.Text = isEn
                    ? $"Tab '{tab.EnTitle}' moved to Tab Storage."
                    : $"Вкладка «{tab.RuTitle}» отправлена в Хранилище вкладок.";
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to remove tab", ex);
        }
    }

    private void BtnRestoreTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is TabItemModel tab)
        {
            _vaultTabs.Remove(tab);
            if (!_activeTabs.Contains(tab))
            {
                _activeTabs.Add(tab);
            }
            RebuildNavPills();
            RefreshVaultUI();
            SaveCurrentUserSettings();

            bool isEn = _currentLanguage == "EN";
            StatusFooterText.Text = isEn
                ? $"Tab '{tab.EnTitle}' restored to navigation."
                : $"Вкладка «{tab.RuTitle}» добавлена в конец навигации.";
        }
    }

    private void BtnToggleTabEdit_Click(object sender, RoutedEventArgs e)
    {
        _isEditingTabs = !_isEditingTabs;
        bool isEn = _currentLanguage == "EN";

        BtnToggleTabEdit.Style = (Style)FindResource(_isEditingTabs ? "BtnPrimaryWhite" : "BtnPill");
        TxtToggleTabEdit.Text = _isEditingTabs
            ? (isEn ? "Done" : "Готово")
            : (isEn ? "Edit" : "Редактировать");

        RebuildNavPills();
        StatusFooterText.Text = _isEditingTabs
            ? (isEn ? "Tab edit mode active. Drag ☰ to reorder, ✕ to remove." : "Редактирование вкладок: зажимайте ☰ для перемещения, ✕ для удаления.")
            : (isEn ? "Tab order saved." : "Порядок вкладок сохранен.");
    }

    private void BtnOpenTabVault_Click(object sender, RoutedEventArgs e)
    {
        bool willOpen = VaultDrawerPanel.Visibility != Visibility.Visible;
        VaultDrawerPanel.Visibility = willOpen ? Visibility.Visible : Visibility.Collapsed;
        bool isEn = _currentLanguage == "EN";

        TxtBtnOpenTabVault.Text = willOpen
            ? (isEn ? "Close" : "Закрыть")
            : (isEn ? "Open" : "Открыть");

        if (IconVaultChevron.RenderTransform is not RotateTransform)
        {
            IconVaultChevron.RenderTransform = new RotateTransform(0, 4.5, 4.5);
        }
        ((RotateTransform)IconVaultChevron.RenderTransform).Angle = willOpen ? 180 : 0;

        if (willOpen)
        {
            RefreshVaultUI();
        }
    }

    private void RefreshVaultUI()
    {
        if (VaultItemsContainer == null) return;
        bool isEn = _currentLanguage == "EN";
        VaultItemsContainer.Children.Clear();

        if (_vaultTabs.Count == 0)
        {
            TxtVaultEmpty.Visibility = Visibility.Visible;
            TxtVaultEmpty.Text = isEn
                ? "All available tabs are active in navigation."
                : "Все доступные вкладки активны в навигации.";
            return;
        }

        TxtVaultEmpty.Visibility = Visibility.Collapsed;

        foreach (var tab in _vaultTabs)
        {
            var rowBorder = new Border
            {
                Background = (SolidColorBrush)FindResource("BrushBgBase"),
                BorderBrush = (SolidColorBrush)FindResource("BrushBorderCard"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left + button
            var btnAdd = new Button
            {
                Style = (Style)FindResource("BtnPill"),
                Tag = tab,
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
                Fill = (SolidColorBrush)FindResource("BrushTextPrimary"),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnAdd.Content = plusPath;
            btnAdd.Click += BtnRestoreTab_Click;
            Grid.SetColumn(btnAdd, 0);
            grid.Children.Add(btnAdd);

            // Center: Icon + Name
            var infoSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (FindResource(tab.IconKey) is Geometry geom)
            {
                var iconPath = new System.Windows.Shapes.Path
                {
                    Data = geom,
                    Width = 14,
                    Height = 14,
                    Fill = (SolidColorBrush)FindResource("BrushTextPrimary"),
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                infoSp.Children.Add(iconPath);
            }
            var nameTb = new TextBlock
            {
                Text = isEn ? tab.EnTitle : tab.RuTitle,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("BrushTextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            };
            infoSp.Children.Add(nameTb);
            Grid.SetColumn(infoSp, 1);
            grid.Children.Add(infoSp);

            // Right label
            var hintTb = new TextBlock
            {
                Text = isEn ? "Restore to sidebar" : "Вернуть в меню",
                FontSize = 11,
                Foreground = (SolidColorBrush)FindResource("BrushTextMuted"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hintTb, 2);
            grid.Children.Add(hintTb);

            rowBorder.Child = grid;
            VaultItemsContainer.Children.Add(rowBorder);
        }
    }

    private async Task<bool> ShowModernConfirmDialogAsync(string title, string message, string confirmText, string cancelText, string iconResource = "IconInfo")
    {
        ModalDialogTitle.Text = title;
        ModalDialogMessage.Text = message;
        BtnModalConfirm.Content = confirmText;
        BtnModalCancel.Content = cancelText;

        if (FindResource(iconResource) is Geometry geom)
        {
            ModalIconPath.Data = geom;
        }

        ModernModalOverlay.Opacity = 0;
        ModernModalOverlay.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
        ModernModalOverlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        _modalTcs = new TaskCompletionSource<bool>();
        bool result = await _modalTcs.Task;

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140));
        fadeOut.Completed += (s, e) =>
        {
            ModernModalOverlay.Visibility = Visibility.Collapsed;
        };
        ModernModalOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);

        return result;
    }

    private void BtnModalConfirm_Click(object sender, RoutedEventArgs e)
    {
        _modalTcs?.TrySetResult(true);
    }

    private void BtnModalCancel_Click(object sender, RoutedEventArgs e)
    {
        _modalTcs?.TrySetResult(false);
    }

    #endregion

    #region RAM & Turbo Handlers

    private async void BtnQuickPurge_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isCpuPurgeMode)
                await ExecuteCpuOptimization();
            else
                ExecuteRamPurge();
        }
        catch (Exception ex)
        {
            Logger.Error("Quick purge error", ex);
        }
    }

    private async void BtnFullPurge_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isCpuPurgeMode)
                await ExecuteCpuOptimization();
            else
                ExecuteRamPurge();
        }
        catch (Exception ex)
        {
            Logger.Error("Full purge error", ex);
        }
    }

    private void ExecuteRamPurge()
    {
        if (_ramCooldownRemaining > 0) return;

        bool isEn = _currentLanguage == "EN";

        BtnFullPurge.IsEnabled = false;
        StatusFooterText.Text = isEn ? "Trimming process working sets..." : "Выполняется сброс рабочих наборов процессов...";

        try
        {
            var res = MemoryService.PurgeMemory();
            string freedStr = StorageService.FormatBytes(res.BytesFreed);
            string nowTime = FormatTimeLog(DateTime.Now);
            string logMsg = isEn
                ? $"[{nowTime}] Purged: {freedStr} physical RAM ({res.ProcessesOptimized} processes optimized). Was: {res.UsedPercentBefore:0}%, now: {res.UsedPercentAfter:0}%."
                : $"[{nowTime}] Очищено: {freedStr} физической памяти ({res.ProcessesOptimized} процессов оптимизировано). Было: {res.UsedPercentBefore:0}%, стало: {res.UsedPercentAfter:0}%.";
            LogRam(logMsg);
            StatusFooterText.Text = isEn ? $"Cleanup completed. Freed {freedStr} RAM." : $"Очистка завершена. Высвобождено {freedStr} RAM.";
        }
        catch (Exception ex)
        {
            LogRam(isEn ? $"[{FormatTimeLog(DateTime.Now)}] Error: {ex.Message}" : $"[{FormatTimeLog(DateTime.Now)}] Ошибка: {ex.Message}");
            StatusFooterText.Text = isEn ? "Memory purge failed." : "Ошибка при очистке памяти.";
        }
        finally
        {
            UpdateTelemetry();
            StartRamCooldown(3);
        }
    }

    private async Task ExecuteCpuOptimization()
    {
        if (_ramCooldownRemaining > 0) return;

        bool isEn = _currentLanguage == "EN";
        BtnFullPurge.IsEnabled = false;
        StatusFooterText.Text = isEn ? "Optimizing CPU thread scheduling and background load..." : "Оптимизация очередей ЦП и фоновой нагрузки...";

        try
        {
            double cpuBefore = HardwareTelemetry.Sample().CpuLoadPercent;
            int optimizedCount = 0;

            await Task.Run(() =>
            {
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.Id == 0 || p.Id == 4) continue;
                        string pName = p.ProcessName.ToLowerInvariant();
                        if (pName.Contains("onedrive") || pName.Contains("dropbox") || pName.Contains("googledrivesync") ||
                            pName.Contains("searchindexer") || pName.Contains("compattelrunner") || pName.Contains("mscorsvw"))
                        {
                            if (p.PriorityClass > ProcessPriorityClass.BelowNormal)
                            {
                                p.PriorityClass = ProcessPriorityClass.BelowNormal;
                                optimizedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Process priority adjustment skipped for {p.ProcessName}: {ex.Message}");
                    }
                    finally { p.Dispose(); }
                }
            });

            await Task.Delay(500);
            HardwareTelemetry.Sample(); // Reset sample baseline after intensive process scan
            await Task.Delay(800);
            double cpuAfter = HardwareTelemetry.Sample().CpuLoadPercent;

            string nowTime = FormatTimeLog(DateTime.Now);
            string logMsg;
            if (cpuAfter < cpuBefore)
            {
                logMsg = isEn
                    ? $"[{nowTime}] CPU Optimization completed. Balanced background tasks ({optimizedCount} adjusted). CPU Load: was {cpuBefore:0}%, now {cpuAfter:0}%."
                    : $"[{nowTime}] Оптимизация ЦП завершена. Нормализованы фоновые процессы (скорректировано {optimizedCount}). Загрузка ЦП: было {cpuBefore:0}%, стало {cpuAfter:0}%.";
                StatusFooterText.Text = isEn ? "CPU optimization completed." : "Оптимизация ЦП завершена.";
            }
            else
            {
                logMsg = isEn
                    ? $"[{nowTime}] CPU Optimization completed. Balanced background tasks ({optimizedCount} adjusted). CPU load did not change (was {cpuBefore:0}%, now {cpuAfter:0}%)."
                    : $"[{nowTime}] Оптимизация ЦП завершена. Нормализованы фоновые процессы (скорректировано {optimizedCount}). Загрузка ЦП не изменилась (было {cpuBefore:0}%, стало {cpuAfter:0}%).";
                StatusFooterText.Text = isEn ? "CPU load did not change." : "Загрузка ЦП не изменилась.";
            }
            LogRam(logMsg);
        }
        catch (Exception ex)
        {
            LogRam(isEn ? $"[{FormatTimeLog(DateTime.Now)}] CPU optimization error: {ex.Message}" : $"[{FormatTimeLog(DateTime.Now)}] Ошибка оптимизации ЦП: {ex.Message}");
            StatusFooterText.Text = isEn ? "CPU optimization failed." : "Ошибка при оптимизации ЦП.";
        }
        finally
        {
            UpdateTelemetry();
            StartRamCooldown(3);
        }
    }

    private void StartRamCooldown(int seconds)
    {
        bool isEn = _currentLanguage == "EN";
        _ramCooldownRemaining = seconds;

        if (BtnFullPurge != null) BtnFullPurge.IsEnabled = false;

        if (BtnFullPurgeText != null)
        {
            BtnFullPurgeText.Text = isEn ? $"Cooldown ({_ramCooldownRemaining}s)" : $"Ожидание ({_ramCooldownRemaining}с)...";
        }

        _ramCooldownTimer?.Stop();
        _ramCooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ramCooldownTimer.Tick += (s, ev) =>
        {
            _ramCooldownRemaining--;
            if (_ramCooldownRemaining > 0)
            {
                if (BtnFullPurgeText != null)
                {
                    BtnFullPurgeText.Text = isEn ? $"Cooldown ({_ramCooldownRemaining}s)" : $"Ожидание ({_ramCooldownRemaining}с)...";
                }
            }
            else
            {
                _ramCooldownTimer?.Stop();
                _ramCooldownRemaining = 0;
                if (BtnFullPurge != null) BtnFullPurge.IsEnabled = true;
                UpdatePurgeTabVisuals();
            }
        };
        _ramCooldownTimer.Start();
    }

    private void BtnSwitchPurgeMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        bool isEn = _currentLanguage == "EN";

        var cm = new ContextMenu();
        var miRam = new MenuItem
        {
            Header = isEn ? "RAM Purge & Optimization" : "Очистка оперативной памяти",
            FontWeight = !_isCpuPurgeMode ? FontWeights.Bold : FontWeights.Normal
        };
        miRam.Click += (s, ev) => SetPurgeTabMode(isCpu: false);

        var miCpu = new MenuItem
        {
            Header = isEn ? "CPU Load Optimization" : "Оптимизация процессора (ЦП)",
            FontWeight = _isCpuPurgeMode ? FontWeights.Bold : FontWeights.Normal
        };
        miCpu.Click += (s, ev) => SetPurgeTabMode(isCpu: true);

        cm.Items.Add(miRam);
        cm.Items.Add(miCpu);
        cm.PlacementTarget = btn;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    public void SetPurgeTabMode(bool isCpu)
    {
        _isCpuPurgeMode = isCpu;
        var tab = _activeTabs.Concat(_vaultTabs).FirstOrDefault(t => t.Key == "ram");
        if (tab != null)
        {
            if (_isCpuPurgeMode)
            {
                tab.EnTitle = "CPU Optimization";
                tab.RuTitle = "Оптимизация ЦП";
                tab.IconKey = "IconCpu";
            }
            else
            {
                tab.EnTitle = "Memory Purge";
                tab.RuTitle = "Очистка ОЗУ";
                tab.IconKey = "IconRam";
            }
        }
        RebuildNavPills();
        UpdatePurgeTabVisuals();
        UpdateTelemetry();
        SaveCurrentUserSettings();
    }

    private void UpdatePurgeTabVisuals()
    {
        bool isEn = _currentLanguage == "EN";
        if (!_isCpuPurgeMode)
        {
            if (RamTabHeading != null) RamTabHeading.Text = isEn ? "Physical Memory Optimization" : "Очистка оперативной памяти";
            if (RamTabSub != null) RamTabSub.Text = isEn ? "Working set trimming and process priority acceleration" : "Управление распределением физической памяти и приоритетом процессов";
            if (RamHeroIcon != null) RamHeroIcon.Data = (Geometry)FindResource("IconRam");
            if (RamBannerTitle != null) RamBannerTitle.Text = isEn ? "Working Set Memory Trimming" : "Выгрузка рабочих наборов физической памяти";
            if (RamBannerSub != null) RamBannerSub.Text = isEn
                ? "Safely returns unused pages from background processes to physical memory, preventing pagefile inflation."
                : "Безопасно возвращает неиспользуемые страницы фоновых приложений из физической памяти, предотвращая заполнение файла подкачки.";
            if (BtnFullPurgeText != null) BtnFullPurgeText.Text = isEn ? "Purge Memory Now" : "Очистить память сейчас";
            if (BtnFullPurgeIcon != null) BtnFullPurgeIcon.Data = (Geometry)FindResource("IconBroom");
            if (RamDetailHeader != null) RamDetailHeader.Text = isEn ? "MEMORY SPECIFICATION" : "ДЕТАЛИЗАЦИЯ ПАМЯТИ";
            if (RamTotalLabel != null) RamTotalLabel.Text = isEn ? "Total RAM:" : "Всего ОЗУ:";
            if (RamUsedLabel != null) RamUsedLabel.Text = isEn ? "In Use:" : "Используется:";
            if (RamAvailLabel != null) RamAvailLabel.Text = isEn ? "Available:" : "Свободно:";
            if (RamLogTitle != null) RamLogTitle.Text = isEn ? "PURGE ACTIVITY LOG" : "ЖУРНАЛ ОПЕРАЦИЙ ОЧИСТКИ";
        }
        else
        {
            if (RamTabHeading != null) RamTabHeading.Text = isEn ? "Processor (CPU) Optimization" : "Оптимизация процессора (ЦП)";
            if (RamTabSub != null) RamTabSub.Text = isEn ? "Thread rebalancing, background load reduction and process normalization" : "Снижение фоновой нагрузки, нормализация приоритетов процессов и потоков";
            if (RamHeroIcon != null) RamHeroIcon.Data = (Geometry)FindResource("IconCpu");
            if (RamBannerTitle != null) RamBannerTitle.Text = isEn ? "CPU Scheduling & Background Load" : "Оптимизация нагрузки процессора";
            if (RamBannerSub != null) RamBannerSub.Text = isEn
                ? "Normalizes runaway background threads and reduces thread scheduling overhead to optimize CPU responsiveness."
                : "Снижает нагрузку от фоновых процессов, нормализует дескрипторы и снижает задержки очередей ЦП.";
            if (BtnFullPurgeText != null) BtnFullPurgeText.Text = isEn ? "Optimize CPU Now" : "Оптимизировать ЦП";
            if (BtnFullPurgeIcon != null) BtnFullPurgeIcon.Data = (Geometry)FindResource("IconCpu");
            if (RamDetailHeader != null) RamDetailHeader.Text = isEn ? "CPU SPECIFICATION" : "СПЕЦИФИКАЦИЯ ПРОЦЕССОРА";
            if (RamTotalLabel != null) RamTotalLabel.Text = isEn ? "Model:" : "Модель:";
            int cores = HardwareTelemetry.GetPhysicalCoreCount();
            if (RamUsedLabel != null)
                RamUsedLabel.Text = isEn
                    ? (cores > 0 ? "Cores / Threads:" : "Logical Threads:")
                    : (cores > 0 ? "Ядра / Потоки:" : "Логические потоки:");
            if (RamAvailLabel != null) RamAvailLabel.Text = isEn ? "Current Load:" : "Текущая нагрузка:";
            if (RamLogTitle != null) RamLogTitle.Text = isEn ? "CPU OPTIMIZATION LOG" : "ЖУРНАЛ ОПТИМИЗАЦИИ ЦП";
        }
        if (BtnRamOpenInspectorText != null) BtnRamOpenInspectorText.Text = isEn ? "Open Task Manager" : "Открыть диспетчер задач";
    }

    private void BtnOpenTaskManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string taskmgrPath = Path.Combine(Environment.SystemDirectory, "taskmgr.exe");
            Process.Start(new ProcessStartInfo(taskmgrPath) { UseShellExecute = true });
            StatusFooterText.Text = _currentLanguage == "EN" ? "Windows Task Manager launched." : "Диспетчер задач запущен.";
        }
        catch (Exception ex)
        {
            StatusFooterText.Text = $"Task Manager launch error: {ex.Message}";
        }
    }

    private void BtnBoostGamePriority_Click(object sender, RoutedEventArgs e)
    {
        bool isEn = _currentLanguage == "EN";
        var result = GameSentinelService.BoostGames(!isEn);
        string nowTime = FormatTimeLog(DateTime.Now);

        if (result.BoostedCount > 0)
        {
            string gamesStr = string.Join(", ", result.AffectedGames);
            LogRam(isEn ? $"[{nowTime}] Elevated priority to High for {result.BoostedCount} process(es) ({gamesStr})."
                        : $"[{nowTime}] Повышен приоритет до Высокого для {result.BoostedCount} процессов ({gamesStr}).");
            StatusFooterText.Text = isEn ? $"Game Booster: elevated priority for {result.BoostedCount} process(es)."
                                         : $"Game Booster: повышен приоритет для {result.BoostedCount} процессов.";
        }
        else if (result.AlreadyHighCount > 0)
        {
            string gamesStr = string.Join(", ", result.AffectedGames);
            LogRam(isEn ? $"[{nowTime}] Game '{gamesStr}' is already running at High priority."
                        : $"[{nowTime}] Игра '{gamesStr}' уже работает с высоким приоритетом.");
            StatusFooterText.Text = isEn ? $"Game already at High priority: {gamesStr}."
                                         : $"Игра уже с высоким приоритетом: {gamesStr}.";
        }
        else
        {
            LogRam(isEn ? $"[{nowTime}] Active game processes not detected."
                        : $"[{nowTime}] Активные игровые процессы не обнаружены.");
            StatusFooterText.Text = isEn ? "No active game processes detected."
                                         : "Активные игровые процессы не обнаружены.";
        }

        RefreshGamesTabActiveList();
    }

    #region Games Tab Handlers

    private void RefreshGamesTabActiveList()
    {
        bool isEn = _currentLanguage == "EN";
        var activeGames = GameSentinelService.GetActiveGames(!isEn);
        ListActiveGames.ItemsSource = activeGames;
        TxtNoActiveGames.Visibility = activeGames.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnRefreshActiveGames_Click(object sender, RoutedEventArgs e)
    {
        RefreshGamesTabActiveList();
    }

    private void BtnToggleLaunchers_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        bool isEn = _currentLanguage == "EN";

        var cm = new ContextMenu();
        var miToggle = new MenuItem
        {
            Header = isEn ? "Scan Minecraft Launchers (TLauncher, etc.)" : "Искать лаунчеры Майнкрафт (TLauncher и др.)",
            IsCheckable = true,
            IsChecked = _includeLaunchers
        };
        miToggle.Click += (s, ev) =>
        {
            _includeLaunchers = miToggle.IsChecked;
            UpdateLauncherToggleVisual();
            BtnScanGames_Click(this, new RoutedEventArgs());
        };
        cm.Items.Add(miToggle);
        cm.PlacementTarget = btn;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private void UpdateLauncherToggleVisual()
    {
        bool isEn = _currentLanguage == "EN";
        if (IconToggleLaunchers != null)
        {
            IconToggleLaunchers.Fill = (SolidColorBrush)FindResource(_includeLaunchers ? "BrushPrimary" : "BrushTextSecondary");
        }
        if (BtnToggleLaunchers != null)
        {
            BtnToggleLaunchers.ToolTip = _includeLaunchers
                ? (isEn ? "Minecraft launchers included in scan" : "Поиск лаунчеров Майнкрафт включен")
                : (isEn ? "Minecraft launchers excluded from scan (Click to toggle)" : "Лаунчеры Майнкрафт исключены из поиска (Нажмите для настройки)");
        }
    }

    private void BtnGameDriveFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        bool isEn = _currentLanguage == "EN";
        var cm = new ContextMenu();

        var allItem = new MenuItem
        {
            Header = isEn ? "All Ready Drives" : "Все доступные диски",
            FontWeight = _gameDriveFilter == "ALL" ? FontWeights.Bold : FontWeights.Normal
        };
        allItem.Click += (s, ev) =>
        {
            _gameDriveFilter = "ALL";
            TxtGameDriveFilter.Text = isEn ? "All Drives" : "Все диски";
        };
        cm.Items.Add(allItem);

        var drives = DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable));
        foreach (var d in drives)
        {
            string letter = d.Name.TrimEnd('\\', ':');
            var item = new MenuItem
            {
                Header = isEn ? $"Drive {letter}:" : $"Диск {letter}:",
                FontWeight = _gameDriveFilter.Equals(letter, StringComparison.OrdinalIgnoreCase) ? FontWeights.Bold : FontWeights.Normal
            };
            item.Click += (s, ev) =>
            {
                _gameDriveFilter = letter;
                TxtGameDriveFilter.Text = isEn ? $"Drive {letter}:" : $"Диск {letter}:";
            };
            cm.Items.Add(item);
        }

        cm.PlacementTarget = btn;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private async void BtnScanGames_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanningGames) return;
        _isScanningGames = true;
        BtnScanGames.IsEnabled = false;
        bool isEn = _currentLanguage == "EN";

        InstalledScanStatusText.Text = isEn 
            ? $"Scanning storage ({_gameDriveFilter})..." 
            : $"Сканирование накопителей ({_gameDriveFilter})...";
        InstalledScanCountText.Text = "";

        try
        {
            _installedGames = await GameSentinelService.ScanInstalledGamesAsync(_gameDriveFilter, !isEn, _includeLaunchers);
            ListInstalledGames.ItemsSource = _installedGames;
            TxtScanPlaceholder.Visibility = _installedGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            long totalBytes = _installedGames.Sum(g => g.SizeBytes);
            string szFormatted = StorageService.FormatBytes(totalBytes);

            InstalledScanCountText.Text = isEn 
                ? $"{_installedGames.Count} games ({szFormatted})" 
                : $"{_installedGames.Count} игр ({szFormatted})";
            InstalledScanStatusText.Text = isEn 
                ? $"Scan completed: {_installedGames.Count} game directories found." 
                : $"Сканирование завершено: найдено каталогов: {_installedGames.Count}.";
            StatusFooterText.Text = isEn 
                ? $"Game library scan complete: {_installedGames.Count} items." 
                : $"Сканирование каталога игр завершено: найдено {_installedGames.Count}.";
        }
        catch (Exception ex)
        {
            Logger.Error("Game scan failed", ex);
            InstalledScanStatusText.Text = isEn ? $"Scan failed: {ex.Message}" : $"Ошибка сканирования: {ex.Message}";
        }
        finally
        {
            _isScanningGames = false;
            BtnScanGames.IsEnabled = true;
        }
    }

    private void BtnOpenGameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path && Directory.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusFooterText.Text = $"Failed to open folder: {ex.Message}";
            }
        }
    }

    #endregion

    private void LogRam(string line)
    {
        RamLogTextBox.AppendText(line + Environment.NewLine);
        RamLogTextBox.ScrollToEnd();
    }

    #endregion

    #region Storage Sentinel Handlers

    private record CleanTargetItem(string Name, string Path, string FormattedSize, long Bytes);

    private void InitStorageTab()
    {
        _ = ScanJunkAsync(showLog: false);
    }

    private async void BtnScanJunk_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ScanJunkAsync(showLog: true, isManualClick: true);
        }
        catch (Exception ex)
        {
            Logger.Error("Scan junk button click failed", ex);
        }
    }

    private void LogStorage(string msg)
    {
        Dispatcher.Invoke(() =>
        {
            if (StorageLogTextBox == null) return;
            string time = FormatTimeLog(DateTime.Now);
            string line = $"[{time}] {msg}";
            if (string.IsNullOrWhiteSpace(StorageLogTextBox.Text))
                StorageLogTextBox.Text = line;
            else
                StorageLogTextBox.AppendText(Environment.NewLine + line);

            StorageLogScroll?.ScrollToEnd();
            if (StorageLogFooter != null)
            {
                StorageLogFooter.Text = $"Last: {time}";
            }
        });
    }

    private void BtnClearStorageLog_Click(object sender, RoutedEventArgs e)
    {
        if (StorageLogTextBox != null) StorageLogTextBox.Text = string.Empty;
        if (StorageLogFooter != null) StorageLogFooter.Text = _currentLanguage == "EN" ? "Console cleared" : "Консоль очищена";
    }

    private async Task PerformScanCoreAsync(bool showLog = false)
    {
        bool isEn = _currentLanguage == "EN";

        if (showLog)
        {
            LogStorage(isEn ? "--> Starting scan for cleanable cache targets..." : "--> Сканирование безопасных целей очистки...");
            StatusFooterText.Text = isEn ? "Scanning temporary junk files..." : "Сканирование временных файлов...";
        }

        try
        {
            var targets = await Task.Run(() => StorageService.ScanTempLocations(isEn));

            var displayItems = targets.Select(t => new CleanTargetItem(
                t.Name,
                t.Path,
                StorageService.FormatBytes(t.Bytes),
                t.Bytes
            )).ToList();

            CleanTargetsList.ItemsSource = displayItems;
            long totalBytes = targets.Sum(t => t.Bytes);

            bool isAdmin = StorageService.IsAdministrator();
            if (BtnRelaunchAdmin != null)
            {
                BtnRelaunchAdmin.Visibility = !isAdmin ? Visibility.Visible : Visibility.Collapsed;
            }

            if (showLog)
            {
                foreach (var t in targets)
                {
                    if (t.Bytes > 0)
                    {
                        LogStorage($"    • {t.Name}: {StorageService.FormatBytes(t.Bytes)}");
                    }
                }

                string summary = isEn
                    ? $"[OK] Scan finished. Total reclaimable: {StorageService.FormatBytes(totalBytes)} ({targets.Count} locations)."
                    : $"[OK] Сканирование завершено. Доступно для очистки: {StorageService.FormatBytes(totalBytes)} ({targets.Count} расположений).";
                LogStorage(summary);
                StatusFooterText.Text = summary;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Storage scan error", ex);
            LogStorage($"[ERROR] Scan error: {ex.Message}");
        }
    }

    private async Task ScanJunkAsync(bool showLog = true, bool isManualClick = false)
    {
        if (_isStorageOperationRunning) return;
        if (isManualClick && _storageCooldownRemaining > 0) return;

        if (BtnScanJunk != null) BtnScanJunk.IsEnabled = false;
        if (BtnCleanSystem != null && _storageCooldownRemaining <= 0) BtnCleanSystem.IsEnabled = false;

        try
        {
            await PerformScanCoreAsync(showLog);
        }
        finally
        {
            if (_storageCooldownRemaining <= 0)
            {
                if (BtnScanJunk != null) BtnScanJunk.IsEnabled = true;
                if (BtnCleanSystem != null) BtnCleanSystem.IsEnabled = true;
            }
        }
    }

    private async void BtnCleanSystem_Click(object sender, RoutedEventArgs e)
    {
        if (_isStorageOperationRunning || _storageCooldownRemaining > 0) return;

        bool isEn = _currentLanguage == "EN";
        _isStorageOperationRunning = true;

        if (BtnCleanSystem != null) BtnCleanSystem.IsEnabled = false;
        if (BtnScanJunk != null) BtnScanJunk.IsEnabled = false;

        if (BtnCleanSystemText != null)
        {
            BtnCleanSystemText.Text = isEn ? "Cleaning..." : "Очистка...";
        }

        LogStorage(isEn ? "====== SYSTEM CACHE CLEANUP INITIATED ======" : "====== ЗАПУСК ОЧИСТКИ СИСТЕМНОГО КЭША ======");
        StatusFooterText.Text = isEn ? "Starting system cleanup..." : "Запуск системной очистки...";

        try
        {
            var res = await StorageService.ExecuteDeepCleanupAsync(isEn, stepMsg =>
            {
                LogStorage(stepMsg);
                StatusFooterText.Text = stepMsg;
            });

            if (res.AdminSkippedStages > 0 || res.FilesSkipped > 0 || res.HasErrors)
            {
                StatusFooterText.Text = isEn
                    ? $"Cleanup finished with warnings. Freed {StorageService.FormatBytes(res.BytesFreed)} (admin skipped: {res.AdminSkippedStages}, locked: {res.FilesSkipped})."
                    : $"Очистка выполнена с ограничениями. Освобождено {StorageService.FormatBytes(res.BytesFreed)} (без прав: {res.AdminSkippedStages}, занято: {res.FilesSkipped}).";
            }
            else
            {
                StatusFooterText.Text = isEn
                    ? $"Cleanup completed. Freed {StorageService.FormatBytes(res.BytesFreed)}."
                    : $"Очистка завершена. Освобождено {StorageService.FormatBytes(res.BytesFreed)}.";
            }

            // Directly invoke core rescan to immediately refresh UI without getting blocked by operation flag
            await PerformScanCoreAsync(showLog: false);
            UpdateTelemetry();
        }
        catch (Exception ex)
        {
            Logger.Error("System cleanup exception", ex);
            LogStorage($"[ERROR] Cleanup exception: {ex.Message}");
            StatusFooterText.Text = $"Cleanup error: {ex.Message}";
        }
        finally
        {
            _isStorageOperationRunning = false;
            StartStorageCooldown(5);
        }
    }

    private void BtnRelaunchAdmin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                exePath = System.IO.Path.Combine(AppContext.BaseDirectory, "CoreSentinel.exe");
            }

            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            bool isEn = _currentLanguage == "EN";
            StatusFooterText.Text = isEn ? "Administrator elevation cancelled." : "Запуск от имени администратора отменен.";
        }
        catch (Exception ex)
        {
            StatusFooterText.Text = $"Elevation error: {ex.Message}";
        }
    }

    private void BtnRollbackSystem_Click(object sender, RoutedEventArgs e)
    {
        bool isEn = _currentLanguage == "EN";
        bool launched = StorageService.LaunchSystemRestore();
        if (launched)
        {
            StatusFooterText.Text = isEn ? "Windows System Restore wizard launched." : "Мастер восстановления системы Windows запущен.";
        }
        else
        {
            StatusFooterText.Text = isEn ? "Could not launch System Restore wizard." : "Не удалось запустить мастер восстановления системы.";
        }
    }

    private void StartStorageCooldown(int seconds)
    {
        bool isEn = _currentLanguage == "EN";
        _storageCooldownRemaining = seconds;

        if (BtnCleanSystem != null) BtnCleanSystem.IsEnabled = false;
        if (BtnScanJunk != null) BtnScanJunk.IsEnabled = false;

        if (BtnCleanSystemText != null)
        {
            BtnCleanSystemText.Text = isEn ? $"Cooldown ({_storageCooldownRemaining}s)" : $"Ожидание ({_storageCooldownRemaining}с)...";
        }

        _storageCooldownTimer?.Stop();
        _storageCooldownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _storageCooldownTimer.Tick += (s, ev) =>
        {
            _storageCooldownRemaining--;
            if (_storageCooldownRemaining > 0)
            {
                if (BtnCleanSystemText != null)
                {
                    BtnCleanSystemText.Text = isEn ? $"Cooldown ({_storageCooldownRemaining}s)" : $"Ожидание ({_storageCooldownRemaining}с)...";
                }
            }
            else
            {
                _storageCooldownTimer?.Stop();
                _storageCooldownRemaining = 0;
                if (BtnCleanSystem != null) BtnCleanSystem.IsEnabled = true;
                if (BtnScanJunk != null) BtnScanJunk.IsEnabled = true;
                if (BtnCleanSystemText != null)
                {
                    BtnCleanSystemText.Text = isEn ? "Clean System Cache" : "Очистить систему";
                }
                if (BtnScanJunkText != null)
                {
                    BtnScanJunkText.Text = isEn ? "Scan Cache" : "Сканировать кэш";
                }
            }
        };
        _storageCooldownTimer.Start();
    }

    #endregion

    #region Settings Handlers & Accordions

    private void InitSettingsTab()
    {
        UpdateAutoRamVisual();
        UpdateAutoGamePriorityVisual();
        UpdateTimeFormatVisual();
        LoadStartupEntries();
        LoadNetworkAdapters();

        if (ExpCustom != null)
        {
            ExpCustom.Collapsed += (s, e) => DisableTabEditMode();
        }
    }

    // Smooth Accordion Expand & Collapse Handlers
    private void Expander_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Expander exp && exp.Content is FrameworkElement content)
        {
            if (e.OriginalSource is DependencyObject dep && FindVisualParent<System.Windows.Controls.Primitives.ToggleButton>(dep) != null)
            {
                if (_accordionCollapsing.TryGetValue(exp, out bool isCollapsing) && isCollapsing)
                {
                    // Was collapsing, user clicked again -> reverse and expand cleanly!
                    e.Handled = true;
                    _accordionCollapsing[exp] = false;
                    AnimateExpand(exp, content);
                    return;
                }

                if (exp.IsExpanded)
                {
                    e.Handled = true; // Intercept abrupt collapse
                    _accordionCollapsing[exp] = true;
                    AnimateCollapse(exp, content);
                }
            }
        }
    }

    private void AnimateCollapse(Expander exp, FrameworkElement content)
    {
        if (exp == ExpCustom)
        {
            DisableTabEditMode();
        }

        _accordionCollapsing[exp] = true;
        int animToken = Environment.TickCount;
        exp.Tag = animToken;

        // Cancel running animations immediately
        content.BeginAnimation(FrameworkElement.HeightProperty, null);
        content.BeginAnimation(UIElement.OpacityProperty, null);

        double startH = content.ActualHeight;
        if (startH <= 1)
        {
            content.Height = double.NaN;
            content.Opacity = 1;
            exp.IsExpanded = false;
            _accordionCollapsing[exp] = false;
            CheckStickyAccordionOnScroll();
            return;
        }

        content.Height = startH;
        var animH = new DoubleAnimation(startH, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        var animO = new DoubleAnimation(Math.Max(0, content.Opacity), 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

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
                CheckStickyAccordionOnScroll();
            }
        };

        content.BeginAnimation(FrameworkElement.HeightProperty, animH);
        content.BeginAnimation(UIElement.OpacityProperty, animO);
    }

    private void Expander_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander exp && exp.Content is FrameworkElement content)
        {
            _accordionCollapsing[exp] = false;
            AnimateExpand(exp, content);
            CheckStickyAccordionOnScroll();
        }
    }

    private void SettingsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        CheckStickyAccordionOnScroll();
    }

    private void SettingsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            SettingsScrollViewer.Clip = new RectangleGeometry(
                new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 14, 14);
        }
    }

    private void StickyAccordionBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SettingsScrollViewer != null)
        {
            SettingsScrollViewer.ScrollToVerticalOffset(SettingsScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void CheckStickyAccordionOnScroll()
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
            // Calculate relative offset of active expander to SettingsScrollViewer
            GeneralTransform transform = activeExp.TransformToAncestor(SettingsScrollViewer);
            Point pos = transform.Transform(new Point(0, 0));

            // Main header height is ~50px. When pos.Y + 46 <= 0, the main accordion header has scrolled out of view!
            double bottomY = pos.Y + activeExp.ActualHeight;
            bool mainHeaderScrolledOff = (pos.Y + 46 <= 0) && (bottomY > 80);

            if (mainHeaderScrolledOff)
            {
                StickyAccordionTitle.Text = activeExp.Header?.ToString() ?? "Settings Section";
                if (StickyAccordionBar.Visibility != Visibility.Visible)
                {
                    StickyAccordionBar.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (StickyAccordionBar.Visibility != Visibility.Collapsed)
                {
                    StickyAccordionBar.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch
        {
            StickyAccordionBar.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnCollapseSticky_Click(object sender, RoutedEventArgs e)
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

    private async void BtnResetSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool isEn = _currentLanguage == "EN";
            string msg = isEn 
                ? "Are you sure you want to reset all parameters, alert thresholds and tab layouts to factory defaults?" 
                : "Вы уверены, что хотите сбросить все параметры, пороги алертов и навигационные вкладки на значения по умолчанию?";
            string title = isEn ? "Reset Confirmation" : "Подтверждение сброса";
            string confirm = isEn ? "Reset" : "Сбросить";
            string cancel = isEn ? "Cancel" : "Отмена";

            bool ok = await ShowModernConfirmDialogAsync(title, msg, confirm, cancel, "IconArrowClockwise");
            if (!ok) return;

            SetLanguage("EN");
            SetTheme(false);
            SetTelemetryInterval(1.5);
            SetDiskThreshold(7.0);

            _autoRamPurgeEnabled = true;
            UpdateAutoRamVisual();

            _autoGamePriorityEnabled = true;
            UpdateAutoGamePriorityVisual();

            if (SliderHudOpacity != null) SliderHudOpacity.Value = 90;
            if (TxtHudOpacityVal != null) TxtHudOpacityVal.Text = "90%";
            if (_hudWindow != null && _hudWindow.IsLoaded) _hudWindow.Opacity = 0.9;

            if (ExpCustom != null && ExpCustom.IsExpanded && ExpCustom.Content is FrameworkElement c1) AnimateCollapse(ExpCustom, c1);
            if (ExpMonitor != null && ExpMonitor.IsExpanded && ExpMonitor.Content is FrameworkElement c2) AnimateCollapse(ExpMonitor, c2);
            if (ExpSysInfo != null && ExpSysInfo.IsExpanded && ExpSysInfo.Content is FrameworkElement c3) AnimateCollapse(ExpSysInfo, c3);
            if (StickyAccordionBar != null) StickyAccordionBar.Visibility = Visibility.Collapsed;
            SettingsScrollViewer?.ScrollToTop();

            _use12HourTime = true;
            UpdateTimeFormatVisual();

            ResetTabsToDefaults();

            StatusFooterText.Text = "Settings have been reset to factory defaults.";
        }
        catch (Exception ex)
        {
            Logger.Error("Reset settings failed", ex);
        }
    }

    private void AnimateExpand(Expander exp, FrameworkElement content)
    {
        _accordionCollapsing[exp] = false;
        int animToken = Environment.TickCount;
        exp.Tag = animToken;

        // Cancel running animations immediately
        content.BeginAnimation(FrameworkElement.HeightProperty, null);
        content.BeginAnimation(UIElement.OpacityProperty, null);

        // Measure clean desired height
        content.Height = double.NaN;
        double availWidth = exp.ActualWidth > 40 ? exp.ActualWidth - 40 : 800;
        content.Measure(new Size(availWidth, double.PositiveInfinity));
        double target = content.DesiredSize.Height;
        if (target < 32) target = 32;

        // If reversing from a mid-collapse, take current height; otherwise start smoothly from 0!
        double startH = 0;
        if (content.ActualHeight > 0 && content.ActualHeight < target - 5)
        {
            startH = content.ActualHeight;
        }

        content.Height = startH;
        content.Opacity = startH > 0 ? Math.Max(0.1, content.Opacity) : 0;

        var animH = new DoubleAnimation(startH, target, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var animO = new DoubleAnimation(content.Opacity, 1.0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        animH.Completed += (s, ev) =>
        {
            if (exp.Tag is int token && token == animToken)
            {
                content.BeginAnimation(FrameworkElement.HeightProperty, null);
                content.BeginAnimation(UIElement.OpacityProperty, null);
                content.Height = double.NaN;
                content.Opacity = 1;
                CheckStickyAccordionOnScroll();
            }
        };

        content.BeginAnimation(FrameworkElement.HeightProperty, animH);
        content.BeginAnimation(UIElement.OpacityProperty, animO);
    }

    private void BtnTimeFormat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _use12HourTime = tag == "12";
            UpdateTimeFormatVisual();
        }
    }

    private void UpdateTimeFormatVisual()
    {
        var primaryStyle = (Style)FindResource("BtnPrimaryWhite");
        var pillStyle = (Style)FindResource("BtnPill");
        if (BtnTime12h != null) BtnTime12h.Style = _use12HourTime ? primaryStyle : pillStyle;
        if (BtnTime24h != null) BtnTime24h.Style = !_use12HourTime ? primaryStyle : pillStyle;
    }

    private string FormatTimeLog(DateTime dt) => _use12HourTime 
        ? dt.ToString("hh:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture)
        : dt.ToString("HH:mm:ss");

    private string FormatInstallDate(string dateStr)
    {
        if (DateTime.TryParse(dateStr, out var dt))
        {
            return _use12HourTime 
                ? dt.ToString("MM/dd/yyyy hh:mm tt", System.Globalization.CultureInfo.InvariantCulture)
                : dt.ToString("dd.MM.yyyy HH:mm");
        }
        return dateStr;
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T typed) return typed;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    // ACCORDION 1: Customization
    private void BtnLang_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string lang)
        {
            SetLanguage(lang);
        }
    }

    private void SetLanguage(string lang)
    {
        if (lang == _currentLanguage) return;
        _currentLanguage = lang;
        var primaryStyle = (Style)FindResource("BtnPrimaryWhite");
        var pillStyle = (Style)FindResource("BtnPill");

        if (BtnLangEn != null) BtnLangEn.Style = lang == "EN" ? primaryStyle : pillStyle;
        if (BtnLangRu != null) BtnLangRu.Style = lang == "RU" ? primaryStyle : pillStyle;

        ApplyLanguageStrings();
        _ = ScanJunkAsync(showLog: false);
        UpdateTelemetry();
    }

    private void ApplyLanguageStrings()
    {
        bool isEn = _currentLanguage == "EN";
        HardwareTelemetry.IsRussianLanguage = !isEn;

        // Navigation
        NavHeaderTitle.Text = isEn ? "NAVIGATION" : "НАВИГАЦИЯ";
        foreach (var tab in _activeTabs.Concat(_vaultTabs))
        {
            if (tab.Key == "storage")
            {
                tab.EnTitle = $"Storage ({_watchedDriveLetter}:)";
                tab.RuTitle = $"Хранилище ({_watchedDriveLetter}:)";
            }
        }
        RebuildNavPills();
        RefreshVaultUI();

        // Dashboard
        DashHeadingTitle.Text = isEn ? "System Overview" : "Обзор системы";
        DashHeadingSub.Text = isEn ? "Real-time hardware telemetry and background process monitoring" : "Мониторинг физических ресурсов и фоновых процессов в реальном времени";
        DashCardCpuTitle.Text = isEn ? "PROCESSOR (CPU)" : "ПРОЦЕССОР (CPU)";
        DashCardRamTitle.Text = isEn ? "PHYSICAL MEMORY" : "ОПЕРАТИВНАЯ ПАМЯТЬ";
        DashEnvCardTitle.Text = isEn ? "Gaming environment and storage status" : "Состояние игрового окружения и накопителей";
        DashGameStatusTitle.Text = isEn ? "Game Process Priority" : "Приоритет игровых процессов";

        // Dashboard Additional Controls
        if (DashHealthStatusTitle != null) DashHealthStatusTitle.Text = isEn ? "System Health Status" : "Статус здоровья системы";
        if (DashQuickActionsTitle != null) DashQuickActionsTitle.Text = isEn ? "Quick Actions" : "Быстрые действия";
        if (DashQuickPurgeRamText != null) DashQuickPurgeRamText.Text = isEn ? "Flush RAM Cache" : "Сбросить кэш ОЗУ";
        if (DashToggleHudText != null) DashToggleHudText.Text = isEn ? "Toggle HUD Overlay" : "Включить HUD Оверлей";
        if (DashCleanJunkText != null) DashCleanJunkText.Text = isEn ? "Clean Temporary Files" : "Очистить временные файлы";

        // ToolTips
        if (QuickCpuMetric != null) QuickCpuMetric.ToolTip = isEn ? "Open Windows Task Manager" : "Открыть диспетчер задач";
        if (QuickRamMetric != null) QuickRamMetric.ToolTip = isEn ? "RAM Usage" : "Использование ОЗУ";
        if (QuickDiskMetric != null) QuickDiskMetric.ToolTip = isEn ? "Open Drive in Windows Explorer" : "Открыть системный диск в Проводнике";
        if (BtnToggleHud != null) BtnToggleHud.ToolTip = isEn ? "Toggle compact floating HUD overlay" : "Открыть компактный плавающий оверлей HUD";
        if (BtnCpuAction != null) BtnCpuAction.ToolTip = isEn ? "Open Windows Task Manager" : "Открыть Диспетчер задач Windows";
        if (MetricCpuAction != null) MetricCpuAction.ToolTip = isEn ? "Open Windows Task Manager" : "Открыть Диспетчер задач Windows";
        if (BtnRamAction != null) BtnRamAction.ToolTip = isEn ? "Open Process Manager (sorted by RAM)" : "Открыть Диспетчер процессов (сортировка по ОЗУ)";
        if (MetricRamAction != null) MetricRamAction.ToolTip = isEn ? "Switch to Memory Purge tab" : "Перейти на вкладку Очистки ОЗУ";
        if (MetricCpuAction != null) MetricCpuAction.ToolTip = isEn ? "Switch to CPU Optimization tab" : "Перейти на вкладку Оптимизации ЦП";
        if (BtnDiskAction != null) BtnDiskAction.ToolTip = isEn ? "Open drive in Windows Explorer" : "Открыть накопитель в Проводнике";
        if (MetricDiskAction != null) MetricDiskAction.ToolTip = isEn ? "Open drive in Windows Explorer" : "Открыть накопитель в Проводнике";
        if (DashDiskDRow != null) DashDiskDRow.ToolTip = isEn ? "Click to open data drive in Windows Explorer" : "Нажмите, чтобы открыть диск данных в Проводнике";

        // RAM & CPU Purge Tab
        UpdatePurgeTabVisuals();
        UpdateLauncherToggleVisual();

        // Storage Tab
        StorageTabHeading.Text = isEn ? $"Storage & System Cleanup ({_watchedDriveLetter}:)" : $"Хранилище и системная очистка ({_watchedDriveLetter}:)";
        StorageTabSub.Text = isEn ? "Monitor drive capacity and safely remove temporary cache files" : "Контроль свободного места накопителей и безопасное удаление временных файлов";
        StorageGaugeTitle.Text = isEn ? $"Drive Capacity ({_watchedDriveLetter}:):" : $"Емкость накопителя ({_watchedDriveLetter}:):";
        CleanTargetsTitle.Text = isEn ? "SAFE CLEANUP TARGETS" : "ОБЛАСТИ БЕЗОПАСНОЙ ОЧИСТКИ";
        BtnScanJunkText.Text = isEn ? "Scan Cache" : "Сканировать кэш";
        if (BtnCleanSystemText != null) BtnCleanSystemText.Text = isEn ? "Clean System Cache" : "Очистить систему";
        if (BtnRollbackSystemText != null) BtnRollbackSystemText.Text = isEn ? "Rollback" : "Откат системы";
        if (BtnRelaunchAdminText != null) BtnRelaunchAdminText.Text = isEn ? "Run as Admin" : "Запуск от админа";
        if (StorageLogTitle != null) StorageLogTitle.Text = isEn ? "ACTIVITY & CLEANUP CONSOLE" : "КОНСОЛЬ АКТИВНОСТИ И ОЧИСТКИ";
        if (StorageLogFooter != null && (StorageLogFooter.Text == "Ready" || StorageLogFooter.Text == "Готов к работе"))
            StorageLogFooter.Text = isEn ? "Ready" : "Готов к работе";
        if (TxtResetSettings != null) TxtResetSettings.Text = isEn ? "Reset Defaults" : "Сбросить настройки";
        if (TxtIntervalCurrent != null) TxtIntervalCurrent.Text = isEn ? $"{_telemetryIntervalSeconds:0.#}s" : $"{_telemetryIntervalSeconds:0.#} сек";
        if (TxtThresholdCurrent != null) TxtThresholdCurrent.Text = isEn ? $"{_diskCThresholdGb:0.#} GB" : $"{_diskCThresholdGb:0.#} ГБ";

        // Games Tab
        if (GamesTabHeading != null) GamesTabHeading.Text = isEn ? "Game Sentinel & Catalog" : "Игровой модуль и каталог игр";
        if (GamesTabSub != null) GamesTabSub.Text = isEn ? "Active games telemetry, priority acceleration, and installed disk libraries" : "Телеметрия активных игр, повышение приоритета и каталог установленных игр";
        if (GamesBannerTitle != null) GamesBannerTitle.Text = isEn ? "Game Priority Booster" : "Оптимизация приоритета игр";
        if (GamesBannerSub != null) GamesBannerSub.Text = isEn ? "Elevates process CPU priority to High for active games (Minecraft, CS2, Dota 2, and others) to ensure maximum FPS." : "Повышает приоритет ЦП до Высокого для запущенных игр (Minecraft, CS2, Dota 2 и др.) для максимального FPS.";
        if (BtnGamesBoostText != null) BtnGamesBoostText.Text = isEn ? "Boost Game Priority" : "Повысить приоритет игр";
        if (ActiveGamesSectionTitle != null) ActiveGamesSectionTitle.Text = isEn ? "ACTIVE GAMES" : "АКТИВНЫЕ ИГРЫ";
        if (RamLogTextBox != null)
        {
            string enInit = "CoreSentinel initialized. Background telemetry and security monitoring active.";
            string ruInit = "CoreSentinel инициализирован. Фоновая телеметрия и мониторинг активны.";
            if (RamLogTextBox.Text.Trim() == enInit || RamLogTextBox.Text.Trim() == ruInit)
            {
                RamLogTextBox.Text = (isEn ? enInit : ruInit) + Environment.NewLine;
            }
        }
        if (InstalledGamesSectionTitle != null) InstalledGamesSectionTitle.Text = isEn ? "GAMES ON THIS DEVICE" : "ИГРЫ НА УСТРОЙСТВЕ";
        if (InstalledGamesSubtitle != null) InstalledGamesSubtitle.Text = isEn ? "Scan storage drives for installed games, libraries and launchers" : "Поиск установленных игр и библиотек на дисках компьютера";
        if (BtnScanGamesText != null) BtnScanGamesText.Text = isEn ? "Scan Drives" : "Сканировать диски";
        if (InstalledGamesFooterInfo != null) InstalledGamesFooterInfo.Text = isEn ? "Scans Steam, Xbox, Epic, AppData (.minecraft, .tlauncher), and custom drive game folders." : "Сканирует библиотеки Steam, Xbox, Epic, AppData (.minecraft, .tlauncher) и папки Games на дисках.";
        if (TxtNoActiveGames != null) TxtNoActiveGames.Text = isEn ? "No games currently running." : "В данный момент игры не запущены.";
        if (TxtScanPlaceholder != null) TxtScanPlaceholder.Text = isEn ? "No scan performed yet. Select a drive and click 'Scan Drives'." : "Сканирование еще не выполнялось. Выберите диск и нажмите 'Сканировать диски'.";
        if (TxtGameDriveFilter != null) TxtGameDriveFilter.Text = _gameDriveFilter == "ALL" ? (isEn ? "All Drives" : "Все диски") : (isEn ? $"Drive {_gameDriveFilter}:" : $"Диск {_gameDriveFilter}:");

        // Settings Tab
        SettingsHeadingTitle.Text = isEn ? "Settings" : "Настройки";
        SettingsHeadingSub.Text = isEn ? "System configuration, customization and hardware specifications" : "Конфигурация системы, кастомизация и информация об оборудовании";
        
        // Accordion 1
        ExpCustom.Header = isEn ? "Customization" : "Кастомизация";
        LangSettingTitle.Text = isEn ? "Interface Language" : "Язык интерфейса";
        LangSettingDesc.Text = isEn ? "Default is English. Switchable instantly." : "По умолчанию — English. Переключается на лету.";
        ThemeSettingTitle.Text = isEn ? "Interface Theme" : "Тема оформления";
        ThemeSettingDesc.Text = isEn ? "Dark or light theme for the entire workspace" : "Тёмная или светлая тема для всего интерфейса";
        ThemeDarkText.Text = isEn ? "Dark" : "Тёмная";
        ThemeLightText.Text = isEn ? "Light" : "Светлая";
        if (TimeFormatSettingTitle != null) TimeFormatSettingTitle.Text = isEn ? "Time Format" : "Формат времени";
        if (TimeFormatSettingDesc != null) TimeFormatSettingDesc.Text = isEn ? "12-hour (AM/PM) or 24-hour clock display" : "12-часовой (AM/PM) или 24-часовой формат времени";
        if (BtnTime12hText != null) BtnTime12hText.Text = isEn ? "12-hour" : "12-часовой";
        if (BtnTime24hText != null) BtnTime24hText.Text = isEn ? "24-hour" : "24-часовой";
        HudOpacityTitle.Text = isEn ? "HUD Overlay Opacity" : "Прозрачность HUD оверлея";
        if (TabManagerTitle != null) TabManagerTitle.Text = isEn ? "Navigation Tabs" : "Вкладки";
        if (TabManagerDesc != null) TabManagerDesc.Text = isEn ? "Manage navigation tabs, order and removal" : "Управление списком вкладок навигации, удаление и сортировка";
        if (TxtToggleTabEdit != null) TxtToggleTabEdit.Text = _isEditingTabs ? (isEn ? "Done" : "Готово") : (isEn ? "Edit" : "Редактировать");
        if (TabVaultTitle != null) TabVaultTitle.Text = isEn ? "Tab Storage" : "Хранилище вкладок";
        if (TabVaultDesc != null) TabVaultDesc.Text = isEn ? "Inactive tabs not added to navigation" : "Вкладки, не добавленные в навигацию. Нажмите Открыть для добавления";
        if (TxtBtnOpenTabVault != null) TxtBtnOpenTabVault.Text = VaultDrawerPanel?.Visibility == Visibility.Visible ? (isEn ? "Close" : "Закрыть") : (isEn ? "Open" : "Открыть");

        // Accordion 2
        ExpMonitor.Header = isEn ? "Monitoring & Tools" : "Мониторинг и инструменты";
        TelemetrySectionTitle.Text = isEn ? "TELEMETRY & POLLING" : "ТЕЛЕМЕТРИЯ И ОПРОС";
        IntervalTitle.Text = isEn ? "Metrics sampling interval:" : "Интервал сбора метрик:";
        IntervalDesc.Text = isEn ? "Determines update frequency for CPU, RAM and storage metrics" : "Определяет частоту обновления данных процессора, памяти и накопителей";

        AutoRamTitle.Text = isEn ? "Auto-purge on memory spike (> 85%)" : "Авто-сброс при перегрузке (> 85%)";
        AutoRamDesc.Text = isEn ? "Background working set trim when RAM is near capacity" : "Фоновый сброс рабочих наборов при пиковой занятости RAM";
        TxtAutoRamStatus.Text = _autoRamPurgeEnabled ? (isEn ? "Enabled" : "Включено") : (isEn ? "Disabled" : "Выключено");
        AutoGamePriorityTitle.Text = isEn ? "Automatic Game Priority" : "Автоматический Game Priority";
        AutoGamePriorityDesc.Text = isEn ? "Auto-boost javaw, cs2, dota2 upon launch" : "Авто-повышение приоритета для javaw, cs2, dota2 при запуске";
        TxtAutoGamePriorityStatus.Text = _autoGamePriorityEnabled ? (isEn ? "Enabled" : "Включено") : (isEn ? "Disabled" : "Выключено");
        ManualRamTitle.Text = isEn ? "Manual memory optimization:" : "Ручная оптимизация памяти:";
        BtnSettingsPurgeRamText.Text = isEn ? "Purge RAM" : "Очистить ОЗУ";
        StorageSectionTitle.Text = isEn ? "STORAGE & THRESHOLDS" : "НАКОПИТЕЛИ И ПОРОГИ";
        DiskThresholdTitle.Text = isEn ? "Drive warning threshold:" : "Порог предупреждения системного диска:";
        DiskThresholdDesc.Text = isEn ? "Warning status triggers when free space drops below threshold" : "Индикация статуса меняется при снижении свободного места";

        AlertSectionTitle.Text = isEn ? "ALERT THRESHOLDS" : "ПОРОГИ АЛЕРТОВ";
        CpuAlertTitle.Text = isEn ? "CPU Alert Threshold (%):" : "Порог CPU (%):";
        RamAlertTitle.Text = isEn ? "RAM Alert Threshold (%):" : "Порог RAM (%):";
        StartupSectionTitle.Text = isEn ? "STARTUP MANAGER" : "МЕНЕДЖЕР АВТОЗАПУСКА";
        BtnRefreshStartupText.Text = isEn ? "Refresh" : "Обновить";
        NetworkSectionTitle.Text = isEn ? "NETWORK ADAPTERS" : "СЕТЕВЫЕ АДАПТЕРЫ";
        BtnPingAllText.Text = isEn ? "Ping (8.8.8.8)" : "Пинг (8.8.8.8)";

        // Accordion 3: System Specs
        ExpSysInfo.Header = isEn ? "System" : "Система";
        SysInfoLoadingText.Text = isEn ? "Searching system specifications..." : "Поиск характеристик системы...";
        if (_sysInfoLoaded && _cachedCpu != null && _cachedRam != null && _cachedDisks != null && _cachedGpus != null && _cachedMb != null && _cachedBios != null && _cachedOs != null && _cachedNet != null)
        {
            PopulateSysInfo(_cachedCpu, _cachedRam, _cachedDisks, _cachedGpus, _cachedMb, _cachedBios, _cachedOs, _cachedNet, _cachedUptime);
        }
    }

    private void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string theme)
        {
            bool wantLight = theme == "Light";
            if (wantLight == _isLightTheme) return;
            SetTheme(wantLight);
        }
    }

    private void SetTheme(bool light)
    {
        _isLightTheme = light;

        try
        {
            var uri = new Uri(light ? "Themes/AppTheme.Light.xaml" : "Themes/AppTheme.xaml", UriKind.Relative);
            Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };
        }
        catch (Exception ex)
        {
            StatusFooterText.Text = $"Theme error: {ex.Message}";
        }

        if (BrandLogoImage != null)
        {
            try
            {
                BrandLogoImage.Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri(light ? "logo_dark.png" : "logo.png", UriKind.Relative));
            }
            catch (Exception ex)
            {
                Logger.Debug($"Brand logo image switch note: {ex.Message}");
            }
        }

        var primaryStyle = (Style)FindResource("BtnPrimaryWhite");
        var pillStyle = (Style)FindResource("BtnPill");

        if (BtnThemeDark != null) BtnThemeDark.Style = !light ? primaryStyle : pillStyle;
        if (BtnThemeLight != null) BtnThemeLight.Style = light ? primaryStyle : pillStyle;
        if (BtnLangEn != null) BtnLangEn.Style = _currentLanguage == "EN" ? primaryStyle : pillStyle;
        if (BtnLangRu != null) BtnLangRu.Style = _currentLanguage == "RU" ? primaryStyle : pillStyle;
        if (BtnTime12h != null) BtnTime12h.Style = _use12HourTime ? primaryStyle : pillStyle;
        if (BtnTime24h != null) BtnTime24h.Style = !_use12HourTime ? primaryStyle : pillStyle;
        if (BtnCleanSystem != null) BtnCleanSystem.Style = primaryStyle;

                        UpdateAutoRamVisual();
        UpdateAutoGamePriorityVisual();
        HighlightTab(_currentTabIndex);
        RebuildNavPills();
        RefreshVaultUI();
    }

    private void SliderHudOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtHudOpacityVal != null)
        {
            TxtHudOpacityVal.Text = $"{(int)e.NewValue}%";
        }
        if (_hudWindow != null && _hudWindow.IsLoaded)
        {
            _hudWindow.Opacity = e.NewValue / 100.0;
        }
    }

    // ACCORDION 2: Single-Row Dropdown Selectors + Inline Custom Value (No separator, no modal dialog)
    private void BtnIntervalSelector_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var cm = new ContextMenu();
        bool isEn = _currentLanguage == "EN";


        var presets = new (double Sec, string EnLabel, string RuLabel)[]
        {
            (0.5, "0.5s (High Precision)", "0.5 сек (Высокая точность)"),
            (1.0, "1.0s (Fast)", "1.0 сек (Быстро)"),
            (1.5, "1.5s (Default)", "1.5 сек (По умолчанию)"),
            (3.0, "3.0s (Eco)", "3.0 сек (Эко)"),
            (5.0, "5.0s (Power Saver)", "5.0 сек (Энергосбережение)")
        };

        foreach (var p in presets)
        {
            var item = new MenuItem
            {
                Header = isEn ? p.EnLabel : p.RuLabel,
                FontWeight = Math.Abs(_telemetryIntervalSeconds - p.Sec) < 0.05 ? FontWeights.Bold : FontWeights.Normal
            };
            item.Click += (s, ev) => SetTelemetryInterval(p.Sec);
            cm.Items.Add(item);
        }

        // Inline Custom item (NO Separator line!)
        var customPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var customLabel = new TextBlock
        {
            Text = isEn ? "Custom: " : "Своё: ",
            Foreground = (Brush)FindResource("BrushTextPrimary"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var customBox = new TextBox
        {
            Text = _telemetryIntervalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            Width = 52,
            Height = 22,
            Padding = new Thickness(4, 1, 4, 1),
            FontSize = 11.5,
            Background = (Brush)FindResource("BrushBgInner"),
            Foreground = (Brush)FindResource("BrushTextPrimary"),
            BorderBrush = (Brush)FindResource("BrushBorderCard"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };

        void CommitCustom()
        {
            if (double.TryParse(customBox.Text.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out double sec))
            {
                if (sec >= 0.2 && sec <= 60.0)
                {
                    SetTelemetryInterval(sec);
                    cm.IsOpen = false;
                }
            }
        }

        customBox.KeyDown += (s, ev) =>
        {
            if (ev.Key == Key.Enter)
            {
                CommitCustom();
                ev.Handled = true;
            }
        };

        customPanel.Children.Add(customLabel);
        customPanel.Children.Add(customBox);

        var customItem = new MenuItem
        {
            Header = customPanel,
            StaysOpenOnClick = true
        };
        cm.Items.Add(customItem);

        cm.PlacementTarget = btn;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.Opened += (s, ev) =>
        {
            customBox.Focus();
            customBox.SelectAll();
        };
        cm.IsOpen = true;
    }

    private void SetTelemetryInterval(double sec)
    {
        _telemetryIntervalSeconds = sec;
        _telemetryTimer.Interval = TimeSpan.FromSeconds(sec);
        bool isEn = _currentLanguage == "EN";


        if (TxtIntervalCurrent != null) TxtIntervalCurrent.Text = isEn ? $"{sec:0.#}s" : $"{sec:0.#} сек";
        StatusFooterText.Text = isEn ? $"Telemetry interval: {sec:0.#}s." : $"Интервал телеметрии: {sec:0.#} сек.";
    }

    private void BtnThresholdSelector_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var cm = new ContextMenu();
        bool isEn = _currentLanguage == "EN";


        var presets = new (double Gb, string EnLabel, string RuLabel)[]
        {
            (3.0, "3 GB", "3 ГБ"),
            (5.0, "5 GB", "5 ГБ"),
            (7.0, "7 GB (Default)", "7 ГБ (По умолчанию)"),
            (10.0, "10 GB", "10 ГБ"),
            (15.0, "15 GB", "15 ГБ")
        };

        foreach (var p in presets)
        {
            var item = new MenuItem
            {
                Header = isEn ? p.EnLabel : p.RuLabel,
                FontWeight = Math.Abs(_diskCThresholdGb - p.Gb) < 0.1 ? FontWeights.Bold : FontWeights.Normal
            };
            item.Click += (s, ev) => SetDiskThreshold(p.Gb);
            cm.Items.Add(item);
        }

        // Inline Custom item (NO Separator line!)
        var customPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var customLabel = new TextBlock
        {
            Text = isEn ? "Custom: " : "Своё: ",
            Foreground = (Brush)FindResource("BrushTextPrimary"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var customBox = new TextBox
        {
            Text = _diskCThresholdGb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            Width = 52,
            Height = 22,
            Padding = new Thickness(4, 1, 4, 1),
            FontSize = 11.5,
            Background = (Brush)FindResource("BrushBgInner"),
            Foreground = (Brush)FindResource("BrushTextPrimary"),
            BorderBrush = (Brush)FindResource("BrushBorderCard"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };

        void CommitCustom()
        {
            if (double.TryParse(customBox.Text.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out double gb))
            {
                if (gb >= 1.0 && gb <= 500.0)
                {
                    SetDiskThreshold(gb);
                    cm.IsOpen = false;
                }
            }
        }

        customBox.KeyDown += (s, ev) =>
        {
            if (ev.Key == Key.Enter)
            {
                CommitCustom();
                ev.Handled = true;
            }
        };

        customPanel.Children.Add(customLabel);
        customPanel.Children.Add(customBox);

        var customItem = new MenuItem
        {
            Header = customPanel,
            StaysOpenOnClick = true
        };
        cm.Items.Add(customItem);

        cm.PlacementTarget = btn;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.Opened += (s, ev) =>
        {
            customBox.Focus();
            customBox.SelectAll();
        };
        cm.IsOpen = true;
    }

    private void SetDiskThreshold(double gb)
    {
        _diskCThresholdGb = gb;
        bool isEn = _currentLanguage == "EN";


        if (TxtThresholdCurrent != null) TxtThresholdCurrent.Text = isEn ? $"{gb:0.#} GB" : $"{gb:0.#} ГБ";
        UpdateTelemetry();
        StatusFooterText.Text = isEn ? $"Disk warning threshold set to {gb:0.#} GB." : $"Порог предупреждения: {gb:0.#} ГБ.";
    }

    private void BtnToggleAutoRam_Click(object sender, RoutedEventArgs e)
    {
        _autoRamPurgeEnabled = !_autoRamPurgeEnabled;
        UpdateAutoRamVisual();
        StatusFooterText.Text = _autoRamPurgeEnabled
            ? (_currentLanguage == "EN" ? "Auto-RAM purge enabled." : "Авто-сброс ОЗУ включен.")
            : (_currentLanguage == "EN" ? "Auto-RAM purge disabled." : "Авто-сброс ОЗУ выключен.");
    }

    private void UpdateAutoRamVisual()
    {
        bool isEn = _currentLanguage == "EN";


        if (TxtAutoRamStatus != null) TxtAutoRamStatus.Text = _autoRamPurgeEnabled ? (isEn ? "Enabled" : "Включено") : (isEn ? "Disabled" : "Выключено");
        if (BtnToggleAutoRam != null) BtnToggleAutoRam.Style = _autoRamPurgeEnabled ? (Style)FindResource("BtnPrimaryWhite") : (Style)FindResource("BtnPill");
    }

    private void BtnToggleAutoGamePriority_Click(object sender, RoutedEventArgs e)
    {
        _autoGamePriorityEnabled = !_autoGamePriorityEnabled;
        UpdateAutoGamePriorityVisual();
        StatusFooterText.Text = _autoGamePriorityEnabled
            ? (_currentLanguage == "EN" ? "Automatic Game Priority enabled." : "Автоматический Game Priority включен.")
            : (_currentLanguage == "EN" ? "Automatic Game Priority disabled." : "Автоматический Game Priority выключен.");
    }

    private void UpdateAutoGamePriorityVisual()
    {
        bool isEn = _currentLanguage == "EN";


        if (TxtAutoGamePriorityStatus != null) TxtAutoGamePriorityStatus.Text = _autoGamePriorityEnabled ? (isEn ? "Enabled" : "Включено") : (isEn ? "Disabled" : "Выключено");
        if (BtnToggleAutoGamePriority != null) BtnToggleAutoGamePriority.Style = _autoGamePriorityEnabled ? (Style)FindResource("BtnPrimaryWhite") : (Style)FindResource("BtnPill");
    }

    private void BtnSettingsPurgeRam_Click(object sender, RoutedEventArgs e)
    {
        ExecuteRamPurge();
        if (SettingsRamStatusText != null)
        {
            string nowTime = FormatTimeLog(DateTime.Now);
            SettingsRamStatusText.Text = _currentLanguage == "EN" ? $"Manual purge executed at {nowTime}" : $"Ручная очистка выполнена в {nowTime}";
        }
    }

    private void SliderCpuAlert_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _cpuAlertThreshold = e.NewValue;
        if (TxtCpuAlertVal != null)
            TxtCpuAlertVal.Text = $"{(int)e.NewValue}%";
    }

    private void SliderRamAlert_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _ramAlertThreshold = e.NewValue;
        if (TxtRamAlertVal != null)
            TxtRamAlertVal.Text = $"{(int)e.NewValue}%";
    }

    // Startup Manager
    private void LoadStartupEntries()
    {
        Task.Run(() =>
        {
            var entries = StartupService.GetAllEntries();
            Dispatcher.Invoke(() =>
            {
                _startupEntries = entries;
                StartupList.ItemsSource = _startupEntries;
            });
        });
    }

    private void BtnRefreshStartup_Click(object sender, RoutedEventArgs e)
    {
        LoadStartupEntries();
    }

    private void BtnStartupToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is StartupEntry entry)
        {
            bool isEn = _currentLanguage == "EN";
            var res = StartupService.SetEntryEnabled(entry, !entry.IsEnabled, isEn);
            StatusFooterText.Text = res.Message;
            LoadStartupEntries();
        }
    }

    private void BtnStartupDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is StartupEntry entry)
        {
            bool isEn = _currentLanguage == "EN";
            var res = StartupService.DeleteEntry(entry, isEn);
            StatusFooterText.Text = res.Message;
            LoadStartupEntries();
        }
    }

    // Network Info & Ping
    private void LoadNetworkAdapters()
    {
        Task.Run(() =>
        {
            var adapters = SystemInfoService.GetNetworkAdapters();
            Dispatcher.Invoke(() =>
            {
                _networkAdapters = adapters;
                NetworkList.ItemsSource = _networkAdapters;
            });
        });
    }

    private void BtnPingAll_Click(object sender, RoutedEventArgs e)
    {
        Task.Run(() =>
        {
            bool isEn = _currentLanguage == "EN";


            try
            {
                using var pinger = new Ping();
                var reply = pinger.Send("8.8.8.8", 1500);
                string resultStr = reply.Status == IPStatus.Success
                    ? $"Google DNS: {reply.RoundtripTime} ms (OK)"
                    : $"Google DNS: {reply.Status}";

                Dispatcher.Invoke(() =>
                {
                    StatusFooterText.Text = isEn ? $"Network Ping: {resultStr}" : $"Пинг сети: {resultStr}";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusFooterText.Text = $"Ping error: {ex.Message}";
                });
            }
        });
    }

    // ACCORDION 3: System Specs
    private void ExpSysInfo_Expanded(object sender, RoutedEventArgs e)
    {
        Expander_Expanded(sender, e);

        if (_sysInfoLoaded) return;
        _sysInfoLoaded = true;

        StatusFooterText.Text = _currentLanguage == "EN" ? "Searching system specifications..." : "Поиск характеристик системы...";

        Task.Run(() =>
        {
            bool isEn = _currentLanguage == "EN";

            var cpu = SystemInfoService.GetCpuDetails(isEn);
            var ram = SystemInfoService.GetRamDetails(isEn);
            var disks = SystemInfoService.GetAllDiskDetails(isEn);
            var gpus = SystemInfoService.GetGpus();
            var mb = SystemInfoService.GetMotherboard();
            var bios = SystemInfoService.GetBios();
            var os = SystemInfoService.GetOsInfo();
            var net = SystemInfoService.GetNetworkAdapters(isEn);
            string uptime = SystemInfoService.GetUptime(isEn);

            Dispatcher.Invoke(() =>
            {
                _cachedCpu = cpu;
                _cachedRam = ram;
                _cachedDisks = disks;
                _cachedGpus = gpus;
                _cachedMb = mb;
                _cachedBios = bios;
                _cachedOs = os;
                _cachedNet = net;
                _cachedUptime = uptime;

                PopulateSysInfo(cpu, ram, disks, gpus, mb, bios, os, net, uptime);

                double availWidth = ExpSysInfo.ActualWidth > 40 ? ExpSysInfo.ActualWidth - 40 : 800;
                SysInfoPanel.Measure(new Size(availWidth, double.PositiveInfinity));
                double targetH = SysInfoPanel.DesiredSize.Height;
                var anim = new DoubleAnimation(SysInfoPanel.ActualHeight, targetH, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                anim.Completed += (s, ev) =>
                {
                    SysInfoPanel.BeginAnimation(FrameworkElement.HeightProperty, null);
                    SysInfoPanel.Height = double.NaN;
                    CheckStickyAccordionOnScroll();
                };
                SysInfoPanel.BeginAnimation(FrameworkElement.HeightProperty, anim);
                StatusFooterText.Text = _currentLanguage == "EN" ? "System specifications loaded." : "Данные о системе загружены.";
            });
        });
    }

    private void PopulateSysInfo(
        List<CpuInfo> cpus,
        List<RamSlot> ram,
        List<DiskDetail> disks,
        List<GpuInfo> gpus,
        MotherboardInfo mb,
        BiosInfo bios,
        OsInfo os,
        List<NetworkAdapterInfo> net,
        string uptime)
    {
        SysInfoPanel.Children.Clear();
        bool isEn = _currentLanguage == "EN";



        string osCaption = os.Caption;
        string osArch = os.Architecture;
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

        // 1. OS & Computer
        AddSysInfoSection(isEn ? "OPERATING SYSTEM & PC" : "ОПЕРАЦИОННАЯ СИСТЕМА И ПК", new[]
        {
            (isEn ? "Computer Name" : "Имя компьютера", os.ComputerName),
            (isEn ? "Current User" : "Пользователь", os.RegisteredUser),
            (isEn ? "OS" : "ОС", $"{osCaption} ({osArch})"),
            (isEn ? "Version / Build" : "Версия / Сборка", $"{os.Version} (Build {os.BuildNumber})"),
            (isEn ? "System Uptime" : "Время работы (Uptime)", uptime),
            (isEn ? "Install Date" : "Дата установки", FormatInstallDate(os.InstallDate))
        });

        // 2. CPU
        foreach (var c in cpus)
        {
            AddSysInfoSection(isEn ? $"PROCESSOR — {c.Name}" : $"ПРОЦЕССОР — {c.Name}", new[]
            {
                (isEn ? "Physical Cores" : "Физических ядер", c.Cores.ToString()),
                (isEn ? "Logical Processors" : "Логических процессоров", c.LogicalProcessors.ToString()),
                (isEn ? "Base Clock" : "Базовая частота", c.MaxClockMhz),
                (isEn ? "Architecture" : "Архитектура", c.Architecture),
                (isEn ? "Socket" : "Сокет", c.SocketDesignation)
            });
        }

        // 3. RAM
        long totalRamBytes = ram.Sum(r => r.CapacityBytes);
        var ramRows = new List<(string Label, string Value)>
        {
            (isEn ? "Total Installed" : "Общий объем", StorageService.FormatBytes(totalRamBytes)),
            (isEn ? "Slots Occupied" : "Слотов занято", ram.Count.ToString())
        };
        for (int i = 0; i < ram.Count; i++)
        {
            var r = ram[i];
            ramRows.Add((isEn ? $"Slot {r.BankLabel}" : $"Слот {r.BankLabel}", $"{r.FormattedSize} · {r.Speed} · {r.Manufacturer} ({r.MemoryType})"));
        }
        AddSysInfoSection(isEn ? "PHYSICAL MEMORY (RAM SLOTS)" : "ОПЕРАТИВНАЯ ПАМЯТЬ (RAM SLOTS)", ramRows);

        // 4. GPU
        foreach (var g in gpus)
        {
            AddSysInfoSection(isEn ? $"GRAPHICS ADAPTER — {g.Name}" : $"ВИДЕОКАРТА — {g.Name}", new[]
            {
                (isEn ? "Video Memory (VRAM)" : "Видеопамять (VRAM)", g.FormattedRam),
                (isEn ? "Driver Version" : "Версия драйвера", g.DriverVersion)
            });
        }

        // 5. Motherboard & BIOS
        AddSysInfoSection(isEn ? "MOTHERBOARD & BIOS" : "МАТЕРИНСКАЯ ПЛАТА И BIOS", new[]
        {
            (isEn ? "Motherboard Vendor" : "Производитель платы", mb.Manufacturer),
            (isEn ? "Motherboard Model" : "Модель платы", mb.Product),
            (isEn ? "Serial Number" : "Серийный номер", mb.SerialNumber),
            (isEn ? "BIOS Vendor" : "Производитель BIOS", bios.Manufacturer),
            (isEn ? "BIOS Version" : "Версия BIOS", bios.Version),
            (isEn ? "BIOS Release Date" : "Дата выпуска BIOS", bios.ReleaseDate)
        });

        // 6. Disks
        var diskRows = new List<(string Label, string Value)>();
        foreach (var d in disks)
        {
            string freeOfTotal = isEn ? $"{d.FormattedFree} free of {d.FormattedTotal}" : $"{d.FormattedFree} свободно из {d.FormattedTotal}";
            string diskLabel = d.Label;
            if (string.IsNullOrWhiteSpace(diskLabel) || diskLabel.Equals("Локальный диск", StringComparison.OrdinalIgnoreCase) || diskLabel.Equals("Local Disk", StringComparison.OrdinalIgnoreCase))
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
            diskRows.Add((isEn ? $"Drive {d.DriveLetter}: [{diskLabel}]" : $"Диск {d.DriveLetter}: [{diskLabel}]", $"{freeOfTotal} ({d.FileSystem}, {driveTypeDesc})"));
        }
        AddSysInfoSection(isEn ? "PHYSICAL & LOGICAL DRIVES" : "ФИЗИЧЕСКИЕ И ЛОГИЧЕСКИЕ НАКОПИТЕЛИ", diskRows);
    }

    private void AddSysInfoSection(string title, IEnumerable<(string Label, string Value)> rows)
    {
        var border = new Border
        {
            Background = (SolidColorBrush)FindResource("BrushBgInner"),
            BorderBrush = (SolidColorBrush)FindResource("BrushBorderCard"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var stack = new StackPanel();

        var header = new TextBlock
        {
            Text = title,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("BrushTextMuted"),
            Margin = new Thickness(0, 0, 0, 10)
        };
        stack.Children.Add(header);

        bool first = true;
        foreach (var (label, val) in rows)
        {
            if (!first)
            {
                stack.Children.Add(new Separator
                {
                    Background = (SolidColorBrush)FindResource("BrushBorderSubtle"),
                    Margin = new Thickness(0, 6, 0, 6)
                });
            }
            first = false;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = (SolidColorBrush)FindResource("BrushTextSecondary"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var v = new TextBlock
            {
                Text = string.IsNullOrEmpty(val) ? "--" : val,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("BrushTextPrimary"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(v, 1);
            grid.Children.Add(v);

            stack.Children.Add(grid);
        }

        border.Child = stack;
        SysInfoPanel.Children.Add(border);
    }

    #endregion

    #region HUD & Process Manager Handlers

    // CPU Card / Chip Click -> Switches to Tab 1 in CPU mode!
    private void CpuMetric_Click(object sender, RoutedEventArgs e)
    {
        SetPurgeTabMode(isCpu: true);
        HighlightTab(1);
    }

    // RAM Card / Chip Click -> Switches to Tab 1 in RAM mode!
    private void RamMetric_Click(object sender, RoutedEventArgs e)
    {
        SetPurgeTabMode(isCpu: false);
        HighlightTab(1);
    }

    private void DiskCMetric_Click(object sender, RoutedEventArgs e)
    {
        OpenDriveInExplorer($"{_watchedDriveLetter}:\\");
    }

    private void DiskDMetric_Click(object sender, RoutedEventArgs e)
    {
        string data = HardwareTelemetry.GetSecondaryDataDriveLetter();
        OpenDriveInExplorer($"{data}:\\");
    }

    private void OpenDriveInExplorer(string drivePath)
    {
        bool isEn = _currentLanguage == "EN";

        try
        {
            string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo
            {
                FileName = explorerPath,
                Arguments = drivePath,
                UseShellExecute = true
            });
            StatusFooterText.Text = isEn ? $"Drive '{drivePath}' opened in Explorer." : $"Накопитель '{drivePath}' открыт в Проводнике.";
        }
        catch (Exception ex)
        {
            StatusFooterText.Text = $"Explorer error: {ex.Message}";
        }
    }

    private void BtnToggleHud_Click(object sender, RoutedEventArgs e)
    {
        if (_hudWindow == null || !_hudWindow.IsLoaded)
        {
            _hudWindow = new TelemetryHudWindow();
            _hudWindow.Show();
        }
        else
        {
            if (_hudWindow.Visibility == Visibility.Visible)
                _hudWindow.Hide();
            else
                _hudWindow.Show();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveCurrentUserSettings();
        _telemetryTimer.Stop();
        if (_hudWindow != null && _hudWindow.IsLoaded)
        {
            _hudWindow.Close();
        }
        base.OnClosed(e);
    }

    #endregion

    #region Window Caption & Fullscreen Controls

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
        }
        else if (WindowState != WindowState.Maximized)
        {
            DragMove();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.Maximized)
        {
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            Width = 1080;
            Height = 720;
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        }
        else
        {
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion
}
