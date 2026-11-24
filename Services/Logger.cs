using System;
using System.IO;

namespace CoreSentinel.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public static class Logger
{
    private static readonly object _lock = new();
    private static readonly string _logDir;
    private static readonly string _appLogFile;
    private static readonly string _crashLogFile;
    private const long MaxLogSizeBytes = 1024 * 1024; // 1 MB

    static Logger()
    {
        _logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoreSentinel",
            "logs");
        _appLogFile = Path.Combine(_logDir, "app.log");
        _crashLogFile = Path.Combine(_logDir, "crash_log.txt");

        try
        {
            Directory.CreateDirectory(_logDir);
        }
        catch { }
    }

    public static string LogDirectory => _logDir;
    public static string AppLogFilePath => _appLogFile;
    public static string CrashLogFilePath => _crashLogFile;

    public static void Debug(string message) => Log(LogLevel.Debug, message);
    public static void Info(string message) => Log(LogLevel.Info, message);
    public static void Warn(string message) => Log(LogLevel.Warning, message);
    public static void Error(string message, Exception? ex = null)
    {
        string text = ex != null ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
        Log(LogLevel.Error, text);
    }

    public static void Log(LogLevel level, string message)
    {
        lock (_lock)
        {
            try
            {
                RotateIfNeeded(_appLogFile);
                string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant(),-7}] [T{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";
                File.AppendAllText(_appLogFile, entry);
            }
            catch
            {
                // Never throw from logger
            }
        }
    }

    public static void LogCrash(string message, Exception? ex = null)
    {
        lock (_lock)
        {
            try
            {
                RotateIfNeeded(_crashLogFile);
                string text = ex != null
                    ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] CRASH: {message}\nException: {ex.GetType().FullName}: {ex.Message}\nStackTrace:\n{ex.StackTrace}\n\n"
                    : $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] CRASH: {message}\n\n";
                File.AppendAllText(_crashLogFile, text);
            }
            catch { }
        }
    }

    private static void RotateIfNeeded(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var fi = new FileInfo(filePath);
                if (fi.Length >= MaxLogSizeBytes)
                {
                    string oldPath = filePath + ".old";
                    if (File.Exists(oldPath))
                    {
                        File.Delete(oldPath);
                    }
                    File.Move(filePath, oldPath);
                }
            }
        }
        catch { }
    }
}
