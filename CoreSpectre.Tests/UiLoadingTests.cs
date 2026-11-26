using System;
using System.IO;
using System.Threading;
using System.Windows;
using CoreSpectre.Services;
using CoreSpectre.Views;
using Xunit;

namespace CoreSpectre.Tests;

public class UiLoadingTests
{
    [Fact]
    public void App_Resources_And_Windows_Load_Without_Exceptions()
    {
        string isolatedSettingsPath = Path.Combine(AppContext.BaseDirectory, $"test_settings_{Guid.NewGuid():N}.json");
        SettingsPersistenceService.SettingsPathOverride = isolatedSettingsPath;
        string? failureDetails = null;

        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    var app = new App();
                    app.InitializeComponent();
                }

                var mainWindow = new MainWindow();

                // Test all tabs
                for (int i = 0; i < 5; i++)
                {
                    mainWindow.HighlightTab(i);
                }

                // Test instantiation of all standalone views
                _ = new OverviewView();
                _ = new PurgeView();
                _ = new StorageView();
                _ = new GamesView();
                _ = new SettingsView();
                var hud = new TelemetryHudWindow();
                hud.ApplyLocalizationAndTheme();
                hud.ApplySnapshot(new TelemetrySnapshot(
                    CpuLoadPercent: 12.5,
                    RamUsedPercent: 45.0,
                    RamUsedBytes: 4L * 1024 * 1024 * 1024,
                    RamTotalBytes: 16L * 1024 * 1024 * 1024,
                    SystemDriveLetter: "C",
                    DiskCFreeGb: 50,
                    DiskCTotalGb: 100,
                    DataDriveLetter: "D",
                    DiskDFreeGb: 100,
                    DiskDTotalGb: 200,
                    HasSecondaryDataDrive: true,
                    IsGameRunning: false,
                    ActiveGameName: "",
                    GpuLoadPercent: 20,
                    GpuTempCelsius: 55,
                    CpuHistory: new[] { 12.5 },
                    RamHistory: new[] { 45.0 }));
                hud.Close();

                // Test theme switching (Light and Dark)
                mainWindow.SetTheme(isLight: true);
                mainWindow.SetTheme(isLight: false);

                // Test language switching and CPU/RAM mode toggle
                mainWindow.SetLanguage("RU");
                mainWindow.SetPurgeTabMode(isCpu: true, saveSettings: false);
                mainWindow.SetPurgeTabMode(isCpu: false, saveSettings: false);
                mainWindow.SetLanguage("EN");

                // Verify required navigation icons exist in active theme
                var currentApp = Application.Current;
                Assert.NotNull(currentApp);
                Assert.NotNull(currentApp.TryFindResource("IconSquaresFour"));
                Assert.NotNull(currentApp.TryFindResource("IconCpu"));
                Assert.NotNull(currentApp.TryFindResource("IconRam"));

                // Verify footer clock updates cleanly for 12h and 24h formats and displays UTC offset
                var clockTb = mainWindow.FindName("FooterClockText") as System.Windows.Controls.TextBlock;
                Assert.NotNull(clockTb);
                mainWindow.SetTimeFormat(false);
                Assert.Contains("(UTC", clockTb.Text);
                Assert.Matches(@"^\d{2}:\d{2}:\d{2} \(UTC[+-]\d+(?::\d{2})?\)$", clockTb.Text);
                mainWindow.SetTimeFormat(true);
                Assert.Contains("(UTC", clockTb.Text);
                Assert.Matches(@"^\d{2}:\d{2}:\d{2} (AM|PM) \(UTC[+-]\d+(?::\d{2})?\)$", clockTb.Text);

                mainWindow.Close();
            }
            catch (Exception ex)
            {
                var cur = ex;
                var sb = new System.Text.StringBuilder();
                while (cur != null)
                {
                    sb.AppendLine($"[{cur.GetType().FullName}] {cur.Message}");
                    cur = cur.InnerException;
                }
                failureDetails = sb.ToString();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        try
        {
            SettingsPersistenceService.SettingsPathOverride = null;
            if (File.Exists(isolatedSettingsPath)) File.Delete(isolatedSettingsPath);
        }
        catch { }

        if (failureDetails != null)
        {
            Assert.Fail(failureDetails);
        }
    }
}
