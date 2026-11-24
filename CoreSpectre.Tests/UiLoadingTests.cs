using System.Threading;
using System.Windows;
using CoreSpectre.Views;
using Xunit;

namespace CoreSpectre.Tests;

public class UiLoadingTests
{
    [Fact]
    public void App_Resources_And_Windows_Load_Without_Exceptions()
    {
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

        if (failureDetails != null)
        {
            Assert.Fail(failureDetails);
        }
    }
}
