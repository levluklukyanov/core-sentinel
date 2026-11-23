using Microsoft.Win32;

namespace CoreSentinel.Services;

public record StartupEntry(
    string Name,
    string Command,
    string Source,
    bool IsEnabled
)
{
    public string EnabledText => IsEnabled ? "Вкл" : "Выкл";
}

public static class StartupService
{
    private static readonly (string KeyPath, string Hive, bool IsUser)[] Sources =
    [
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU", true),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM", false),
        (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM", false)
    ];

    private static readonly (string KeyPath, string Hive)[] DisabledSources =
    [
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "HKCU"),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", "HKLM"),
        (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", "HKLM")
    ];

    public static List<StartupEntry> GetAllEntries()
    {
        var entries = new List<StartupEntry>();
        var disabledNames = GetDisabledNames();

        foreach (var (keyPath, hive, isUser) in Sources)
        {
            try
            {
                var rootKey = isUser ? Registry.CurrentUser : Registry.LocalMachine;
                using var key = rootKey.OpenSubKey(keyPath, writable: false);
                if (key == null) continue;

                foreach (var name in key.GetValueNames())
                {
                    string cmd = key.GetValue(name)?.ToString() ?? "";
                    bool enabled = !disabledNames.Contains(name, StringComparer.OrdinalIgnoreCase);
                    entries.Add(new StartupEntry(
                        Name: name,
                        Command: cmd,
                        Source: $"{hive}\\...\\Run",
                        IsEnabled: enabled
                    ));
                }
            }
            catch { }
        }

        return entries.DistinctBy(e => e.Name).ToList();
    }

    private static HashSet<string> GetDisabledNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (keyPath, hive) in DisabledSources)
        {
            try
            {
                var root = hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                using var key = root.OpenSubKey(keyPath, writable: false);
                if (key == null) continue;
                foreach (var name in key.GetValueNames())
                {
                    var data = key.GetValue(name) as byte[];
                    // StartupApproved: first byte 0x03 = disabled
                    if (data != null && data.Length > 0 && data[0] == 0x03)
                        set.Add(name);
                }
            }
            catch { }
        }
        return set;
    }

    public static bool SetEntryEnabled(StartupEntry entry, bool enable)
    {
        // We modify the StartupApproved key to toggle without deleting the entry
        var approvedPaths = new[]
        {
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
        };

        foreach (var (root, path) in approvedPaths)
        {
            try
            {
                using var key = root.OpenSubKey(path, writable: true);
                if (key == null) continue;
                if (key.GetValueNames().Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                {
                    // enabled = bytes[0]=0x02, disabled = bytes[0]=0x03
                    var existing = key.GetValue(entry.Name) as byte[] ?? new byte[12];
                    if (existing.Length > 0)
                        existing[0] = enable ? (byte)0x02 : (byte)0x03;
                    key.SetValue(entry.Name, existing, RegistryValueKind.Binary);
                    return true;
                }
            }
            catch { }
        }

        // If not found in StartupApproved, write directly into Run key
        try
        {
            var root = entry.Source.StartsWith("HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
            using var key = root.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key != null)
            {
                if (enable)
                    key.SetValue(entry.Name, entry.Command);
                else
                    key.DeleteValue(entry.Name, throwOnMissingValue: false);
                return true;
            }
        }
        catch { }
        return false;
    }

    public static bool DeleteEntry(StartupEntry entry)
    {
        try
        {
            var root = entry.Source.StartsWith("HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
            using var key = root.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue(entry.Name, throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }
}
