using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CoreSpectre.Services;
using CoreSpectre.Views;

namespace CoreSpectre;

public partial class MainWindow : Window
{
    private UserSettingsModel _settings = new();
    private readonly DispatcherTimer _telemetryTimer;
    private readonly DispatcherTimer _clockTimer;
    private TelemetryHudWindow? _hudWindow;

    // Configurable runtime thresholds
    private double _telemetryIntervalSeconds = 1.0;
    private double _diskCThresholdGb = 7.0;
    private double _cpuAlertThreshold = 90.0;
    private double _ramAlertThreshold = 85.0;
    private bool _autoRamPurgeEnabled = true;
    private DateTime _lastAutoPurgeTime = DateTime.MinValue;
    private bool _autoGamePriorityEnabled = true;
    private string _watchedDriveLetter = "C";
    private string _currentLanguage = "EN";
    private bool _isLightTheme = false;
    private bool _use12HourTime = true;
    private bool _isCpuPurgeMode = false;
    private int _ramCooldownRemaining = 0;
    private DispatcherTimer? _ramCooldownTimer;

    // Navigation Tabs Model
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
    private FrameworkElement? _draggedRow;
    private Point _dragStartPoint;
    private int _dragStartIndex = -1;
    private int _targetDropIndex = -1;
    private TaskCompletionSource<bool>? _modalTcs;

    public MainWindow()
    {
        InitializeComponent();
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;

        SettingsPersistenceService.OnSettingsError += msg =>
        {
            Dispatcher.Invoke(() =>
            {
                if (StatusFooterText != null) StatusFooterText.Text = msg;
            });
        };

        LoadAndApplyUserSettings();

        // Wire View Events
        WireViewEvents();

        _telemetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_telemetryIntervalSeconds)
        };
        _telemetryTimer.Tick += (s, e) => UpdateTelemetry();
        _telemetryTimer.Start();

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (s, e) => UpdateFooterClock();
        _clockTimer.Start();

        // Initial setup
        InitNavigationTabs();
        ApplyLanguageStrings();
        SetTheme(_isLightTheme);
        UpdateTelemetry();
        UpdateFooterClock();
        HighlightTab(0);
        RefreshVaultUI();

        ViewPurge.Log(LocalizationService.IsRussian
            ? "CoreSpectre инициализирован. Фоновая телеметрия и мониторинг активны."
            : "CoreSpectre initialized. Background telemetry and security monitoring active.");

        _ = ViewStorage.ScanJunkAsync(showLog: false);
    }

    private void WireViewEvents()
    {
        // Overview View
        ViewOverview.OnCpuClicked += () =>
        {
            SetPurgeTabMode(isCpu: true);
            HighlightTab(1);
        };
        ViewOverview.OnRamClicked += () =>
        {
            SetPurgeTabMode(isCpu: false);
            HighlightTab(1);
        };
        ViewOverview.OnDiskCClicked += () => OpenDriveExplorer(_watchedDriveLetter);
        ViewOverview.OnDiskDClicked += () => OpenSecondaryDriveExplorer();
        ViewOverview.OnDiskDropdownClicked += fe => ShowDiskDropdown(fe);
        ViewOverview.OnQuickPurgeClicked += () => ExecuteQuickPurge();
        ViewOverview.OnToggleHudClicked += () => ToggleHudWindow();
        ViewOverview.OnCleanJunkClicked += () => HighlightTab(2);

        // Purge View
        ViewPurge.OnPurgeRequested += () => ExecuteQuickPurge();
        ViewPurge.OnSwitchPurgeModeRequested += () => ShowPurgeModeDropdown();
        ViewPurge.OnOpenTaskManagerRequested += () => LaunchTaskManager();

        // Storage View
        ViewStorage.OnDiskDropdownRequested += fe => ShowDiskDropdown(fe);
        ViewStorage.OnStatusChanged += msg => StatusFooterText.Text = msg;
        ViewStorage.OnCleanupCompleted += () => UpdateTelemetry();
        ViewStorage.ShowConfirmDialogAsync = ShowModernConfirmDialogAsync;

        // Games View
        ViewGames.OnBoostPriorityRequested += () => ExecuteGameBoost();
        ViewGames.OnRefreshActiveGamesRequested += () => PollActiveGames();
        ViewGames.OnGameDriveFilterRequested += fe => ShowGameDriveFilterDropdown(fe);
        ViewGames.OnIncludeLaunchersChanged += val =>
        {
            _settings.IncludeLaunchers = val;
            SaveCurrentUserSettings();
        };
        ViewGames.OnStatusChanged += msg => StatusFooterText.Text = msg;

        // Settings View
        ViewSettings.OnLanguageChanged += lang => SetLanguage(lang);
        ViewSettings.OnThemeChanged += isLight => SetTheme(isLight);
        ViewSettings.OnTimeFormatChanged += is12h => SetTimeFormat(is12h);
        ViewSettings.OnHudOpacityChanged += val => SetHudOpacity(val);
        ViewSettings.OnCpuAlertChanged += val =>
        {
            _cpuAlertThreshold = val;
            SaveCurrentUserSettings();
        };
        ViewSettings.OnRamAlertChanged += val =>
        {
            _ramAlertThreshold = val;
            SaveCurrentUserSettings();
        };
        ViewSettings.OnAutoRamChanged += val =>
        {
            _autoRamPurgeEnabled = val;
            SaveCurrentUserSettings();
            StatusFooterText.Text = val
                ? (LocalizationService.IsEnglish ? "Auto-RAM purge enabled." : "Авто-сброс ОЗУ включен.")
                : (LocalizationService.IsEnglish ? "Auto-RAM purge disabled." : "Авто-сброс ОЗУ выключен.");
        };
        ViewSettings.OnAutoGamePriorityChanged += val =>
        {
            _autoGamePriorityEnabled = val;
            SaveCurrentUserSettings();
            StatusFooterText.Text = val
                ? (LocalizationService.IsEnglish ? "Automatic Game Priority enabled." : "Автоматический Game Priority включен.")
                : (LocalizationService.IsEnglish ? "Automatic Game Priority disabled." : "Автоматический Game Priority выключен.");
        };
        ViewSettings.OnResetSettingsRequested += () => ResetSettings();
        ViewSettings.OnIntervalSelectorRequested += fe => ShowIntervalDropdown(fe);
        ViewSettings.OnThresholdSelectorRequested += fe => ShowThresholdDropdown(fe);
        ViewSettings.OnToggleTabEditRequested += () => ToggleTabEditMode();
        ViewSettings.OnTabEditShouldDisable += () => DisableTabEditMode();
        ViewSettings.OnOpenTabVaultRequested += () => ToggleTabVault();
        ViewSettings.OnRestoreTabRequested += key => RestoreTabByKey(key);
        ViewSettings.OnStatusChanged += msg => StatusFooterText.Text = msg;
    }

    private void LoadAndApplyUserSettings()
    {
        _settings = SettingsPersistenceService.Load();

        _currentLanguage = _settings.Language == "RU" ? "RU" : "EN";
        LocalizationService.CurrentLanguage = _currentLanguage;
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
        _isCpuPurgeMode = string.Equals(_settings.PurgeMode, "CPU", StringComparison.OrdinalIgnoreCase);

        ViewPurge.SetPurgeMode(_isCpuPurgeMode);
        ViewGames.SetIncludeLaunchers(_settings.IncludeLaunchers);
        ViewStorage.BindSettings(_settings, SaveCurrentUserSettings);
        ViewSettings.InitSettings(_settings, _isLightTheme);
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
            _settings.AutoRamPurge = _autoRamPurgeEnabled;
            _settings.AutoGamePriority = _autoGamePriorityEnabled;
            _settings.WatchedDriveLetter = _watchedDriveLetter;
            _settings.PurgeMode = _isCpuPurgeMode ? "CPU" : "RAM";
            _settings.ActiveTabs = _activeTabs.Select(t => t.Key).ToList();
            _settings.VaultTabs = _vaultTabs.Select(t => t.Key).ToList();

            if (!SettingsPersistenceService.Save(_settings))
            {
                if (SettingsPersistenceService.LastError != null)
                {
                    StatusFooterText.Text = $"Settings save error: {SettingsPersistenceService.LastError}";
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to persist runtime settings: {ex.Message}");
            StatusFooterText.Text = $"Settings save error: {ex.Message}";
        }
    }

    private void UpdateFooterClock()
    {
        var now = DateTime.Now;
        var offset = TimeZoneInfo.Local.GetUtcOffset(now);
        string offsetSign = offset >= TimeSpan.Zero ? "+" : "-";
        string offsetStr = offset.Minutes == 0
            ? $"UTC{offsetSign}{Math.Abs(offset.Hours)}"
            : $"UTC{offsetSign}{Math.Abs(offset.Hours)}:{Math.Abs(offset.Minutes):00}";

        string timePattern = _use12HourTime ? "hh:mm:ss tt" : "HH:mm:ss";
        string localTimeFormatted = now.ToString(timePattern, CultureInfo.InvariantCulture);

        FooterClockText.Text = $"{localTimeFormatted} ({offsetStr})";
    }

    private void UpdateTelemetry()
    {
        try
        {
            var sample = HardwareTelemetry.Sample();
            bool isEn = LocalizationService.IsEnglish;

            // Quick metrics header
            QuickCpuText.Text = $"{sample.CpuLoadPercent:0}%";
            QuickRamText.Text = sample.RamTotalBytes > 0 ? $"{sample.RamUsedPercent:0}%" : "N/A";
            QuickDiskLabel.Text = isEn ? $"Drive {sample.SystemDriveLetter}: " : $"Диск {sample.SystemDriveLetter}: ";
            QuickDiskText.Text = isEn ? $"{sample.DiskCFreeGb:0.0} GB" : $"{sample.DiskCFreeGb:0.0} ГБ";
            QuickDiskMetric.ToolTip = isEn
                ? $"Open Drive {sample.SystemDriveLetter}: in Windows Explorer"
                : $"Открыть диск {sample.SystemDriveLetter}: в Проводнике";

            double ramUsedGb = sample.RamUsedBytes / (1024.0 * 1024 * 1024);
            double ramTotalGb = sample.RamTotalBytes / (1024.0 * 1024 * 1024);

            // Watched drive metrics (can be C:, D:, etc.)
            var watchedInfo = StorageService.GetDriveInfo(_watchedDriveLetter);
            double watchedTotalGb = watchedInfo != null ? (double)watchedInfo.TotalBytes / (1024 * 1024 * 1024) : sample.DiskCTotalGb;
            double watchedFreeGb = watchedInfo != null ? (double)watchedInfo.FreeBytes / (1024 * 1024 * 1024) : sample.DiskCFreeGb;

            // Health determination
            string healthBadge = isEn ? "Healthy" : "В норме";
            string recommendation = isEn ? "All systems operating nominally" : "Все системы работают стабильно";
            if (sample.CpuLoadPercent >= _cpuAlertThreshold || sample.RamUsedPercent >= _ramAlertThreshold)
            {
                healthBadge = isEn ? "Warning" : "Внимание";
                recommendation = isEn ? "High system resource consumption" : "Высокая нагрузка на ресурсы";
            }
            else if (watchedFreeGb < _diskCThresholdGb)
            {
                healthBadge = isEn ? "Low Space" : "Мало места";
                recommendation = isEn
                    ? $"Free space on {_watchedDriveLetter}: below threshold (< {_diskCThresholdGb:0.#} GB)"
                    : $"На диске {_watchedDriveLetter}: осталось мало места (< {_diskCThresholdGb:0.#} ГБ)";
            }

            // Game info & Header Game Mode Indicator
            var activeGames = GameSentinelService.GetActiveGames(!isEn);
            string gameBadge = activeGames.Count > 0 ? (isEn ? "Active" : "Активен") : (isEn ? "Waiting" : "В ожидании");
            string gameDetail = activeGames.Count > 0
                ? $"{activeGames[0].DisplayName} (PID: {activeGames[0].Pid})"
                : (isEn ? "No games detected · Background monitoring active" : "Игры не обнаружены · Мониторинг активен");

            if (activeGames.Count > 0)
            {
                GameModeDot.Fill = (Brush)FindResource("BrushTextPrimary");
                GameModeText.Text = isEn ? "Active" : "Активен";
                GameModeText.Foreground = (Brush)FindResource("BrushTextPrimary");
                GameModeIndicatorPanel.ToolTip = isEn
                    ? $"Active game: {activeGames[0].DisplayName}"
                    : $"Активная игра: {activeGames[0].DisplayName}";
            }
            else
            {
                GameModeDot.Fill = (Brush)FindResource("BrushTextMuted");
                GameModeText.Text = isEn ? "Waiting for games" : "Ожидание игр";
                GameModeText.Foreground = (Brush)FindResource("BrushTextSecondary");
                GameModeIndicatorPanel.ToolTip = isEn ? "Open Game Sentinel" : "Открыть Игровой модуль";
            }

            // Update Views
            ViewOverview.UpdateMetrics(
                sample.CpuLoadPercent,
                HardwareTelemetry.GetProcessorName(),
                sample.RamUsedPercent,
                ramUsedGb,
                ramTotalGb,
                _watchedDriveLetter,
                watchedFreeGb,
                watchedTotalGb,
                sample.HasSecondaryDataDrive ? sample.DataDriveLetter : null,
                sample.DiskDFreeGb,
                sample.DiskDTotalGb,
                gameBadge,
                gameDetail,
                healthBadge,
                recommendation
            );

            ViewPurge.UpdateMetrics(
                ramUsedGb,
                ramTotalGb,
                sample.CpuLoadPercent,
                HardwareTelemetry.GetProcessorName(),
                HardwareTelemetry.GetPhysicalCoreCount(),
                Environment.ProcessorCount
            );

            ViewStorage.UpdateCapacity(_watchedDriveLetter, watchedFreeGb, watchedTotalGb);
            ViewGames.UpdateActiveGames(activeGames);

            // Auto-RAM purge with 60s cooldown
            if (_autoRamPurgeEnabled && sample.RamTotalBytes > 0 && sample.RamUsedPercent >= _ramAlertThreshold &&
                (DateTime.Now - _lastAutoPurgeTime).TotalSeconds >= 60)
            {
                _lastAutoPurgeTime = DateTime.Now;
                ExecuteRamPurge(isAutomatic: true);
            }

            // Auto Game Priority Boost
            if (_autoGamePriorityEnabled && activeGames.Count > 0)
            {
                GameSentinelService.BoostGames(!isEn);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Telemetry update warning: {ex.Message}");
        }
    }

    private void ApplyLanguageStrings()
    {
        NavHeaderTitle.Text = LocalizationService.Get("Nav.Navigation");
        QuickCpuMetric.ToolTip = LocalizationService.Get("Tip.CpuAction");
        QuickRamMetric.ToolTip = LocalizationService.Get("Tip.RamAction");
        BtnToggleHud.ToolTip = LocalizationService.Get("Tip.ToggleHud");

        foreach (var tab in _activeTabs.Concat(_vaultTabs))
        {
            if (tab.Key == "storage")
            {
                tab.EnTitle = $"Storage ({_watchedDriveLetter}:)";
                tab.RuTitle = $"Хранилище ({_watchedDriveLetter}:)";
            }
            else if (tab.Key == "ram")
            {
                tab.EnTitle = _isCpuPurgeMode ? "CPU Optimization" : "RAM Purge";
                tab.RuTitle = _isCpuPurgeMode ? "Оптимизация ЦП" : "Очистка ОЗУ";
                tab.IconKey = _isCpuPurgeMode ? "IconCpu" : "IconRam";
            }
        }
        RebuildNavPills();
        RefreshVaultUI();

        ViewOverview.ApplyLanguage(_currentLanguage);
        ViewPurge.UpdateVisuals();
        ViewStorage.ApplyLanguage(_currentLanguage);
        ViewGames.ApplyLanguage(_currentLanguage);
        ViewSettings.ApplyLanguage(_currentLanguage);
        UpdateTelemetry();
    }

    public void SetLanguage(string lang)
    {
        _currentLanguage = lang == "RU" ? "RU" : "EN";
        LocalizationService.CurrentLanguage = _currentLanguage;
        SaveCurrentUserSettings();
        ApplyLanguageStrings();
        _ = ViewStorage.ScanJunkAsync(showLog: false);
    }

    public void SetTheme(bool isLight)
    {
        _isLightTheme = isLight;
        try
        {
            var uri = new Uri(isLight
                ? "pack://application:,,,/CoreSpectre;component/Assets/Themes/AppTheme.Light.xaml"
                : "pack://application:,,,/CoreSpectre;component/Assets/Themes/AppTheme.xaml", UriKind.Absolute);
            Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };
        }
        catch
        {
            try
            {
                var relUri = new Uri(isLight ? "Assets/Themes/AppTheme.Light.xaml" : "Assets/Themes/AppTheme.xaml", UriKind.Relative);
                Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = relUri };
            }
            catch (Exception ex)
            {
                Logger.Warn($"Theme apply warning: {ex.Message}");
            }
        }

        if (BrandLogoImage != null)
        {
            try
            {
                BrandLogoImage.Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri(isLight
                        ? "pack://application:,,,/CoreSpectre;component/Assets/Icons/logo_dark.png"
                        : "pack://application:,,,/CoreSpectre;component/Assets/Icons/logo.png", UriKind.Absolute));
            }
            catch
            {
                try
                {
                    BrandLogoImage.Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri(isLight ? "Assets/Icons/logo_dark.png" : "Assets/Icons/logo.png", UriKind.Relative));
                }
                catch { }
            }
        }

        SaveCurrentUserSettings();
        ViewSettings.RefreshThemeState(isLight);
        ViewGames.UpdateLauncherToggleVisual();
        RebuildNavPills();
        RefreshVaultUI();
    }

    public void SetTimeFormat(bool is12h)
    {
        _use12HourTime = is12h;
        SaveCurrentUserSettings();
        UpdateFooterClock();
    }

    public void SetHudOpacity(double val)
    {
        _settings.HudOpacity = val;
        if (_hudWindow != null && _hudWindow.IsLoaded)
        {
            _hudWindow.Opacity = val / 100.0;
        }
        SaveCurrentUserSettings();
    }

    public void SetPurgeTabMode(bool isCpu, bool saveSettings = true)
    {
        _isCpuPurgeMode = isCpu;
        ViewPurge.SetPurgeMode(isCpu);

        var ramTab = _activeTabs.Concat(_vaultTabs).FirstOrDefault(t => t.Key == "ram");
        if (ramTab != null)
        {
            ramTab.EnTitle = isCpu ? "CPU Optimization" : "RAM Purge";
            ramTab.RuTitle = isCpu ? "Оптимизация ЦП" : "Очистка ОЗУ";
            ramTab.IconKey = isCpu ? "IconCpu" : "IconRam";
        }

        RebuildNavPills();
        RefreshVaultUI();
        UpdateTelemetry();
        if (saveSettings) SaveCurrentUserSettings();
    }

    private void LaunchTaskManager()
    {
        try
        {
            string taskmgrPath = Path.Combine(Environment.SystemDirectory, "taskmgr.exe");
            Process.Start(new ProcessStartInfo(taskmgrPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusFooterText.Text = $"Failed to launch taskmgr: {ex.Message}";
        }
    }

    private void OpenDriveExplorer(string driveLetter)
    {
        try
        {
            string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(explorerPath, $"{driveLetter}:\\") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusFooterText.Text = $"Failed to open explorer: {ex.Message}";
        }
    }

    private void OpenSecondaryDriveExplorer()
    {
        var sample = HardwareTelemetry.Sample();
        if (sample.HasSecondaryDataDrive && !string.IsNullOrEmpty(sample.DataDriveLetter))
        {
            OpenDriveExplorer(sample.DataDriveLetter);
        }
    }

    private void ToggleHudWindow()
    {
        if (_hudWindow == null || !_hudWindow.IsLoaded)
        {
            _hudWindow = new TelemetryHudWindow();
            _hudWindow.Opacity = _settings.HudOpacity / 100.0;
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

    private void ExecuteQuickPurge()
    {
        if (ViewPurge.IsCpuPurgeMode)
            _ = ExecuteCpuOptimization();
        else
            ExecuteRamPurge();
    }

    private void ExecuteRamPurge(bool isAutomatic = false)
    {
        if (_ramCooldownRemaining > 0) return;

        bool isEn = LocalizationService.IsEnglish;
        StatusFooterText.Text = isEn ? "Trimming process working sets..." : "Выполняется сброс рабочих наборов процессов...";

        try
        {
            var res = MemoryService.PurgeMemory();
            string freedStr = StorageService.FormatBytes(res.BytesFreed);
            string nowTime = DateTime.Now.ToString("HH:mm:ss");

            string logMsg = isEn
                ? $"[{nowTime}] Purged: {freedStr} physical RAM ({res.ProcessesOptimized} processes optimized). Was: {res.UsedPercentBefore:0}%, now: {res.UsedPercentAfter:0}%."
                : $"[{nowTime}] Очищено: {freedStr} физической памяти ({res.ProcessesOptimized} процессов оптимизировано). Было: {res.UsedPercentBefore:0}%, стало: {res.UsedPercentAfter:0}%.";

            ViewPurge.Log(logMsg);
            StatusFooterText.Text = isAutomatic ? $"[Auto-Purge] {freedStr} RAM freed." : (isEn ? $"Cleanup completed. Freed {freedStr} RAM." : $"Очистка завершена. Высвобождено {freedStr} RAM.");
        }
        catch (Exception ex)
        {
            Logger.Error("Ram purge failed", ex);
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

        bool isEn = LocalizationService.IsEnglish;
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
                    catch (Exception ex) { Logger.Debug($"CPU optimization skipped process: {ex.Message}"); }
                    finally { p.Dispose(); }
                }
            });

            await Task.Delay(500);
            HardwareTelemetry.Sample();
            await Task.Delay(800);
            double cpuAfter = HardwareTelemetry.Sample().CpuLoadPercent;

            string nowTime = DateTime.Now.ToString("HH:mm:ss");
            string status = (cpuAfter < cpuBefore)
                ? (isEn ? $"CPU optimized: {cpuBefore:0}% -> {cpuAfter:0}% ({optimizedCount} background utilities normalized)" : $"ЦП оптимизирован: {cpuBefore:0}% -> {cpuAfter:0}% ({optimizedCount} фоновых служб нормализовано)")
                : (isEn ? "CPU load did not change." : "Загрузка ЦП не изменилась.");

            ViewPurge.Log($"[{nowTime}] {status}");
            StatusFooterText.Text = status;
        }
        catch (Exception ex)
        {
            Logger.Error("CPU optimization error", ex);
        }
        finally
        {
            UpdateTelemetry();
            StartRamCooldown(3);
        }
    }

    private void StartRamCooldown(int seconds)
    {
        _ramCooldownRemaining = seconds;
        _ramCooldownTimer?.Stop();
        _ramCooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ramCooldownTimer.Tick += (s, e) =>
        {
            _ramCooldownRemaining--;
            if (_ramCooldownRemaining <= 0)
            {
                _ramCooldownTimer.Stop();
            }
        };
        _ramCooldownTimer.Start();
    }

    private void ExecuteGameBoost()
    {
        bool isEn = LocalizationService.IsEnglish;
        var res = GameSentinelService.BoostGames(!isEn);
        if (res.BoostedCount > 0)
        {
            StatusFooterText.Text = isEn
                ? $"Boosted priority for {res.BoostedCount} game process(es) to High ({string.Join(", ", res.AffectedGames)})."
                : $"Приоритет повышен до Высокого для {res.BoostedCount} процессов ({string.Join(", ", res.AffectedGames)}).";
        }
        else if (res.AlreadyHighCount > 0)
        {
            StatusFooterText.Text = isEn
                ? $"All active game processes ({res.AlreadyHighCount}) already have High priority."
                : $"Все активные игровые процессы ({res.AlreadyHighCount}) уже работают с высоким приоритетом.";
        }
        else
        {
            StatusFooterText.Text = isEn ? "No active games found to boost." : "Активные игровые процессы для оптимизации не найдены.";
        }
        PollActiveGames();
    }

    private void PollActiveGames()
    {
        var games = GameSentinelService.GetActiveGames(!LocalizationService.IsEnglish);
        ViewGames.UpdateActiveGames(games);
    }

    // Modern Confirm Dialog
    public async Task<bool> ShowModernConfirmDialogAsync(string title, string message, string confirmText, string cancelText, string iconKey)
    {
        ModalDialogTitle.Text = title;
        ModalDialogMessage.Text = message;
        BtnModalConfirm.Content = confirmText;
        BtnModalCancel.Content = cancelText;

        if (TryFindResource(iconKey) is Geometry g)
        {
            ModalIconPath.Data = g;
        }

        ModernModalOverlay.Visibility = Visibility.Visible;
        _modalTcs = new TaskCompletionSource<bool>();
        bool result = await _modalTcs.Task;
        ModernModalOverlay.Visibility = Visibility.Collapsed;
        return result;
    }

    private void BtnModalConfirm_Click(object sender, RoutedEventArgs e) => _modalTcs?.TrySetResult(true);
    private void BtnModalCancel_Click(object sender, RoutedEventArgs e) => _modalTcs?.TrySetResult(false);

    // Dropdown helpers
    private void ShowDiskDropdown(FrameworkElement target)
    {
        var cm = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        bool isEn = LocalizationService.IsEnglish;
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady);
        foreach (var d in drives)
        {
            string letter = d.Name.TrimEnd(':', '\\');
            string vol = string.IsNullOrWhiteSpace(d.VolumeLabel)
                ? (letter == "C" ? (isEn ? "System" : "Системный") : (isEn ? "Data" : "Данные"))
                : d.VolumeLabel;
            string freeStr = StorageService.FormatBytes(d.AvailableFreeSpace);
            bool isCurrent = string.Equals(letter, _watchedDriveLetter, StringComparison.OrdinalIgnoreCase);

            var item = new MenuItem
            {
                Header = isEn ? $"Drive {letter}: [{vol}] · {freeStr} free" : $"Диск {letter}: [{vol}] · свободно {freeStr}",
                FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal
            };
            item.Click += (s, ev) =>
            {
                _watchedDriveLetter = letter;
                SaveCurrentUserSettings();
                UpdateTelemetry();
                ApplyLanguageStrings();
                _ = ViewStorage.ScanJunkAsync(showLog: false);
            };
            cm.Items.Add(item);
        }
        cm.IsOpen = true;
    }

    private void ShowPurgeModeDropdown()
    {
        var cm = new ContextMenu { PlacementTarget = ViewPurge.BtnSwitchPurgeMode, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        bool isEn = LocalizationService.IsEnglish;

        var itemRam = new MenuItem
        {
            Header = isEn ? "Memory Purge (RAM)" : "Очистка оперативной памяти (ОЗУ)",
            FontWeight = !_isCpuPurgeMode ? FontWeights.Bold : FontWeights.Normal
        };
        itemRam.Click += (s, e) => SetPurgeTabMode(false);

        var itemCpu = new MenuItem
        {
            Header = isEn ? "CPU Optimization" : "Оптимизация процессора (ЦП)",
            FontWeight = _isCpuPurgeMode ? FontWeights.Bold : FontWeights.Normal
        };
        itemCpu.Click += (s, e) => SetPurgeTabMode(true);

        cm.Items.Add(itemRam);
        cm.Items.Add(itemCpu);
        cm.IsOpen = true;
    }

    private void ShowGameDriveFilterDropdown(FrameworkElement target)
    {
        var cm = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        bool isEn = LocalizationService.IsEnglish;

        var allItem = new MenuItem
        {
            Header = isEn ? "All Drives" : "Все диски",
            FontWeight = ViewGames.GameDriveFilter == "ALL" ? FontWeights.Bold : FontWeights.Normal
        };
        allItem.Click += (s, e) => ViewGames.UpdateDriveFilter("ALL");
        cm.Items.Add(allItem);

        var drives = DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable));
        foreach (var d in drives)
        {
            string letter = d.Name.TrimEnd(':', '\\');
            var item = new MenuItem
            {
                Header = isEn ? $"Drive {letter}:" : $"Диск {letter}:",
                FontWeight = ViewGames.GameDriveFilter.Equals(letter, StringComparison.OrdinalIgnoreCase) ? FontWeights.Bold : FontWeights.Normal
            };
            item.Click += (s, e) => ViewGames.UpdateDriveFilter(letter);
            cm.Items.Add(item);
        }
        cm.IsOpen = true;
    }

    private void ShowIntervalDropdown(FrameworkElement target)
    {
        var cm = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        bool isEn = LocalizationService.IsEnglish;

        var presets = new (double Sec, string EnLabel, string RuLabel)[]
        {
            (0.5, "0.5s (High Precision)", "0.5 сек (Высокая точность)"),
            (1.0, "1.0s (Fast)", "1.0 сек (Быстро)"),
            (1.5, "1.5s (Default)", "1.5 сек (По умолчанию)"),
            (3.0, "3.0s (Eco)", "3.0 сек (Эко)"),
            (5.0, "5.0s (Power Saver)", "5.0 сек (Энергосбережение)")
        };

        void ApplyInterval(double sec)
        {
            _telemetryIntervalSeconds = sec;
            _telemetryTimer.Interval = TimeSpan.FromSeconds(sec);
            SaveCurrentUserSettings();
            ViewSettings.InitSettings(_settings, _isLightTheme);
            StatusFooterText.Text = isEn ? $"Telemetry interval: {sec:0.#}s." : $"Интервал телеметрии: {sec:0.#} сек.";
        }

        foreach (var p in presets)
        {
            var item = new MenuItem
            {
                Header = isEn ? p.EnLabel : p.RuLabel,
                FontWeight = Math.Abs(_telemetryIntervalSeconds - p.Sec) < 0.05 ? FontWeights.Bold : FontWeights.Normal
            };
            item.Click += (s, e) => ApplyInterval(p.Sec);
            cm.Items.Add(item);
        }

        // Inline Custom item
        var customPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var customLabel = new TextBlock
        {
            Text = isEn ? "Custom (s): " : "Своё (сек): ",
            Foreground = (Brush)FindResource("BrushTextPrimary"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var customBox = new TextBox
        {
            Text = _telemetryIntervalSeconds.ToString("0.#", CultureInfo.InvariantCulture),
            Width = 54,
            Height = 22,
            Padding = new Thickness(4, 1, 4, 1),
            FontSize = 11.5,
            Background = (Brush)FindResource("BrushBgInner"),
            Foreground = (Brush)FindResource("BrushTextPrimary"),
            BorderBrush = (Brush)FindResource("BrushBorderCard"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };

        customBox.KeyDown += (s, ev) =>
        {
            if (ev.Key == Key.Enter)
            {
                if (double.TryParse(customBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double sec)
                    && sec >= 0.2 && sec <= 60.0)
                {
                    ApplyInterval(sec);
                    cm.IsOpen = false;
                }
                ev.Handled = true;
            }
        };

        customPanel.Children.Add(customLabel);
        customPanel.Children.Add(customBox);

        cm.Items.Add(new MenuItem { Header = customPanel, StaysOpenOnClick = true });
        cm.Opened += (s, ev) =>
        {
            customBox.Focus();
            customBox.SelectAll();
        };
        cm.IsOpen = true;
    }

    private void ShowThresholdDropdown(FrameworkElement target)
    {
        var cm = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        bool isEn = LocalizationService.IsEnglish;

        var presets = new (double Gb, string EnLabel, string RuLabel)[]
        {
            (3.0, "3 GB", "3 ГБ"),
            (5.0, "5 GB", "5 ГБ"),
            (7.0, "7 GB (Default)", "7 ГБ (По умолчанию)"),
            (10.0, "10 GB", "10 ГБ"),
            (15.0, "15 GB", "15 ГБ")
        };

        void ApplyThreshold(double gb)
        {
            _diskCThresholdGb = gb;
            SaveCurrentUserSettings();
            ViewSettings.InitSettings(_settings, _isLightTheme);
            UpdateTelemetry();
            StatusFooterText.Text = isEn ? $"Disk warning threshold set to {gb:0.#} GB." : $"Порог предупреждения: {gb:0.#} ГБ.";
        }

        foreach (var p in presets)
        {
            var item = new MenuItem
            {
                Header = isEn ? p.EnLabel : p.RuLabel,
                FontWeight = Math.Abs(_diskCThresholdGb - p.Gb) < 0.1 ? FontWeights.Bold : FontWeights.Normal
            };
            item.Click += (s, e) => ApplyThreshold(p.Gb);
            cm.Items.Add(item);
        }

        // Inline Custom item
        var customPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var customLabel = new TextBlock
        {
            Text = isEn ? "Custom (GB): " : "Своё (ГБ): ",
            Foreground = (Brush)FindResource("BrushTextPrimary"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var customBox = new TextBox
        {
            Text = _diskCThresholdGb.ToString("0.#", CultureInfo.InvariantCulture),
            Width = 54,
            Height = 22,
            Padding = new Thickness(4, 1, 4, 1),
            FontSize = 11.5,
            Background = (Brush)FindResource("BrushBgInner"),
            Foreground = (Brush)FindResource("BrushTextPrimary"),
            BorderBrush = (Brush)FindResource("BrushBorderCard"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };

        customBox.KeyDown += (s, ev) =>
        {
            if (ev.Key == Key.Enter)
            {
                if (double.TryParse(customBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double gb)
                    && gb >= 1.0 && gb <= 500.0)
                {
                    ApplyThreshold(gb);
                    cm.IsOpen = false;
                }
                ev.Handled = true;
            }
        };

        customPanel.Children.Add(customLabel);
        customPanel.Children.Add(customBox);

        cm.Items.Add(new MenuItem { Header = customPanel, StaysOpenOnClick = true });
        cm.Opened += (s, ev) =>
        {
            customBox.Focus();
            customBox.SelectAll();
        };
        cm.IsOpen = true;
    }

    private async void ResetSettings()
    {
        try
        {
            bool isEn = LocalizationService.IsEnglish;
            string msg = isEn
                ? "Are you sure you want to reset all parameters, alert thresholds and tab layouts to factory defaults?"
                : "Вы уверены, что хотите сбросить все параметры, пороги алертов и навигационные вкладки на значения по умолчанию?";
            bool ok = await ShowModernConfirmDialogAsync(isEn ? "Reset Confirmation" : "Подтверждение сброса", msg, isEn ? "Reset" : "Сбросить", isEn ? "Cancel" : "Отмена", "IconArrowClockwise");
            if (!ok) return;

            _isEditingTabs = false;
            ViewSettings.SetTabEditState(false);
            ViewSettings.SetVaultDrawerOpen(false);
            ViewSettings.CollapseAllAccordions();

            _settings = new UserSettingsModel();
            SettingsPersistenceService.Save(_settings);

            LoadAndApplyUserSettings();
            InitNavigationTabs();
            _telemetryTimer.Interval = TimeSpan.FromSeconds(_telemetryIntervalSeconds);
            ApplyLanguageStrings();
            SetTheme(_isLightTheme);
            UpdateTelemetry();
            UpdateFooterClock();
            _ = ViewStorage.ScanJunkAsync(showLog: false);

            StatusFooterText.Text = "Settings have been reset to factory defaults.";
        }
        catch (Exception ex)
        {
            Logger.Error("Reset settings failed", ex);
        }
    }

    // Navigation Tabs & Vault
    private void InitNavigationTabs()
    {
        string storageTitleEn = $"Storage ({_watchedDriveLetter}:)";
        string storageTitleRu = $"Хранилище ({_watchedDriveLetter}:)";

        var allKnownTabs = new Dictionary<string, TabItemModel>
        {
            ["overview"] = new() { Key = "overview", TagIndex = 0, EnTitle = "Overview", RuTitle = "Обзор", IconKey = "IconSquaresFour" },
            ["ram"] = new()
            {
                Key = "ram",
                TagIndex = 1,
                EnTitle = _isCpuPurgeMode ? "CPU Optimization" : "RAM Purge",
                RuTitle = _isCpuPurgeMode ? "Оптимизация ЦП" : "Очистка ОЗУ",
                IconKey = _isCpuPurgeMode ? "IconCpu" : "IconRam"
            },
            ["storage"] = new() { Key = "storage", TagIndex = 2, EnTitle = storageTitleEn, RuTitle = storageTitleRu, IconKey = "IconHardDrive" },
            ["games"] = new() { Key = "games", TagIndex = 3, EnTitle = "Game Sentinel", RuTitle = "Игровой модуль", IconKey = "IconGamepad" },
            ["settings"] = new() { Key = "settings", TagIndex = 4, EnTitle = "Settings", RuTitle = "Настройки", IconKey = "IconGear" }
        };

        _activeTabs.Clear();
        _vaultTabs.Clear();

        var savedActive = _settings.ActiveTabs ?? new List<string> { "overview", "ram", "storage", "games", "settings" };
        foreach (var key in savedActive)
        {
            if (allKnownTabs.TryGetValue(key, out var tab))
            {
                _activeTabs.Add(tab);
                allKnownTabs.Remove(key);
            }
        }

        if (!_activeTabs.Any(t => t.Key == "settings") && allKnownTabs.TryGetValue("settings", out var settingsTab))
        {
            _activeTabs.Add(settingsTab);
            allKnownTabs.Remove("settings");
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

        RebuildNavPills();
        RefreshVaultUI();
    }

    private void RebuildNavPills()
    {
        if (NavPillsContainer == null) return;
        NavPillsContainer.Children.Clear();
        bool isEn = LocalizationService.IsEnglish;

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

            var btn = new Button
            {
                Style = navPillStyle,
                Tag = tab.TagIndex,
                Background = isSelected ? activeBrush : inactiveBrush,
                BorderBrush = isSelected ? activeBorder : inactiveBorder,
                Foreground = isSelected ? activeText : inactiveText,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            btn.Click += (s, e) => HighlightTab(tab.TagIndex);

            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (TryFindResource(tab.IconKey) is Geometry geom)
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

            var txt = new TextBlock
            {
                Text = isEn ? tab.EnTitle : tab.RuTitle,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            sp.Children.Add(txt);
            btn.Content = sp;

            tab.PillButton = btn;
            tab.TitleTextBlock = txt;
            Grid.SetColumn(btn, 0);
            rowGrid.Children.Add(btn);

            if (_isEditingTabs)
            {
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
                    btnDel.Content = new System.Windows.Shapes.Path
                    {
                        Data = (Geometry)FindResource("IconClose"),
                        Width = 9,
                        Height = 9,
                        Fill = (SolidColorBrush)FindResource("BrushTextSecondary"),
                        Stretch = Stretch.Uniform
                    };
                    btnDel.Click += BtnRemoveTab_Click;
                    Grid.SetColumn(btnDel, 1);
                    rowGrid.Children.Add(btnDel);
                }

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
                gripBorder.Child = new System.Windows.Shapes.Path
                {
                    Data = (Geometry)FindResource("IconGripLines"),
                    Width = 12,
                    Height = 12,
                    Fill = (SolidColorBrush)FindResource("BrushTextMuted"),
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
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

    public void HighlightTab(int index)
    {
        _currentTabIndex = index;

        if (index != 4)
        {
            DisableTabEditMode();
        }

        // Toggle user controls
        ViewOverview.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewPurge.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ViewStorage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        ViewGames.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        ViewSettings.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;

        if (index == 2 && (DateTime.UtcNow - ViewStorage.LastScanUtc).TotalSeconds >= 10)
        {
            _ = ViewStorage.ScanJunkAsync(showLog: false, isManualClick: false, isBackground: true);
        }

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
                if (tab.TitleTextBlock != null)
                {
                    tab.TitleTextBlock.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium;
                }
            }
        }
    }

    private void DisableTabEditMode()
    {
        if (!_isEditingTabs) return;
        _isEditingTabs = false;
        ViewSettings.SetTabEditState(false);
        RebuildNavPills();
    }

    private void ToggleTabEditMode()
    {
        _isEditingTabs = !_isEditingTabs;
        bool isEn = LocalizationService.IsEnglish;
        ViewSettings.SetTabEditState(_isEditingTabs);
        RebuildNavPills();
        StatusFooterText.Text = _isEditingTabs
            ? (isEn ? "Tab edit mode active. Drag ☰ to reorder, ✕ to remove." : "Режим редактирования вкладок: зажимайте ☰ для перемещения, ✕ для удаления.")
            : (isEn ? "Tab order saved." : "Порядок вкладок сохранен.");
    }

    private void ToggleTabVault()
    {
        bool willOpen = !ViewSettings.IsVaultOpen;
        ViewSettings.SetVaultDrawerOpen(willOpen);
        if (willOpen)
        {
            RefreshVaultUI();
        }
        bool isEn = LocalizationService.IsEnglish;
        StatusFooterText.Text = willOpen
            ? (isEn ? "Tab vault opened." : "Хранилище вкладок открыто.")
            : (isEn ? "Tab vault closed." : "Хранилище вкладок закрыто.");
    }

    private void RefreshVaultUI()
    {
        bool isEn = LocalizationService.IsEnglish;
        var entries = _vaultTabs.Select(t => new TabVaultEntry(t.Key, isEn ? t.EnTitle : t.RuTitle, t.IconKey));
        ViewSettings.RefreshVaultItems(entries);
    }

    private async void BtnRemoveTab_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && fe.Tag is TabItemModel tab)
            {
                bool isEn = LocalizationService.IsEnglish;
                string title = isEn ? "Remove Tab?" : "Удалить вкладку?";
                string msg = isEn
                    ? $"Move tab '{tab.EnTitle}' to Tab Storage? You can restore it anytime in Settings."
                    : $"Переместить вкладку «{tab.RuTitle}» в Хранилище вкладок? Вы сможете вернуть её в любой момент через настройки.";

                bool ok = await ShowModernConfirmDialogAsync(title, msg, isEn ? "Remove" : "Удалить", isEn ? "Cancel" : "Отмена", "IconClose");
                if (!ok) return;

                _activeTabs.Remove(tab);
                if (!_vaultTabs.Contains(tab))
                {
                    _vaultTabs.Add(tab);
                }

                if (_currentTabIndex == tab.TagIndex)
                {
                    var nextTab = _activeTabs.FirstOrDefault();
                    if (nextTab != null) HighlightTab(nextTab.TagIndex);
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

    private void RestoreTabByKey(string key)
    {
        var tab = _vaultTabs.FirstOrDefault(t => t.Key == key);
        if (tab == null) return;

        _vaultTabs.Remove(tab);
        if (!_activeTabs.Contains(tab))
        {
            _activeTabs.Add(tab);
        }

        RebuildNavPills();
        RefreshVaultUI();
        SaveCurrentUserSettings();

        bool isEn = LocalizationService.IsEnglish;
        StatusFooterText.Text = isEn
            ? $"Tab '{tab.EnTitle}' restored to navigation."
            : $"Вкладка «{tab.RuTitle}» добавлена в навигацию.";
    }

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

        double rowH = _draggedRow.ActualHeight > 0 ? _draggedRow.ActualHeight + 4 : 40;
        int slotOffset = (int)Math.Round(deltaY / rowH);
        _targetDropIndex = Math.Clamp(_dragStartIndex + slotOffset, 0, _activeTabs.Count - 1);

        for (int i = 0; i < NavPillsContainer.Children.Count; i++)
        {
            if (i == _dragStartIndex) continue;
            var child = (UIElement)NavPillsContainer.Children[i];
            if (_dragStartIndex < _targetDropIndex)
            {
                child.RenderTransform = (i > _dragStartIndex && i <= _targetDropIndex) ? new TranslateTransform(0, -rowH) : null;
            }
            else if (_dragStartIndex > _targetDropIndex)
            {
                child.RenderTransform = (i >= _targetDropIndex && i < _dragStartIndex) ? new TranslateTransform(0, rowH) : null;
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

    private void TabGrip_LostMouseCapture(object sender, MouseEventArgs e) => CommitDrop();

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

    // Window Caption Handlers
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
        }
        else if (WindowState != WindowState.Maximized && e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
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

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void CpuMetric_Click(object sender, MouseButtonEventArgs e)
    {
        SetPurgeTabMode(isCpu: true);
        HighlightTab(1);
    }
    private void RamMetric_Click(object sender, MouseButtonEventArgs e)
    {
        SetPurgeTabMode(isCpu: false);
        HighlightTab(1);
    }
    private void DiskMetric_Click(object sender, MouseButtonEventArgs e) => OpenDriveExplorer(HardwareTelemetry.GetSystemDriveLetter());
    private void GameModeIndicator_Click(object sender, MouseButtonEventArgs e) => HighlightTab(3);
    private void BtnToggleHud_Click(object sender, RoutedEventArgs e) => ToggleHudWindow();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (ModernModalOverlay.Visibility == Visibility.Visible)
            {
                _modalTcs?.TrySetResult(false);
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveCurrentUserSettings();
        _telemetryTimer.Stop();
        _clockTimer.Stop();
        ViewStorage.StopAutoScan();
        if (_hudWindow != null && _hudWindow.IsLoaded)
        {
            _hudWindow.Close();
        }
        base.OnClosed(e);
    }
}
