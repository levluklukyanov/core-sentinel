using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CoreSpectre.Services;
using CoreSpectre.ViewModels;

namespace CoreSpectre.Views;

public partial class StorageView : UserControl
{
    private bool _isStorageOperationRunning = false;
    private bool _isScanningTargets = false;
    private int _storageCooldownRemaining = 0;
    private DispatcherTimer? _storageCooldownTimer;
    private readonly DispatcherTimer _autoScanTimer;
    private DateTime _lastScanUtc = DateTime.MinValue;
    private string _watchedDriveLetter = "C";
    private string _currentSortMode = "SizeDesc";
    private UserSettingsModel _settings = new();
    private Action? _saveSettingsCallback;

    private List<CleanTargetItem> _cachedTargets = new();

    public DateTime LastScanUtc => _lastScanUtc;

    public event Action<FrameworkElement>? OnDiskDropdownRequested;
    public event Action<string>? OnStatusChanged;
    public event Action? OnCleanupCompleted;
    public Func<string, string, string, string, string, Task<bool>>? ShowConfirmDialogAsync { get; set; }

    public StorageView()
    {
        InitializeComponent();
        _settings = SettingsPersistenceService.Load();
        _currentSortMode = _settings.StorageSortMode ?? "SizeDesc";
        UpdateSortButtonText();

        _autoScanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autoScanTimer.Tick += async (s, e) =>
        {
            if (!_isStorageOperationRunning && !_isScanningTargets)
            {
                await ScanJunkAsync(showLog: false, isManualClick: false, isBackground: true);
            }
        };
        _autoScanTimer.Start();
    }

    public void StopAutoScan()
    {
        _autoScanTimer.Stop();
    }

    public void BindSettings(UserSettingsModel settings, Action saveCallback)
    {
        _settings = settings;
        _saveSettingsCallback = saveCallback;
        _currentSortMode = settings.StorageSortMode ?? "SizeDesc";
        UpdateSortButtonText();
        RefreshSelectionStates();
    }

    private void PersistSettings()
    {
        if (_saveSettingsCallback != null)
            _saveSettingsCallback();
        else
            SettingsPersistenceService.Save(_settings);
    }

    private void UpdateSortButtonText()
    {
        bool isEn = LocalizationService.IsEnglish;
        TxtSortTargets.Text = _currentSortMode switch
        {
            "SizeAsc" => isEn ? "Sort: Size ↑" : "Сорт: Размер ↑",
            "NameAsc" => isEn ? "Sort: Name A-Z" : "Сорт: Имя А-Я",
            _ => isEn ? "Sort: Size ↓" : "Сорт: Размер ↓"
        };
    }

    public void ApplyLanguage(string language)
    {
        bool isEn = language == "EN";
        StorageTabHeading.Text = isEn ? $"Storage & System Cleanup ({_watchedDriveLetter}:)" : $"Хранилище и системная очистка ({_watchedDriveLetter}:)";
        StorageTabSub.Text = LocalizationService.Get("Storage.Subtitle");
        StorageGaugeTitle.Text = isEn ? $"Storage Capacity ({_watchedDriveLetter}:):" : $"Емкость накопителя ({_watchedDriveLetter}:):";
        CleanTargetsTitle.Text = LocalizationService.Get("Storage.Targets");
        BtnScanJunkText.Text = LocalizationService.Get("Storage.ScanBtn");
        if (_storageCooldownRemaining <= 0 && !_isStorageOperationRunning)
        {
            BtnCleanSystemText.Text = LocalizationService.Get("Storage.CleanBtn");
        }
        BtnRollbackSystemText.Text = LocalizationService.Get("Storage.RollbackBtn");
        StorageLogTitle.Text = LocalizationService.Get("Storage.ConsoleTitle");

        if (StorageLogFooter.Text == "Ready" || StorageLogFooter.Text == "Готов к работе")
        {
            StorageLogFooter.Text = LocalizationService.Get("Status.Ready");
        }

        UpdateSortButtonText();
    }

    public void UpdateCapacity(string driveLetter, double freeGb, double totalGb)
    {
        _watchedDriveLetter = driveLetter;
        bool isEn = LocalizationService.IsEnglish;

        StorageGaugeTitle.Text = isEn ? $"Storage Capacity ({driveLetter}:):" : $"Емкость накопителя ({driveLetter}:):";
        StorageHeaderStatus.Text = isEn
            ? $"Free: {freeGb:0.0} GB of {totalGb:0.0} GB"
            : $"Свободно: {freeGb:0.0} ГБ из {totalGb:0.0} ГБ";

        double usedPercent = totalGb > 0 ? ((totalGb - freeGb) / totalGb) * 100.0 : 0;
        StorageProgressC.Value = Math.Clamp(usedPercent, 0, 100);
    }

    private bool IsTargetKeySelected(string key)
    {
        var disabled = _settings.DisabledCleanupTargets ?? new List<string>();
        if (disabled.Contains(key, StringComparer.OrdinalIgnoreCase))
            return false;

        return key switch
        {
            "user_temp" or "win_temp" => _settings.CleanTempFiles,
            "sw_dist" or "deliv_opt" => _settings.CleanSoftwareDistribution,
            "crash_dumps" or "wer" or "cbs_logs" => _settings.CleanLogsAndReports,
            "recycle_bin" => _settings.CleanRecycleBin,
            "dns_cache" => _settings.CleanDnsCache,
            "dism_cleanup" => _settings.CleanDismComponentCleanup,
            "dism_resetbase" => _settings.CleanDismResetBase,
            _ => true
        };
    }

    private void RefreshSelectionStates()
    {
        if (_cachedTargets.Count == 0) return;
        _cachedTargets = _cachedTargets
            .Select(t => t with { IsSelected = IsTargetKeySelected(t.Key) })
            .ToList();
        ApplySorting();
    }

    public async Task PerformScanCoreAsync(bool showLog = false)
    {
        bool isEn = LocalizationService.IsEnglish;

        if (showLog)
        {
            LogStorage(isEn ? "--> Starting scan for cleanable cache targets..." : "--> Сканирование безопасных целей очистки...");
            OnStatusChanged?.Invoke(isEn ? "Scanning temporary junk files..." : "Сканирование временных файлов...");
        }

        try
        {
            var settingsSnapshot = _settings;
            var targets = await Task.Run(() => StorageService.ScanTempLocations(isEn, settingsSnapshot));

            var items = targets.Select(t => new CleanTargetItem(
                t.Key,
                t.Name,
                t.Path,
                StorageService.FormatBytes(t.Bytes),
                t.Bytes,
                IsTargetKeySelected(t.Key),
                t.RequiresAdmin
            )).ToList();

            // Include command-based cleanup actions in the same checkable list
            items.Add(new CleanTargetItem(
                "dns_cache",
                isEn ? "Flush DNS Resolver Cache" : "Сброс сетевого кэша DNS",
                "ipconfig /flushdns",
                isEn ? "System" : "Команда",
                -1,
                IsTargetKeySelected("dns_cache"),
                false
            ));

            items.Add(new CleanTargetItem(
                "dism_cleanup",
                isEn ? "DISM Component Store Cleanup" : "Очистка компонентов Windows (DISM)",
                "DISM /StartComponentCleanup",
                "WinSxS",
                -1,
                IsTargetKeySelected("dism_cleanup"),
                true
            ));

            items.Add(new CleanTargetItem(
                "dism_resetbase",
                isEn ? "DISM ResetBase (Irreversible updates)" : "DISM ResetBase (Необратимая очистка обновлений)",
                "DISM /StartComponentCleanup /ResetBase",
                "ResetBase",
                -1,
                IsTargetKeySelected("dism_resetbase"),
                true
            ));

            _cachedTargets = items;
            _lastScanUtc = DateTime.UtcNow;

            ApplySorting();

            long selectedBytes = _cachedTargets.Where(t => t.IsSelected && t.Bytes > 0).Sum(t => t.Bytes);
            long totalBytes = targets.Sum(t => t.Bytes);

            if (showLog)
            {
                foreach (var t in targets)
                {
                    if (t.Bytes > 0)
                    {
                        string mark = IsTargetKeySelected(t.Key) ? "•" : "○";
                        LogStorage($"    {mark} {t.Name}: {StorageService.FormatBytes(t.Bytes)}");
                    }
                }

                string summary = isEn
                    ? $"[OK] Scan finished. Selected for cleanup: {StorageService.FormatBytes(selectedBytes)} (total found: {StorageService.FormatBytes(totalBytes)})."
                    : $"[OK] Сканирование завершено. Выбрано к очистке: {StorageService.FormatBytes(selectedBytes)} (всего найдено: {StorageService.FormatBytes(totalBytes)}).";
                LogStorage(summary);
                OnStatusChanged?.Invoke(summary);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Storage scan error", ex);
            if (showLog) LogStorage($"[ERROR] Scan error: {ex.Message}");
        }
    }

    private void ApplySorting()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ApplySorting);
            return;
        }

        var sorted = _currentSortMode switch
        {
            "SizeAsc" => _cachedTargets.OrderBy(t => t.Bytes < 0 ? 1 : 0).ThenBy(t => t.Bytes).ThenBy(t => t.Name).ToList(),
            "NameAsc" => _cachedTargets.OrderBy(t => t.Name).ToList(),
            _ => _cachedTargets.OrderBy(t => t.Bytes < 0 ? 1 : 0).ThenByDescending(t => t.Bytes).ThenBy(t => t.Name).ToList()
        };

        CleanTargetsList.ItemsSource = sorted;
    }

    private async void TargetCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not string key || string.IsNullOrEmpty(key))
            return;

        bool wantChecked = cb.IsChecked == true;
        _settings.DisabledCleanupTargets ??= new List<string>();

        if (key == "dns_cache")
        {
            _settings.CleanDnsCache = wantChecked;
        }
        else if (key == "dism_cleanup")
        {
            _settings.CleanDismComponentCleanup = wantChecked;
        }
        else if (key == "dism_resetbase")
        {
            if (wantChecked && ShowConfirmDialogAsync != null)
            {
                bool isEnDlg = LocalizationService.IsEnglish;
                string title = isEnDlg ? "DISM ResetBase Warning" : "Предупреждение DISM ResetBase";
                string msg = isEnDlg
                    ? "WARNING: After executing DISM /ResetBase, all currently installed Windows updates will become permanent and cannot be uninstalled. Do you want to enable this?"
                    : "ВНИМАНИЕ: После выполнения DISM /ResetBase все установленные обновления Windows станут постоянными и их нельзя будет удалить. Включить эту опцию?";
                bool confirmed = await ShowConfirmDialogAsync(title, msg, isEnDlg ? "Enable" : "Включить", isEnDlg ? "Cancel" : "Отмена", "IconShield");
                if (!confirmed)
                {
                    _settings.CleanDismResetBase = false;
                    PersistSettings();
                    RefreshSelectionStates();
                    return;
                }
            }
            _settings.CleanDismResetBase = wantChecked;
        }
        else if (key == "recycle_bin")
        {
            _settings.CleanRecycleBin = wantChecked;
            _settings.DisabledCleanupTargets.RemoveAll(k => k.Equals("recycle_bin", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            (bool groupEnabled, Action<bool> setGroup, string[] siblings) = key switch
            {
                "user_temp" or "win_temp" => (_settings.CleanTempFiles, (Action<bool>)(v => _settings.CleanTempFiles = v), new[] { "user_temp", "win_temp" }),
                "sw_dist" or "deliv_opt" => (_settings.CleanSoftwareDistribution, (Action<bool>)(v => _settings.CleanSoftwareDistribution = v), new[] { "sw_dist", "deliv_opt" }),
                "crash_dumps" or "wer" or "cbs_logs" => (_settings.CleanLogsAndReports, (Action<bool>)(v => _settings.CleanLogsAndReports = v), new[] { "crash_dumps", "wer", "cbs_logs" }),
                _ => (true, _ => { }, Array.Empty<string>())
            };

            if (wantChecked)
            {
                if (!groupEnabled)
                {
                    setGroup(true);
                    foreach (var s in siblings)
                    {
                        if (!s.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                            !_settings.DisabledCleanupTargets.Contains(s, StringComparer.OrdinalIgnoreCase))
                        {
                            _settings.DisabledCleanupTargets.Add(s);
                        }
                    }
                }
                _settings.DisabledCleanupTargets.RemoveAll(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                if (!_settings.DisabledCleanupTargets.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    _settings.DisabledCleanupTargets.Add(key);
                }
                if (siblings.Length > 0 && siblings.All(s => _settings.DisabledCleanupTargets.Contains(s, StringComparer.OrdinalIgnoreCase)))
                {
                    setGroup(false);
                    foreach (var s in siblings)
                    {
                        _settings.DisabledCleanupTargets.RemoveAll(k => k.Equals(s, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
        }

        PersistSettings();
        RefreshSelectionStates();

        bool isEn = LocalizationService.IsEnglish;
        long selectedBytes = _cachedTargets.Where(t => t.IsSelected && t.Bytes > 0).Sum(t => t.Bytes);
        OnStatusChanged?.Invoke(isEn
            ? $"Selected for cleanup: {StorageService.FormatBytes(selectedBytes)}"
            : $"Выбрано к очистке: {StorageService.FormatBytes(selectedBytes)}");
    }

    public async Task ScanJunkAsync(bool showLog = true, bool isManualClick = false, bool isBackground = false)
    {
        if (_isStorageOperationRunning || _isScanningTargets) return;
        if (isManualClick && _storageCooldownRemaining > 0) return;

        _isScanningTargets = true;
        if (!isBackground)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                BtnScanJunk.IsEnabled = false;
                if (_storageCooldownRemaining <= 0) BtnCleanSystem.IsEnabled = false;
            });
        }

        try
        {
            await PerformScanCoreAsync(showLog);
        }
        finally
        {
            _isScanningTargets = false;
            if (!isBackground)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_storageCooldownRemaining <= 0)
                    {
                        BtnScanJunk.IsEnabled = true;
                        BtnCleanSystem.IsEnabled = true;
                    }
                });
            }
        }
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

    private async void BtnCleanSystem_Click(object sender, RoutedEventArgs e)
    {
        if (_isStorageOperationRunning || _storageCooldownRemaining > 0) return;

        bool isEn = LocalizationService.IsEnglish;
        _isStorageOperationRunning = true;

        BtnCleanSystem.IsEnabled = false;
        BtnScanJunk.IsEnabled = false;
        BtnCleanSystemText.Text = isEn ? "Cleaning..." : "Очистка...";

        LogStorage(isEn ? "====== SYSTEM CACHE CLEANUP INITIATED ======" : "====== ЗАПУСК ОЧИСТКИ СИСТЕМНОГО КЭША ======");
        OnStatusChanged?.Invoke(isEn ? "Starting system cleanup..." : "Запуск системной очистки...");

        try
        {
            var opts = new StorageCleanupOptions(
                _settings.CleanTempFiles,
                _settings.CleanSoftwareDistribution,
                _settings.CleanLogsAndReports,
                _settings.CleanDnsCache,
                _settings.CleanRecycleBin,
                _settings.CleanDismComponentCleanup,
                _settings.CleanDismResetBase,
                new HashSet<string>(_settings.DisabledCleanupTargets ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
            );

            var res = await StorageService.ExecuteDeepCleanupAsync(isEn, stepMsg =>
            {
                LogStorage(stepMsg);
            }, opts);

            OnStatusChanged?.Invoke(res.SummaryMessage);

            await PerformScanCoreAsync(showLog: false);
            OnCleanupCompleted?.Invoke();

            if (res.AdminSkippedStages > 0 && !StorageService.IsAdministrator() && ShowConfirmDialogAsync != null)
            {
                string title = isEn ? "Administrator Rights Required" : "Требуются права администратора";
                string msg = isEn
                    ? $"Cleanup finished, but {res.AdminSkippedStages} system area(s) still contain files that require Administrator privileges (Windows Temp, Windows Update cache, WER/CBS logs, or DISM).\n\nCoreSpectre will run an elevated background worker to clean the remaining system areas without closing the app. Grant rights?"
                    : $"Очистка завершена, но часть системных областей ({res.AdminSkippedStages} этап(а/ов)) не была очищена — для них требуются права администратора (Windows Temp, кэш обновлений, отчёты WER/CBS или DISM).\n\nCoreSpectre запустит фоновый процесс с правами администратора и доочистит оставшиеся области, не закрывая окно приложения. Дать права?";
                string confirmBtn = isEn ? "Grant Rights" : "Дать права";
                string cancelBtn = isEn ? "Skip" : "Нет";

                bool grantAdmin = await ShowConfirmDialogAsync(title, msg, confirmBtn, cancelBtn, "IconShield");
                if (grantAdmin)
                {
                    await RunElevatedWorkerCleanupAsync(isEn);
                }
                else
                {
                    OnStatusChanged?.Invoke(isEn ? "Remaining protected files skipped." : "Защищённые системные файлы оставлены без изменений.");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("System cleanup exception", ex);
            LogStorage($"[ERROR] Cleanup exception: {ex.Message}");
            OnStatusChanged?.Invoke($"Cleanup error: {ex.Message}");
        }
        finally
        {
            _isStorageOperationRunning = false;
            StartStorageCooldown(5);
        }
    }

    private async void BtnRollbackSystem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool isEn = LocalizationService.IsEnglish;
            bool isAdmin = StorageService.IsAdministrator();

            if (ShowConfirmDialogAsync != null)
            {
                string title = isEn ? "Windows System Restore" : "Восстановление системы Windows";
                string msg = isEn
                    ? "CoreSpectre automatically creates a Windows restore point ('CoreSpectre Before Deep Cleanup') before deep cleanup when launched as Administrator and System Protection is enabled.\n\nClick 'Open Restore' to launch the Windows System Restore wizard (rstrui.exe) and check or revert to an available restore point."
                    : "CoreSpectre автоматически создаёт точку восстановления Windows («CoreSpectre Before Deep Cleanup») перед очисткой, если запущен от имени администратора и в Windows включена Защита системы.\n\nНажмите «Открыть откат», чтобы запустить мастер восстановления Windows (rstrui.exe) и выбрать точку отката.";
                string confirmBtn = isEn ? "Open Restore" : "Открыть откат";
                string cancelBtn = isEn ? "Cancel" : "Отмена";

                bool proceed = await ShowConfirmDialogAsync(title, msg, confirmBtn, cancelBtn, "IconArrowUUpLeft");
                if (!proceed) return;
            }

            bool launched = StorageService.LaunchSystemRestore();
            if (launched)
            {
                string status = isEn ? "Windows System Restore wizard (rstrui.exe) launched." : "Мастер восстановления системы Windows (rstrui.exe) запущен.";
                LogStorage(status);
                OnStatusChanged?.Invoke(status);
            }
            else
            {
                string status = isEn
                    ? $"Could not launch rstrui.exe{(isAdmin ? "" : " (try running as Administrator)")}."
                    : $"Не удалось запустить rstrui.exe{(isAdmin ? "" : " (попробуйте запуск от имени администратора)")}.";
                LogStorage(status);
                OnStatusChanged?.Invoke(status);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Rollback launch failed", ex);
        }
    }

    private void BtnSortTargets_Click(object sender, RoutedEventArgs e)
    {
        _currentSortMode = _currentSortMode switch
        {
            "SizeDesc" => "SizeAsc",
            "SizeAsc" => "NameAsc",
            _ => "SizeDesc"
        };

        UpdateSortButtonText();
        ApplySorting();

        _settings.StorageSortMode = _currentSortMode;
        PersistSettings();
    }

    private async Task RunElevatedWorkerCleanupAsync(bool isEn)
    {
        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoreSpectre", "logs");
        Directory.CreateDirectory(logDir);

        string runId = Guid.NewGuid().ToString("N");
        string jobPath = Path.Combine(logDir, $"elevated_job_{runId}.json");
        string progressPath = Path.Combine(logDir, $"elevated_progress_{runId}.log");
        string resultPath = Path.Combine(logDir, $"elevated_result_{runId}.json");

        try
        {
            var job = new ElevatedCleanupJob(
                IsEnglish: isEn,
                CleanTemp: _settings.CleanTempFiles,
                CleanSoftwareDistribution: _settings.CleanSoftwareDistribution,
                CleanLogsAndReports: _settings.CleanLogsAndReports,
                CleanDismComponentCleanup: _settings.CleanDismComponentCleanup,
                CleanDismResetBase: _settings.CleanDismResetBase,
                DisabledTargetKeys: new List<string>(_settings.DisabledCleanupTargets ?? new List<string>()),
                ProgressLogPath: progressPath,
                ResultJsonPath: resultPath
            );

            await File.WriteAllTextAsync(jobPath, System.Text.Json.JsonSerializer.Serialize(job));

            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                exePath = Path.Combine(AppContext.BaseDirectory, "CoreSpectre.exe");
            }

            var psi = new ProcessStartInfo(exePath)
            {
                Arguments = $"--elevated-worker \"{jobPath}\"",
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            LogStorage(isEn
                ? "[UAC] Starting elevated cleanup worker (main window stays active)..."
                : "[UAC] Запуск фонового процесса очистки с правами администратора (окно приложения остаётся открытым)...");
            OnStatusChanged?.Invoke(isEn ? "Running elevated system cleanup..." : "Выполняется очистка с правами администратора...");

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                string msg = isEn ? "Could not start elevated cleanup worker." : "Не удалось запустить процесс очистки с правами администратора.";
                LogStorage(msg);
                OnStatusChanged?.Invoke(msg);
                return;
            }

            int linesRead = 0;
            while (!proc.HasExited)
            {
                linesRead = ReadNewProgressLines(progressPath, linesRead);
                await Task.Delay(250);
            }

            await proc.WaitForExitAsync();
            linesRead = ReadNewProgressLines(progressPath, linesRead);

            if (File.Exists(resultPath))
            {
                string json = await File.ReadAllTextAsync(resultPath);
                var adminResult = System.Text.Json.JsonSerializer.Deserialize<CleanResult>(json);
                if (adminResult != null && !string.IsNullOrWhiteSpace(adminResult.SummaryMessage))
                {
                    OnStatusChanged?.Invoke(adminResult.SummaryMessage);
                }
            }
            else if (proc.ExitCode != 0)
            {
                string errMsg = isEn
                    ? $"Elevated cleanup exited with code {proc.ExitCode}."
                    : $"Очистка с правами администратора завершилась с кодом {proc.ExitCode}.";
                LogStorage(errMsg);
                OnStatusChanged?.Invoke(errMsg);
            }

            await PerformScanCoreAsync(showLog: false);
            OnCleanupCompleted?.Invoke();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            string msg = isEn ? "Administrator elevation cancelled." : "Предоставление прав администратора отменено.";
            LogStorage(msg);
            OnStatusChanged?.Invoke(msg);
        }
        catch (Exception ex)
        {
            Logger.Error("Elevated worker failed", ex);
            LogStorage($"[ERROR] Elevated worker exception: {ex.Message}");
            OnStatusChanged?.Invoke($"Elevation error: {ex.Message}");
        }
        finally
        {
            TryDeleteTempFile(jobPath);
            TryDeleteTempFile(progressPath);
            TryDeleteTempFile(resultPath);
        }
    }

    private int ReadNewProgressLines(string progressPath, int alreadyReadCount)
    {
        if (!File.Exists(progressPath)) return alreadyReadCount;

        try
        {
            using var fs = new FileStream(progressPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            int idx = 0;
            while (sr.ReadLine() is { } line)
            {
                if (idx >= alreadyReadCount && !string.IsNullOrWhiteSpace(line))
                {
                    LogStorage(line);
                }
                idx++;
            }
            return idx;
        }
        catch
        {
            return alreadyReadCount;
        }
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Ignore cleanup errors for temporary worker IPC files
        }
    }

    private void StartStorageCooldown(int seconds)
    {
        bool isEn = LocalizationService.IsEnglish;
        _storageCooldownRemaining = seconds;

        BtnCleanSystem.IsEnabled = false;
        BtnScanJunk.IsEnabled = false;
        BtnCleanSystemText.Text = isEn ? $"Cooldown ({_storageCooldownRemaining}s)" : $"Ожидание ({_storageCooldownRemaining} сек)";

        _storageCooldownTimer?.Stop();
        _storageCooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _storageCooldownTimer.Tick += (s, e) =>
        {
            _storageCooldownRemaining--;
            if (_storageCooldownRemaining <= 0)
            {
                _storageCooldownTimer.Stop();
                BtnCleanSystem.IsEnabled = true;
                BtnScanJunk.IsEnabled = true;
                BtnCleanSystemText.Text = isEn ? "Clean System Cache" : "Очистить систему";
            }
            else
            {
                BtnCleanSystemText.Text = isEn ? $"Cooldown ({_storageCooldownRemaining}s)" : $"Ожидание ({_storageCooldownRemaining} сек)";
            }
        };
        _storageCooldownTimer.Start();
    }

    public void LogStorage(string msg)
    {
        Dispatcher.Invoke(() =>
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            string line = $"[{time}] {msg}";
            if (string.IsNullOrWhiteSpace(StorageLogTextBox.Text))
                StorageLogTextBox.Text = line;
            else
                StorageLogTextBox.AppendText(Environment.NewLine + line);

            StorageLogScroll.ScrollToEnd();
            StorageLogFooter.Text = $"Last: {time}";
        });
    }

    private void BtnClearStorageLog_Click(object sender, RoutedEventArgs e)
    {
        StorageLogTextBox.Text = string.Empty;
        StorageLogFooter.Text = LocalizationService.IsEnglish ? "Console cleared" : "Консоль очищена";
    }

    private void BtnDiskDropdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe) OnDiskDropdownRequested?.Invoke(fe);
    }
}
