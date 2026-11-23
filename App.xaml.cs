using System.IO;
using System.Windows;
using CoreSentinel.Services;

namespace CoreSentinel;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Инициализируем шрифты до создания главного окна
        FontService.Initialize();

        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                LogCrash($"[{DateTime.Now}] Dispatcher Exception:\n{args.Exception}\n\n");
            }
            catch { }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try
            {
                LogCrash($"[{DateTime.Now}] Domain Exception:\n{args.ExceptionObject}\n\n");
            }
            catch { }
        };
    }

    private static void LogCrash(string message)
    {
        try
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CoreSentinel");
            Directory.CreateDirectory(logDir);
            string crashFile = Path.Combine(logDir, "crash_log.txt");
            File.AppendAllText(crashFile, message);
        }
        catch { }
    }
}

