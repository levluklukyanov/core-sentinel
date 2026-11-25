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
        string settingsPath = SettingsPersistenceService.GetSettingsPath();
        string? originalSettings = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
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
                _ = new TelemetryHudWindow();

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

                // Verify footer clock shows only UTC+N offset (no UTC clock time)
                var clockTb = mainWindow.FindName("FooterClockText") as System.Windows.Controls.TextBlock;
                Assert.NotNull(clockTb);
                Assert.Contains("(UTC", clockTb.Text);
                Assert.DoesNotContain("UTC)", clockTb.Text);

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
            if (originalSettings != null)
                File.WriteAllText(settingsPath, originalSettings);
            else if (File.Exists(settingsPath))
                File.Delete(settingsPath);
        }
        catch { }

        if (failureDetails != null)
        {
            Assert.Fail(failureDetails);
        }
    }
}
