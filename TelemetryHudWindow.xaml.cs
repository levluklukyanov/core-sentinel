using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CoreSentinel.Services;

namespace CoreSentinel;

public partial class TelemetryHudWindow : Window
{
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int GWL_EXSTYLE = -20;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly DispatcherTimer _timer;
    private bool _isClickThrough = false;

    public TelemetryHudWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _timer.Tick += (s, e) => RefreshMetrics();
        _timer.Start();

        RefreshMetrics();
    }

    private void RefreshMetrics()
    {
        var sample = HardwareTelemetry.Sample();

        HudCpuText.Text = $"{sample.CpuLoadPercent:0}%";
        HudRamText.Text = $"{sample.RamUsedPercent:0}%";
        HudDiskLabel.Text = $"{sample.SystemDriveLetter}: ";
        HudDiskText.Text = $"{sample.DiskCFreeGb:0.#}G";

        // Game mode indicator (Monochrome)
        if (sample.IsGameRunning)
        {
            HudStatusDot.Fill = (SolidColorBrush)FindResource("BrushTextPrimary");
            HudStatusDot.ToolTip = $"Game Mode активен: {sample.ActiveGameName}";
        }
        else
        {
            HudStatusDot.Fill = (SolidColorBrush)FindResource("BrushTextMuted");
            HudStatusDot.ToolTip = "Система в норме";
        }
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isClickThrough && e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnLockHud_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        if (!_isClickThrough)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
            _isClickThrough = true;
            HudLockIcon.Data = (Geometry)FindResource("IconLock");
            HudLockIcon.Fill = (SolidColorBrush)FindResource("BrushTextPrimary");
            BtnLockHud.ToolTip = "Снять режим Click-Through (разблокировать перемещение)";
        }
        else
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
            _isClickThrough = false;
            HudLockIcon.Data = (Geometry)FindResource("IconLockOpen");
            HudLockIcon.Fill = (SolidColorBrush)FindResource("BrushTextMuted");
            BtnLockHud.ToolTip = "Режим Click-Through (клики проходят насквозь в игры)";
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Hide();
    }
}
