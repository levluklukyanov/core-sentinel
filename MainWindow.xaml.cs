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
using System.Windows.Threading;
using CoreSentinel.Services;

namespace CoreSentinel;

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
    private bool _autoGamePriorityEnabled = true;
    private string _watchedDriveLetter = "C";
    private string _currentLanguage = "EN";
    private bool _isLightTheme = false;
    private bool _use12HourTime = true;
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

        ViewPurge.Log(LocalizationService.IsRussian
            ? "CoreSentinel инициализирован. Фоновая телеметрия и мониторинг активны."
            : "CoreSentinel initialized. Background telemetry and security monitoring active.");

        _ = ViewStorage.ScanJunkAsync(showLog: false);
    }

    private void WireViewEvents()
    {
        // Overview View
        ViewOverview.OnCpuClicked += () => LaunchTaskManager();
        ViewOverview.OnRamClicked += () =>
        {
            ViewPurge.SetPurgeMode(false);
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
        ViewStorage.ShowConfirmDialogAsync = ShowModernConfirmDialogAsync;

        // Games View
        ViewGames.OnBoostPriorityRequested += () => ExecuteGameBoost();
        ViewGames.OnRefreshActiveGamesRequested += () => PollActiveGames();
        ViewGames.OnGameDriveFilterRequested += fe => ShowGameDriveFilterDropdown(fe);
        ViewGames.OnStatusChanged += msg => StatusFooterText.Text = msg;

        // Settings View
        ViewSettings.OnLanguageChanged += lang => SetLanguage(lang);
        ViewSettings.OnThemeChanged += isLight => SetTheme(isLight);
        ViewSettings.OnTimeFormatChanged += is12h => SetTimeFormat(is12h);
        ViewSettings.OnHudOpacityChanged += val => SetHudOpacity(val);
        ViewSettings.OnResetSettingsRequested += () => ResetSettings();
        ViewSettings.OnPurgeRamRequested += () => ExecuteRamPurge();
        ViewSettings.OnIntervalSelectorRequested += fe => ShowIntervalDropdown(fe);
        ViewSettings.OnThresholdSelectorRequested += fe => ShowThresholdDropdown(fe);
        ViewSettings.OnToggleTabEditRequested += () => ToggleTabEditMode();
        ViewSettings.OnOpenTabVaultRequested += () => ToggleTabVault();
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
        var utcNow = DateTime.UtcNow;
        var offset = TimeZoneInfo.Local.GetUtcOffset(now);
        string utcOffsetStr = offset.Hours >= 0 ? $"+{offset.Hours}" : $"{offset.Hours}";
        if (offset.Minutes != 0) utcOffsetStr += $":{Math.Abs(offset.Minutes):00}";

        string timePattern = _use12HourTime ? "hh:mm:ss tt" : "HH:mm:ss";
        string localTimeFormatted = now.ToString(timePattern, CultureInfo.InvariantCulture);
        string utcTimeFormatted = utcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        FooterClockText.Text = $"{localTimeFormatted} (UTC{utcOffsetStr} · {utcTimeFormatted} UTC)";
    }

    private void UpdateTelemetry()
    {
        try
        {
            var sample = HardwareTelemetry.Sample();
            bool isEn = LocalizationService.IsEnglish;

            // Quick metrics header
            QuickCpuText.Text = $"{sample.CpuLoadPercent:0}%";
            QuickRamText.Text = $"{sample.RamUsedPercent:0}%";
            QuickDiskText.Text = $"{sample.DiskCFreeGb:0.0} GB";
            QuickDiskMetric.ToolTip = isEn
                ? $"Open Drive {_watchedDriveLetter}: in Windows Explorer"
                : $"Открыть диск {_watchedDriveLetter}: в Проводнике";

            double ramUsedGb = sample.RamUsedBytes / (1024.0 * 1024 * 1024);
            double ramTotalGb = sample.RamTotalBytes / (1024.0 * 1024 * 1024);

            // Health determination
            string healthBadge = isEn ? "Healthy" : "В норме";
            string recommendation = isEn ? "All systems operating nominally" : "Все системы работают стабильно";
            if (sample.CpuLoadPercent >= _cpuAlertThreshold || sample.RamUsedPercent >= _ramAlertThreshold)
            {
                healthBadge = isEn ? "Warning" : "Внимание";
                recommendation = isEn ? "High system resource consumption" : "Высокая нагрузка на ресурсы";
            }
            else if (sample.DiskCFreeGb < _diskCThresholdGb)
            {
                healthBadge = isEn ? "Low Space" : "Мало места";
                recommendation = isEn ? $"Free space on {_watchedDriveLetter}: below threshold" : $"На диске {_watchedDriveLetter}: осталось мало места";
            }

            // Game info
            var activeGames = GameSentinelService.GetActiveGames(!isEn);
            string gameBadge = activeGames.Count > 0 ? (isEn ? "Active" : "Активен") : (isEn ? "Waiting" : "Ожидание");
            string gameDetail = activeGames.Count > 0
                ? $"{activeGames[0].DisplayName} (PID: {activeGames[0].Pid})"
                : (isEn ? "No games detected · Background monitoring active" : "Игры не обнаружены · Мониторинг активен");

            // Update Views
            ViewOverview.UpdateMetrics(
                sample.CpuLoadPercent,
                HardwareTelemetry.GetProcessorName(),
                sample.RamUsedPercent,
                ramUsedGb,
                ramTotalGb,
                _watchedDriveLetter,
                sample.DiskCFreeGb,
                sample.DiskCTotalGb,
                sample.HasSecondaryDataDrive ? sample.DataDriveLetter : null,
                sample.DiskDFreeGb,
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

            ViewStorage.UpdateCapacity(_watchedDriveLetter, sample.DiskCFreeGb, sample.DiskCTotalGb);
            ViewGames.UpdateActiveGames(activeGames);

            // Auto-RAM purge
            if (_autoRamPurgeEnabled && sample.RamUsedPercent > _ramAlertThreshold)
            {
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
        bool isEn = LocalizationService.IsEnglish;
        NavHeaderTitle.Text = LocalizationService.Get("Nav.Navigation");

        foreach (var tab in _activeTabs.Concat(_vaultTabs))
        {
            if (tab.Key == "storage")
            {
                tab.EnTitle = $"Storage ({_watchedDriveLetter}:)";
                tab.RuTitle = $"Хранилище ({_watchedDriveLetter}:)";
            }
        }
        RebuildNavPills();

        ViewOverview.ApplyLanguage(_currentLanguage);
        ViewPurge.UpdateVisuals();
        ViewStorage.ApplyLanguage(_currentLanguage);
        ViewGames.ApplyLanguage(_currentLanguage);
        ViewSettings.ApplyLanguage(_currentLanguage);
    }

    public void SetLanguage(string lang)
    {
        _currentLanguage = lang == "RU" ? "RU" : "EN";
        LocalizationService.CurrentLanguage = _currentLanguage;
        SaveCurrentUserSettings();
        ApplyLanguageStrings();
    }

    public void SetTheme(bool isLight)
    {
        _isLightTheme = isLight;
        try
        {
            var uri = new Uri(isLight ? "Themes/AppTheme.Light.xaml" : "Themes/AppTheme.xaml", UriKind.Relative);
            Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };
        }
        catch (Exception ex)
        {
            Logger.Warn($"Theme apply warning: {ex.Message}");
        }

        if (BrandLogoImage != null)
        {
            try
            {
                BrandLogoImage.Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri(isLight ? "logo_dark.png" : "logo.png", UriKind.Relative));
            }
            catch { }
        }

        SaveCurrentUserSettings();
        RebuildNavPills();
    }

    public void SetTimeFormat(bool is12h)
    {
        _use12HourTime = is12h;
        SaveCurrentUserSettings();
        UpdateFooterClock();
    }

    public void SetHudOpacity(double val)
    {
        if (_hudWindow != null && _hudWindow.IsLoaded)
        {
            _hudWindow.Opacity = val / 100.0;
        }
        SaveCurrentUserSettings();
    }

    private void LaunchTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
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
            Process.Start(new ProcessStartInfo("explorer.exe", $"{driveLetter}:\\") { UseShellExecute = true });
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
                    catch { }
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
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
        foreach (var d in drives)
        {
            string letter = d.Name.TrimEnd(':', '\\');
            var item = new MenuItem { Header = $"Drive {letter}: ({d.TotalSize / (1024 * 1024 * 1024)} GB)" };
            item.Click += (s, ev) =>
            {
                _watchedDriveLetter = letter;
                SaveCurrentUserSettings();
                UpdateTelemetry();
                ApplyLanguageStrings();
            };
            cm.Items.Add(item);
        }
        cm.IsOpen = true;
    }

    private void ShowPurgeModeDropdown()
    {
        var cm = new ContextMenu { PlacementTarget = ViewPurge.BtnSwitchPurgeMode, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        bool isEn = LocalizationService.IsEnglish;

        var itemRam = new MenuItem { Header = isEn ? "Memory Purge (RAM)" : "Очистка оперативной памяти (ОЗУ)" };
        itemRam.Click += (s, e) => ViewPurge.SetPurgeMode(false);

        var itemCpu = new MenuItem { Header = isEn ? "CPU Optimization" : "Оптимизация процессора (ЦП)" };
        itemCpu.Click += (s, e) => ViewPurge.SetPurgeMode(true);

        cm.Items.Add(itemRam);
        cm.Items.Add(itemCpu);
        cm.IsOpen = true;
    }

    private void ShowGameDriveFilterDropdown(FrameworkElement target)
    {
        var cm = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        bool isEn = LocalizationService.IsEnglish;

        var allItem = new MenuItem { Header = isEn ? "All Drives" : "Все диски" };
        allItem.Click += (s, e) => ViewGames.UpdateDriveFilter("ALL");
        cm.Items.Add(allItem);

        var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
        foreach (var d in drives)
        {
            string letter = d.Name.TrimEnd(':', '\\');
            var item = new MenuItem { Header = isEn ? $"Drive {letter}:" : $"Диск {letter}:" };
            item.Click += (s, e) => ViewGames.UpdateDriveFilter(letter);
            cm.Items.Add(item);
        }
        cm.IsOpen = true;
    }

    private void ShowIntervalDropdown(FrameworkElement target)
    {
        var cm = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        double[] intervals = { 0.5, 1.0, 1.5, 2.0, 3.0, 5.0 };
        foreach (var sec in intervals)
        {
            var item = new MenuItem { Header = $"{sec:0.#}s" };
            item.Click += (s, e) =>
            {
                _telemetryIntervalSeconds = sec;
                _telemetryTimer.Interval = TimeSpan.FromSeconds(sec);
                SaveCurrentUserSettings();
                ViewSettings.InitSettings(_settings, _isLightTheme);
            };
            cm.Items.Add(item);
        }
        cm.IsOpen = true;
    }

    private void ShowThresholdDropdown(FrameworkElement target)
    {
        var cm = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        double[] thresholds = { 3.0, 5.0, 7.0, 10.0, 15.0, 20.0 };
        foreach (var gb in thresholds)
        {
            var item = new MenuItem { Header = $"{gb:0.#} GB" };
            item.Click += (s, e) =>
            {
                _diskCThresholdGb = gb;
                SaveCurrentUserSettings();
                ViewSettings.InitSettings(_settings, _isLightTheme);
            };
            cm.Items.Add(item);
        }
        cm.IsOpen = true;
    }

    private async void ResetSettings()
    {
        bool isEn = LocalizationService.IsEnglish;
        string msg = isEn 
            ? "Are you sure you want to reset all parameters, alert thresholds and tab layouts to factory defaults?" 
            : "Вы уверены, что хотите сбросить все параметры, пороги алертов и навигационные вкладки на значения по умолчанию?";
        bool ok = await ShowModernConfirmDialogAsync(isEn ? "Reset Confirmation" : "Подтверждение сброса", msg, isEn ? "Reset" : "Сбросить", isEn ? "Cancel" : "Отмена", "IconArrowClockwise");
        if (!ok) return;

        SetLanguage("EN");
        SetTheme(false);
        SetTimeFormat(true);
        _telemetryIntervalSeconds = 1.0;
        _diskCThresholdGb = 7.0;
        _autoRamPurgeEnabled = true;
        _autoGamePriorityEnabled = true;

        SaveCurrentUserSettings();
        LoadAndApplyUserSettings();
        StatusFooterText.Text = isEn ? "Settings have been reset to factory defaults." : "Настройки сброшены на значения по умолчанию.";
    }

    // Navigation Tabs
    private void InitNavigationTabs()
    {
        var master = new List<TabItemModel>
        {
            new() { Key = "overview", TagIndex = 0, EnTitle = "Overview", RuTitle = "Обзор", IconKey = "IconGrid" },
            new() { Key = "ram", TagIndex = 1, EnTitle = "RAM Purge", RuTitle = "Очистка ОЗУ", IconKey = "IconRam" },
            new() { Key = "storage", TagIndex = 2, EnTitle = $"Storage ({_watchedDriveLetter}:)", RuTitle = $"Хранилище ({_watchedDriveLetter}:)", IconKey = "IconHardDrive" },
            new() { Key = "games", TagIndex = 3, EnTitle = "Game Sentinel", RuTitle = "Игровой модуль", IconKey = "IconGamepad" },
            new() { Key = "settings", TagIndex = 4, EnTitle = "Settings", RuTitle = "Настройки", IconKey = "IconGear" }
        };

        _activeTabs.Clear();
        _vaultTabs.Clear();

        var savedActive = _settings.ActiveTabs ?? new List<string> { "overview", "ram", "storage", "games", "settings" };
        foreach (var key in savedActive)
        {
            var tab = master.FirstOrDefault(t => t.Key == key);
            if (tab != null && !_activeTabs.Contains(tab)) _activeTabs.Add(tab);
        }

        foreach (var tab in master)
        {
            if (!_activeTabs.Contains(tab)) _vaultTabs.Add(tab);
        }

        RebuildNavPills();
    }

    private void RebuildNavPills()
    {
        NavPillsContainer.Children.Clear();
        bool isEn = LocalizationService.IsEnglish;

        for (int i = 0; i < _activeTabs.Count; i++)
        {
            var tab = _activeTabs[i];
            var btn = new Button
            {
                Style = (Style)FindResource("BtnNavPill"),
                Tag = tab.TagIndex,
                Height = 36,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (TryFindResource(tab.IconKey) is Geometry g)
            {
                var icon = new System.Windows.Shapes.Path
                {
                    Data = g,
                    Width = 14,
                    Height = 14,
                    Fill = (Brush)FindResource("BrushTextSecondary"),
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(icon, 0);
                grid.Children.Add(icon);
            }

            var txt = new TextBlock
            {
                Text = isEn ? tab.EnTitle : tab.RuTitle,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("BrushTextSecondary"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(txt, 1);
            grid.Children.Add(txt);

            btn.Content = grid;
            btn.Click += (s, e) => HighlightTab(tab.TagIndex);

            tab.PillButton = btn;
            tab.TitleTextBlock = txt;
            NavPillsContainer.Children.Add(btn);
        }

        HighlightTab(_currentTabIndex);
    }

    public void HighlightTab(int index)
    {
        _currentTabIndex = index;

        // Toggle user controls
        ViewOverview.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewPurge.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ViewStorage.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        ViewGames.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        ViewSettings.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;

        // Update pills styling
        foreach (var tab in _activeTabs)
        {
            if (tab.PillButton != null)
            {
                bool isSelected = tab.TagIndex == index;
                tab.PillButton.Style = (Style)FindResource(isSelected ? "BtnNavPillActive" : "BtnNavPill");
                if (tab.TitleTextBlock != null)
                {
                    tab.TitleTextBlock.Foreground = (Brush)FindResource(isSelected ? "BrushTextPrimary" : "BrushTextSecondary");
                    tab.TitleTextBlock.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium;
                }
            }
        }
    }

    private void ToggleTabEditMode()
    {
        _isEditingTabs = !_isEditingTabs;
        bool isEn = LocalizationService.IsEnglish;
        StatusFooterText.Text = _isEditingTabs
            ? (isEn ? "Tab edit mode active." : "Режим редактирования вкладок активен.")
            : (isEn ? "Tab edit mode finished." : "Режим редактирования вкладок завершен.");
    }

    private void ToggleTabVault()
    {
        bool isEn = LocalizationService.IsEnglish;
        StatusFooterText.Text = isEn ? "Tab vault opened." : "Хранилище вкладок открыто.";
    }

    // Window Caption Handlers
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BtnMaximize_Click(sender, e);
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void CpuMetric_Click(object sender, MouseButtonEventArgs e) => LaunchTaskManager();
    private void RamMetric_Click(object sender, MouseButtonEventArgs e)
    {
        ViewPurge.SetPurgeMode(false);
        HighlightTab(1);
    }
    private void DiskMetric_Click(object sender, MouseButtonEventArgs e) => OpenDriveExplorer(_watchedDriveLetter);
    private void BtnToggleHud_Click(object sender, RoutedEventArgs e) => ToggleHudWindow();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
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
        if (_hudWindow != null && _hudWindow.IsLoaded)
        {
            _hudWindow.Close();
        }
        base.OnClosed(e);
    }
}
