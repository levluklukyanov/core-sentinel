using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CoreSentinel.Services;

namespace CoreSentinel;

public class ProcessItemViewModel : INotifyPropertyChanged
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    private string _windowTitle = "";
    public string WindowTitle
    {
        get => _windowTitle;
        set
        {
            if (_windowTitle != value)
            {
                _windowTitle = value;
                OnPropertyChanged();
            }
        }
    }
    public bool IsCritical { get; init; }

    private double _cpuPercent;
    public double CpuPercent
    {
        get => _cpuPercent;
        set
        {
            if (Math.Abs(_cpuPercent - value) > 0.05)
            {
                _cpuPercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedCpu));
            }
        }
    }

    public string FormattedCpu => $"{CpuPercent:0.0}%";

    private long _workingSetBytes;
    public long WorkingSetBytes
    {
        get => _workingSetBytes;
        set
        {
            if (_workingSetBytes != value)
            {
                _workingSetBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedRam));
            }
        }
    }

    public string FormattedRam => StorageService.FormatBytes(WorkingSetBytes);

    private int _threadCount;
    public int ThreadCount
    {
        get => _threadCount;
        set
        {
            if (_threadCount != value)
            {
                _threadCount = value;
                OnPropertyChanged();
            }
        }
    }

    private string _priority = "Обычный";
    public string Priority
    {
        get => _priority;
        set
        {
            if (_priority != value)
            {
                _priority = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public partial class ProcessManagerWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;
    private ProcessSortMode _sortMode;
    private readonly ObservableCollection<ProcessItemViewModel> _items = new();
    private readonly Dictionary<int, ProcessItemViewModel> _itemMap = new();
    private ProcessItemViewModel? _selectedItem;

    public ProcessManagerWindow(ProcessSortMode initialSort = ProcessSortMode.Ram)
    {
        InitializeComponent();
        _sortMode = initialSort;
        ProcessListView.ItemsSource = _items;

        UpdateSortButtonsVisual();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _refreshTimer.Tick += (s, e) => RefreshData(forceResetOrder: false);
        _refreshTimer.Start();

        RefreshData(forceResetOrder: true);
    }

    public void SetSortMode(ProcessSortMode mode)
    {
        if (_sortMode != mode)
        {
            _sortMode = mode;
            UpdateSortButtonsVisual();
            RefreshData(forceResetOrder: true);
        }
    }

    private void RefreshData(bool forceResetOrder = false)
    {
        string query = SearchBox.Text.Trim();
        var rawList = ProcessTrackerService.SampleProcesses(_sortMode, query);

        // Update live summary in titlebar
        var snap = HardwareTelemetry.Sample();
        double usedGb = (double)snap.RamUsedBytes / (1024 * 1024 * 1024);
        double totalGb = (double)snap.RamTotalBytes / (1024 * 1024 * 1024);
        DashSummaryText.Text = $"CPU: {snap.CpuLoadPercent:0}% · ОЗУ: {usedGb:0.#} / {totalGb:0.#} ГБ ({snap.RamUsedPercent:0}%) · Процессов: {rawList.Count}";
        FooterProcessCountText.Text = $"Всего в списке: {rawList.Count}";

        int? currentSelectedPid = _selectedItem?.Pid;

        if (forceResetOrder || _items.Count == 0)
        {
            _items.Clear();
            _itemMap.Clear();
            foreach (var r in rawList)
            {
                var vm = new ProcessItemViewModel
                {
                    Pid = r.Pid,
                    Name = r.Name,
                    DisplayName = r.DisplayName,
                    WindowTitle = r.WindowTitle,
                    IsCritical = r.IsCritical,
                    CpuPercent = r.CpuPercent,
                    WorkingSetBytes = r.WorkingSetBytes,
                    ThreadCount = r.ThreadCount,
                    Priority = r.Priority
                };
                _items.Add(vm);
                _itemMap[r.Pid] = vm;
            }
        }
        else
        {
            // Remove dead processes
            var activeRawPids = new HashSet<int>(rawList.Select(r => r.Pid));
            var deadVms = _items.Where(it => !activeRawPids.Contains(it.Pid)).ToList();
            foreach (var dead in deadVms)
            {
                _items.Remove(dead);
                _itemMap.Remove(dead.Pid);
            }

            // In-place updates and additions for new/recycled PIDs
            foreach (var r in rawList)
            {
                if (_itemMap.TryGetValue(r.Pid, out var vm))
                {
                    // Check if PID was recycled by Windows for another executable
                    if (vm.Name != r.Name)
                    {
                        int idx = _items.IndexOf(vm);
                        var newVm = new ProcessItemViewModel
                        {
                            Pid = r.Pid,
                            Name = r.Name,
                            DisplayName = r.DisplayName,
                            WindowTitle = r.WindowTitle,
                            IsCritical = r.IsCritical,
                            CpuPercent = r.CpuPercent,
                            WorkingSetBytes = r.WorkingSetBytes,
                            ThreadCount = r.ThreadCount,
                            Priority = r.Priority
                        };
                        if (idx >= 0) _items[idx] = newVm;
                        _itemMap[r.Pid] = newVm;
                    }
                    else
                    {
                        vm.CpuPercent = r.CpuPercent;
                        vm.WorkingSetBytes = r.WorkingSetBytes;
                        vm.ThreadCount = r.ThreadCount;
                        vm.Priority = r.Priority;
                        if (vm.WindowTitle != r.WindowTitle) vm.WindowTitle = r.WindowTitle;
                    }
                }
                else
                {
                    var newVm = new ProcessItemViewModel
                    {
                        Pid = r.Pid,
                        Name = r.Name,
                        DisplayName = r.DisplayName,
                        WindowTitle = r.WindowTitle,
                        IsCritical = r.IsCritical,
                        CpuPercent = r.CpuPercent,
                        WorkingSetBytes = r.WorkingSetBytes,
                        ThreadCount = r.ThreadCount,
                        Priority = r.Priority
                    };
                    _items.Add(newVm);
                    _itemMap[r.Pid] = newVm;
                }
            }
        }

        // Restore selection
        if (currentSelectedPid.HasValue && _itemMap.TryGetValue(currentSelectedPid.Value, out var restoredItem))
        {
            if (ProcessListView.SelectedItem != restoredItem)
            {
                ProcessListView.SelectedItem = restoredItem;
            }
        }
    }

    private void UpdateSortButtonsVisual()
    {
        var primaryStyle = (Style)FindResource("BtnPrimaryWhite");
        var pillStyle = (Style)FindResource("BtnPill");

        BtnSortCpu.Style = _sortMode == ProcessSortMode.Cpu ? primaryStyle : pillStyle;
        BtnSortRam.Style = _sortMode == ProcessSortMode.Ram ? primaryStyle : pillStyle;
    }

    #region Toolbar Handlers

    private void BtnOpenTaskmgr_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string taskmgrPath = System.IO.Path.Combine(Environment.SystemDirectory, "taskmgr.exe");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(taskmgrPath) { UseShellExecute = true });
            StatusFooterText.Text = "Диспетчер задач Windows запущен.";
        }
        catch (Exception ex)
        {
            StatusFooterText.Text = $"Ошибка запуска: {ex.Message}";
        }
    }

    private void BtnSortCpu_Click(object sender, RoutedEventArgs e)
    {
        SetSortMode(ProcessSortMode.Cpu);
    }

    private void BtnSortRam_Click(object sender, RoutedEventArgs e)
    {
        SetSortMode(ProcessSortMode.Ram);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        RefreshData(forceResetOrder: true);
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshData(forceResetOrder: true);
        StatusFooterText.Text = $"Список процессов обновлен вручную в {DateTime.Now:HH:mm:ss}.";
    }

    private void BtnPurgeProcessRam_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        var (success, msg) = ProcessTrackerService.PurgeProcessMemory(_selectedItem.Pid);
        StatusFooterText.Text = msg;
        RefreshData(forceResetOrder: false);
    }

    private void BtnKillProcess_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;

        if (_selectedItem.IsCritical)
        {
            StatusFooterText.Text = $"Защита системы: '{_selectedItem.DisplayName}' (PID {_selectedItem.Pid}) критически важен. Завершение заблокировано.";
            return;
        }

        var (success, msg) = ProcessTrackerService.TerminateProcess(_selectedItem.Pid);
        StatusFooterText.Text = msg;
        RefreshData(forceResetOrder: true);
    }

    #endregion

    #region List Selection & Window Handlers

    private void ProcessListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedItem = ProcessListView.SelectedItem as ProcessItemViewModel;

        if (_selectedItem != null)
        {
            BtnKillProcess.IsEnabled = !_selectedItem.IsCritical;
            BtnPurgeProcessRam.IsEnabled = true;
            string criticalTag = _selectedItem.IsCritical ? " · [Системный защищенный]" : "";
            StatusFooterText.Text = $"Выбран: {_selectedItem.DisplayName} (PID {_selectedItem.Pid}) · ОЗУ: {_selectedItem.FormattedRam} · CPU: {_selectedItem.FormattedCpu}{criticalTag}";
        }
        else
        {
            BtnKillProcess.IsEnabled = false;
            BtnPurgeProcessRam.IsEnabled = false;
            StatusFooterText.Text = "Диспетчер процессов активен. Выберите строку для управления.";
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else if (WindowState != WindowState.Maximized)
        {
            DragMove();
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        base.OnClosed(e);
    }

    #endregion
}
