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
    private int _storageCooldownRemaining = 0;
    private DispatcherTimer? _storageCooldownTimer;
    private string _watchedDriveLetter = "C";
    private string _currentSortMode = "SizeDesc";

    private List<CleanTargetItem> _cachedTargets = new();

    public event Action<FrameworkElement>? OnDiskDropdownRequested;
    public event Action<string>? OnStatusChanged;
    public Func<string, string, string, string, string, Task<bool>>? ShowConfirmDialogAsync { get; set; }

    public StorageView()
    {
        InitializeComponent();
        LoadSortMode();
    }

    private void LoadSortMode()
    {
        var settings = SettingsPersistenceService.Load();
        _currentSortMode = settings.StorageSortMode ?? "SizeDesc";
        UpdateSortButtonText();
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
        BtnCleanSystemText.Text = LocalizationService.Get("Storage.CleanBtn");
        BtnRollbackSystemText.Text = LocalizationService.Get("Storage.RollbackBtn");
        BtnRelaunchAdminText.Text = LocalizationService.Get("Storage.AdminBtn");
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

        bool isAdmin = StorageService.IsAdministrator();
        BtnRelaunchAdmin.Visibility = !isAdmin ? Visibility.Visible : Visibility.Collapsed;
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
            var targets = await Task.Run(() => StorageService.ScanTempLocations(isEn));

            _cachedTargets = targets.Select(t => new CleanTargetItem(
                t.Name,
                t.Path,
                StorageService.FormatBytes(t.Bytes),
                t.Bytes
            )).ToList();

            ApplySorting();

            long totalBytes = targets.Sum(t => t.Bytes);
            bool isAdmin = StorageService.IsAdministrator();
            BtnRelaunchAdmin.Visibility = !isAdmin ? Visibility.Visible : Visibility.Collapsed;

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
                OnStatusChanged?.Invoke(summary);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Storage scan error", ex);
            LogStorage($"[ERROR] Scan error: {ex.Message}");
        }
    }

    private void ApplySorting()
    {
        var sorted = _currentSortMode switch
        {
            "SizeAsc" => _cachedTargets.OrderBy(t => t.Bytes).ThenBy(t => t.Name).ToList(),
            "NameAsc" => _cachedTargets.OrderBy(t => t.Name).ToList(),
            _ => _cachedTargets.OrderByDescending(t => t.Bytes).ThenBy(t => t.Name).ToList()
        };

        CleanTargetsList.ItemsSource = sorted;
    }

    public async Task ScanJunkAsync(bool showLog = true, bool isManualClick = false)
    {
        if (_isStorageOperationRunning) return;
        if (isManualClick && _storageCooldownRemaining > 0) return;

        BtnScanJunk.IsEnabled = false;
        if (_storageCooldownRemaining <= 0) BtnCleanSystem.IsEnabled = false;

        try
        {
            await PerformScanCoreAsync(showLog);
        }
        finally
        {
            if (_storageCooldownRemaining <= 0)
            {
                BtnScanJunk.IsEnabled = true;
                BtnCleanSystem.IsEnabled = true;
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
            var res = await StorageService.ExecuteDeepCleanupAsync(isEn, stepMsg =>
            {
                LogStorage(stepMsg);
                OnStatusChanged?.Invoke(stepMsg);
            });

            if (res.AdminSkippedStages > 0 || res.FilesSkipped > 0 || res.HasErrors)
            {
                OnStatusChanged?.Invoke(isEn
                    ? $"Cleanup finished with warnings. Freed {StorageService.FormatBytes(res.BytesFreed)} (admin skipped: {res.AdminSkippedStages}, locked: {res.FilesSkipped})."
                    : $"Очистка выполнена с ограничениями. Освобождено {StorageService.FormatBytes(res.BytesFreed)} (без прав: {res.AdminSkippedStages}, занято: {res.FilesSkipped}).");
            }
            else
            {
                OnStatusChanged?.Invoke(isEn
                    ? $"Cleanup completed. Freed {StorageService.FormatBytes(res.BytesFreed)}."
                    : $"Очистка завершена. Освобождено {StorageService.FormatBytes(res.BytesFreed)}.");
            }

            await PerformScanCoreAsync(showLog: false);
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

    private void BtnRollbackSystem_Click(object sender, RoutedEventArgs e)
    {
        bool isEn = LocalizationService.IsEnglish;
        bool launched = StorageService.LaunchSystemRestore();
        if (launched)
        {
            OnStatusChanged?.Invoke(isEn ? "Windows System Restore wizard launched." : "Мастер восстановления системы Windows запущен.");
        }
        else
        {
            OnStatusChanged?.Invoke(isEn ? "Could not launch System Restore wizard." : "Не удалось запустить мастер восстановления системы.");
        }
    }

    private void BtnRelaunchAdmin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                exePath = Path.Combine(AppContext.BaseDirectory, "CoreSpectre.exe");
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
            bool isEn = LocalizationService.IsEnglish;
            OnStatusChanged?.Invoke(isEn ? "Administrator elevation cancelled." : "Запуск от имени администратора отменен.");
        }
        catch (Exception ex)
        {
            OnStatusChanged?.Invoke($"Elevation error: {ex.Message}");
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

        var settings = SettingsPersistenceService.Load();
        settings.StorageSortMode = _currentSortMode;
        SettingsPersistenceService.Save(settings);
    }

    private void BtnCleanSettingsGear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        bool isEn = LocalizationService.IsEnglish;
        var settings = SettingsPersistenceService.Load();

        var cm = new ContextMenu
        {
            PlacementTarget = btn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        MenuItem CreateItem(string title, bool isChecked, Action<bool> onToggle)
        {
            var item = new MenuItem
            {
                Header = title,
                IsCheckable = true,
                IsChecked = isChecked,
                StaysOpenOnClick = true
            };
            item.Click += (s, args) =>
            {
                onToggle(item.IsChecked);
                SettingsPersistenceService.Save(settings);
            };
            return item;
        }

        cm.Items.Add(CreateItem(isEn ? "Temporary Files (Temp)" : "Временные файлы (Temp)", settings.CleanTempFiles, v => settings.CleanTempFiles = v));
        cm.Items.Add(CreateItem(isEn ? "Windows Update Cache (SoftwareDistribution)" : "Кэш обновлений Windows (SoftwareDistribution)", settings.CleanSoftwareDistribution, v => settings.CleanSoftwareDistribution = v));
        cm.Items.Add(CreateItem(isEn ? "Error Reports & CBS Logs (WER/CBS)" : "Логи и отчеты об ошибках (WER, CBS)", settings.CleanLogsAndReports, v => settings.CleanLogsAndReports = v));
        cm.Items.Add(CreateItem(isEn ? "Flush DNS Resolver Cache" : "Сброс кэша DNS", settings.CleanDnsCache, v => settings.CleanDnsCache = v));
        cm.Items.Add(CreateItem(isEn ? "Empty Recycle Bin" : "Очистка Корзины", settings.CleanRecycleBin, v => settings.CleanRecycleBin = v));
        cm.Items.Add(new Separator());
        cm.Items.Add(CreateItem(isEn ? "DISM Component Cleanup (/StartComponentCleanup)" : "Очистка компонентов DISM (/StartComponentCleanup)", settings.CleanDismComponentCleanup, v => settings.CleanDismComponentCleanup = v));

        // DISM ResetBase with warning
        var resetBaseItem = new MenuItem
        {
            Header = isEn ? "DISM ResetBase (Irreversible updates cleanup)" : "DISM ResetBase (Необратимая очистка обновлений)",
            IsCheckable = true,
            IsChecked = settings.CleanDismResetBase,
            StaysOpenOnClick = true
        };
        resetBaseItem.Click += async (s, args) =>
        {
            if (resetBaseItem.IsChecked)
            {
                if (ShowConfirmDialogAsync != null)
                {
                    string title = isEn ? "DISM ResetBase Warning" : "Предупреждение DISM ResetBase";
                    string msg = isEn
                        ? "WARNING: After executing DISM /ResetBase, all currently installed Windows updates will become permanent and cannot be uninstalled. Do you want to enable this?"
                        : "ВНИМАНИЕ: После выполнения DISM /ResetBase все установленные обновления Windows станут постоянными и их нельзя будет удалить. Включить эту опцию?";
                    bool confirmed = await ShowConfirmDialogAsync(title, msg, isEn ? "Enable" : "Включить", isEn ? "Cancel" : "Отмена", "IconShield");
                    if (!confirmed)
                    {
                        resetBaseItem.IsChecked = false;
                        settings.CleanDismResetBase = false;
                        SettingsPersistenceService.Save(settings);
                        return;
                    }
                }
            }
            settings.CleanDismResetBase = resetBaseItem.IsChecked;
            SettingsPersistenceService.Save(settings);
        };
        cm.Items.Add(resetBaseItem);

        cm.IsOpen = true;
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
