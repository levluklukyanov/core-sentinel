using System;
using System.IO;
using System.Windows;
using CoreSpectre.Services;

namespace CoreSpectre;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Инициализируем шрифты до создания главного окна
        FontService.Initialize();
        Logger.Info("CoreSpectre application startup initialized.");

        base.OnStartup(e);

        DispatcherUnhandledException += (s, args) =>
        {
            var ex = args.Exception;
            Logger.Error($"Dispatcher unhandled exception: {ex.GetType().FullName}", ex);

            // Determine if safe/ignorable UI exception
            bool isBenign = ex is System.ComponentModel.Win32Exception ||
                            !(ex is System.Windows.Markup.XamlParseException) &&
                            (ex.Message.Contains("clipboard", StringComparison.OrdinalIgnoreCase) ||
                             ex.Message.Contains("drag", StringComparison.OrdinalIgnoreCase));

            if (isBenign)
            {
                Logger.Warn($"Non-fatal UI exception handled: {ex.Message}");
                args.Handled = true;
            }
            else
            {
                Logger.LogCrash("Fatal Dispatcher Unhandled Exception", ex);
                string details = ex.InnerException != null 
                    ? $"{ex.Message}\n\nDetails: {ex.InnerException.Message}" 
                    : ex.Message;

                try
                {
                    MessageBox.Show(
                        $"A critical error occurred in CoreSpectre.\n\n" +
                        $"Error: {details}\n\n" +
                        $"Crash log saved to:\n{Logger.CrashLogFilePath}\n\n" +
                        $"The application will now shut down to prevent state corruption.",
                        "CoreSpectre - Critical Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch (Exception dialogEx)
                {
                    Logger.Error("Failed to display crash dialog", dialogEx);
                }

                args.Handled = false;
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Logger.LogCrash("Unhandled AppDomain Exception", ex);
            }
            else
            {
                Logger.LogCrash($"Unhandled AppDomain Object: {args.ExceptionObject}");
            }
        };
    }
}
