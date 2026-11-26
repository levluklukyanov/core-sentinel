using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CoreSpectre.Services;

namespace CoreSpectre;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private CancellationTokenSource? _pipeCts;

    private static string GetUserScopeToken()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return (id.User?.Value ?? Environment.UserName).Replace("-", "");
        }
        catch
        {
            return Environment.UserName;
        }
    }

    private static string? ExtractCsbArg(string[] args)
    {
        foreach (var arg in args)
        {
            if (!string.IsNullOrWhiteSpace(arg) &&
                arg.EndsWith(".csb", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(arg))
            {
                return Path.GetFullPath(arg);
            }
        }
        return null;
    }

    private static async Task ForwardActivationToPrimaryAsync(string pipeName, string? csbPath)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(cts.Token);
            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            await writer.WriteLineAsync(string.IsNullOrEmpty(csbPath) ? "ACTIVATE" : $"OPEN:{csbPath}");
        }
        catch (Exception ex)
        {
            Logger.Debug($"Single-instance forward failed: {ex.Message}");
        }
    }

    private void StartActivationPipeListener(string pipeName, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var sec = new PipeSecurity();
                    using var id = WindowsIdentity.GetCurrent();
                    if (id.User != null)
                    {
                        sec.AddAccessRule(new PipeAccessRule(id.User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    }
                    try
                    {
                        // Allow Medium-IL secondary instance (.csb open) to signal an elevated primary instance
                        sec.SetSecurityDescriptorSddlForm("S:(ML;;NW;;;ME)", AccessControlSections.Audit);
                    }
                    catch (Exception sddlEx)
                    {
                        Logger.Debug($"Pipe SACL setup skipped: {sddlEx.Message}");
                    }

                    using var server = NamedPipeServerStreamAcl.Create(
                        pipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        inBufferSize: 4096,
                        outBufferSize: 1024,
                        sec);

                    await server.WaitForConnectionAsync(ct);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readCts.CancelAfter(TimeSpan.FromSeconds(2));
                    string? msg = await reader.ReadLineAsync(readCts.Token);
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        string? csbPath = msg.StartsWith("OPEN:", StringComparison.Ordinal) ? msg[5..] : null;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (MainWindow is MainWindow mw)
                            {
                                mw.HandleExternalCsbOpen(csbPath);
                            }
                        });
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    Logger.Debug("Activation pipe client read timed out.");
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Activation pipe loop warning: {ex.Message}");
                    await Task.Delay(250, ct);
                }
            }
        }, ct);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        int workerIdx = Array.FindIndex(e.Args, a => string.Equals(a, "--elevated-worker", StringComparison.OrdinalIgnoreCase));
        if (workerIdx >= 0 && workerIdx + 1 < e.Args.Length)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string pipeName = e.Args[workerIdx + 1];
            int code = 0;
            try
            {
                Logger.Info($"CoreSpectre elevated cleanup worker started for pipe: {pipeName}");
                code = await StorageService.RunElevatedWorkerAsync(pipeName);
            }
            catch (Exception ex)
            {
                Logger.Error("Elevated cleanup worker failed", ex);
                code = 1;
            }
            Shutdown(code);
            return;
        }

        string? startupCsbPath = ExtractCsbArg(e.Args);
        bool startMinimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        string scope = GetUserScopeToken();
        string mutexName = $"Local\\CoreSpectre_SingleInstance_{scope}";
        string activationPipeName = $"CoreSpectre_Activation_{scope}";

        _instanceMutex = new Mutex(initiallyOwned: true, mutexName, out bool isPrimary);
        if (!isPrimary)
        {
            await ForwardActivationToPrimaryAsync(activationPipeName, startupCsbPath);
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown(0);
            Environment.Exit(0);
            return;
        }

        // Инициализируем шрифты и ассоциацию .csb до создания главного окна
        SettingsPersistenceService.EnsureCsbFileAssociation();
        FontService.Initialize();
        Logger.Info("CoreSpectre application startup initialized.");

        DispatcherUnhandledException += (s, args) =>
        {
            var ex = args.Exception;
            Logger.Error($"Dispatcher unhandled exception: {ex.GetType().FullName}", ex);

            // Handle only well-known benign WPF clipboard/drag COM & Win32 races
            bool isBenign =
                (ex is System.Runtime.InteropServices.COMException comEx &&
                 (unchecked((uint)comEx.HResult) == 0x800401D0u || unchecked((uint)comEx.HResult) == 0x800401D3u)) ||
                (ex is System.ComponentModel.Win32Exception w32 &&
                 (w32.NativeErrorCode == 1418 || w32.NativeErrorCode == 1400));

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

        _pipeCts = new CancellationTokenSource();
        StartActivationPipeListener(activationPipeName, _pipeCts.Token);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        if (!string.IsNullOrEmpty(startupCsbPath))
        {
            mainWindow.Loaded += (_, _) => mainWindow.HandleExternalCsbOpen(startupCsbPath);
        }
        if (startMinimized && string.IsNullOrEmpty(startupCsbPath))
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(mainWindow);
            helper.EnsureHandle();
        }
        else
        {
            mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts?.Cancel();
        _pipeCts?.Dispose();
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
