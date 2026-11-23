using System.IO;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CoreSentinel.Services;

public record CpuInfo(
    string Name,
    int Cores,
    int LogicalProcessors,
    string MaxClockMhz,
    string Architecture,
    string SocketDesignation
);

public record RamSlot(
    string BankLabel,
    long CapacityBytes,
    string FormattedSize,
    string Speed,
    string MemoryType,
    string Manufacturer
);

public record DiskDetail(
    string DriveLetter,
    string Label,
    long TotalBytes,
    long FreeBytes,
    string FileSystem,
    string DriveType,
    string MediaType,
    string FormattedTotal,
    string FormattedFree,
    double UsedPercent
);

public record NetworkAdapterInfo(
    string Name,
    string Description,
    string MacAddress,
    string IpAddress,
    string Speed,
    bool IsUp
)
{
    public string IpAndMac => $"IP: {(string.IsNullOrEmpty(IpAddress) ? "None" : IpAddress)} · MAC: {MacAddress} · {Speed}";
    public string StatusText { get; set; } = IsUp ? "Подключено" : "Отключено";
}

public record MotherboardInfo(
    string Manufacturer,
    string Product,
    string SerialNumber,
    string Version
);

public record BiosInfo(
    string Manufacturer,
    string Version,
    string ReleaseDate,
    string Caption
);

public record OsInfo(
    string Caption,
    string Version,
    string BuildNumber,
    string Architecture,
    string InstallDate,
    string LastBootTime,
    string RegisteredUser,
    string ComputerName
);

public record GpuInfo(
    string Name,
    string DriverVersion,
    long AdapterRamBytes,
    string FormattedRam
);

public static class SystemInfoService
{
    // ─── CPU ───────────────────────────────────────────────────────────────
    public static List<CpuInfo> GetCpuDetails(bool isEn = false)
    {
        var list = new List<CpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                uint cores  = (uint)(obj["NumberOfCores"] ?? 0u);
                uint logical = (uint)(obj["NumberOfLogicalProcessors"] ?? 0u);
                uint clock  = (uint)(obj["MaxClockSpeed"] ?? 0u);
                string clockStr = clock > 0 
                    ? (isEn ? $"{clock / 1000.0:0.00} GHz" : $"{clock / 1000.0:0.00} ГГц") 
                    : "—";
                list.Add(new CpuInfo(
                    Name: obj["Name"]?.ToString()?.Trim() ?? "—",
                    Cores: (int)cores,
                    LogicalProcessors: (int)logical,
                    MaxClockMhz: clockStr,
                    Architecture: GetCpuArch(obj["Architecture"]),
                    SocketDesignation: obj["SocketDesignation"]?.ToString() ?? "—"
                ));
            }
        }
        catch { list.Add(new CpuInfo(isEn ? "No data" : "Нет данных", 0, 0, "—", "—", "—")); }
        return list;
    }

    private static string GetCpuArch(object? arch) => arch?.ToString() switch
    {
        "0" => "x86", "1" => "MIPS", "2" => "Alpha", "3" => "PowerPC",
        "5" => "ARM", "6" => "ia64", "9" => "x64", _ => "—"
    };

    // ─── RAM ───────────────────────────────────────────────────────────────
    public static List<RamSlot> GetRamDetails(bool isEn = false)
    {
        var list = new List<RamSlot>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
            foreach (ManagementObject obj in searcher.Get())
            {
                ulong cap = (ulong)(obj["Capacity"] ?? 0UL);
                string memType = GetMemoryType(obj["MemoryType"]);
                string speedUnit = isEn ? "MHz" : "МГц";
                list.Add(new RamSlot(
                    BankLabel: obj["BankLabel"]?.ToString() ?? obj["DeviceLocator"]?.ToString() ?? "—",
                    CapacityBytes: (long)cap,
                    FormattedSize: StorageService.FormatBytes((long)cap),
                    Speed: obj["Speed"] != null ? $"{obj["Speed"]} {speedUnit}" : "—",
                    MemoryType: memType,
                    Manufacturer: obj["Manufacturer"]?.ToString()?.Trim() ?? "—"
                ));
            }
        }
        catch { }
        return list;
    }

    private static string GetMemoryType(object? t) => t?.ToString() switch
    {
        "20" => "DDR", "21" => "DDR2", "22" => "DDR2 FB-DIMM",
        "24" => "DDR3", "26" => "DDR4", "34" => "DDR5",
        _ => "DRAM"
    };

    // ─── DISKS ─────────────────────────────────────────────────────────────
    public static List<DiskDetail> GetAllDiskDetails(bool isEn = false)
    {
        var list = new List<DiskDetail>();
        try
        {
            // Get media types from Win32_DiskDrive via partition/disk mapping
            var mediaTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var diskSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
                foreach (ManagementObject disk in diskSearcher.Get())
                {
                    string media = disk["MediaType"]?.ToString() ?? "";
                    using var partRelated = disk.GetRelated("Win32_DiskPartition");
                    foreach (ManagementObject part in partRelated)
                    {
                        using var logRelated = part.GetRelated("Win32_LogicalDisk");
                        foreach (ManagementObject log in logRelated)
                        {
                            string? letter = log["DeviceID"]?.ToString();
                            if (!string.IsNullOrEmpty(letter))
                                mediaTypes[letter] = media;
                        }
                    }
                }
            }
            catch { }

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                long total = drive.TotalSize;
                long free  = drive.AvailableFreeSpace;
                double usedPct = total > 0 ? (double)(total - free) / total * 100.0 : 0;
                string letter = drive.Name.TrimEnd('\\');
                string media = mediaTypes.TryGetValue(letter.TrimEnd(':'), out var mt) ? mt : "—";

                string defaultLabel = isEn ? "Local Disk" : "Локальный диск";
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? defaultLabel : drive.VolumeLabel;

                list.Add(new DiskDetail(
                    DriveLetter: letter,
                    Label: label,
                    TotalBytes: total,
                    FreeBytes: free,
                    FileSystem: drive.DriveFormat,
                    DriveType: drive.DriveType.ToString(),
                    MediaType: media,
                    FormattedTotal: StorageService.FormatBytes(total),
                    FormattedFree: StorageService.FormatBytes(free),
                    UsedPercent: Math.Round(usedPct, 1)
                ));
            }
        }
        catch { }
        return list;
    }

    // ─── MOTHERBOARD ───────────────────────────────────────────────────────
    public static MotherboardInfo GetMotherboard()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
            foreach (ManagementObject obj in searcher.Get())
            {
                return new MotherboardInfo(
                    Manufacturer: obj["Manufacturer"]?.ToString()?.Trim() ?? "—",
                    Product: obj["Product"]?.ToString()?.Trim() ?? "—",
                    SerialNumber: obj["SerialNumber"]?.ToString()?.Trim() ?? "—",
                    Version: obj["Version"]?.ToString()?.Trim() ?? "—"
                );
            }
        }
        catch { }
        return new MotherboardInfo("—", "—", "—", "—");
    }

    // ─── BIOS ──────────────────────────────────────────────────────────────
    public static BiosInfo GetBios()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
            foreach (ManagementObject obj in searcher.Get())
            {
                string rawDate = obj["ReleaseDate"]?.ToString() ?? "";
                string date = rawDate.Length >= 8
                    ? $"{rawDate[6..8]}.{rawDate[4..6]}.{rawDate[0..4]}"
                    : rawDate;

                return new BiosInfo(
                    Manufacturer: obj["Manufacturer"]?.ToString()?.Trim() ?? "—",
                    Version: obj["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? "—",
                    ReleaseDate: date,
                    Caption: obj["Caption"]?.ToString()?.Trim() ?? "—"
                );
            }
        }
        catch { }
        return new BiosInfo("—", "—", "—", "—");
    }

    // ─── OS ────────────────────────────────────────────────────────────────
    public static OsInfo GetOsInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                string rawInstall = obj["InstallDate"]?.ToString() ?? "";
                string rawBoot    = obj["LastBootUpTime"]?.ToString() ?? "";
                return new OsInfo(
                    Caption: obj["Caption"]?.ToString()?.Trim() ?? "Windows",
                    Version: obj["Version"]?.ToString() ?? "—",
                    BuildNumber: obj["BuildNumber"]?.ToString() ?? "—",
                    Architecture: obj["OSArchitecture"]?.ToString() ?? "—",
                    InstallDate: ParseWmiDate(rawInstall),
                    LastBootTime: ParseWmiDate(rawBoot),
                    RegisteredUser: obj["RegisteredUser"]?.ToString() ?? Environment.UserName,
                    ComputerName: obj["CSName"]?.ToString() ?? Environment.MachineName
                );
            }
        }
        catch { }
        return new OsInfo(
            "Windows",
            Environment.OSVersion.Version.ToString(),
            Environment.OSVersion.Version.Build.ToString(),
            Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
            "—", "—",
            Environment.UserName,
            Environment.MachineName
        );
    }

    private static string ParseWmiDate(string wmiDate)
    {
        if (wmiDate.Length < 14) return wmiDate;
        try
        {
            int yr  = int.Parse(wmiDate[0..4]);
            int mo  = int.Parse(wmiDate[4..6]);
            int day = int.Parse(wmiDate[6..8]);
            int hr  = int.Parse(wmiDate[8..10]);
            int min = int.Parse(wmiDate[10..12]);
            return $"{day:D2}.{mo:D2}.{yr} {hr:D2}:{min:D2}";
        }
        catch { return wmiDate; }
    }

    // ─── GPU ───────────────────────────────────────────────────────────────
    public static List<GpuInfo> GetGpus()
    {
        var list = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                ulong ram = 0;
                try { ram = (ulong)(uint)(obj["AdapterRAM"] ?? 0u); } catch { }
                list.Add(new GpuInfo(
                    Name: obj["Name"]?.ToString()?.Trim() ?? "—",
                    DriverVersion: obj["DriverVersion"]?.ToString() ?? "—",
                    AdapterRamBytes: (long)ram,
                    FormattedRam: ram > 0 ? StorageService.FormatBytes((long)ram) : "—"
                ));
            }
        }
        catch { }
        return list;
    }

    // ─── NETWORK ───────────────────────────────────────────────────────────
    public static List<NetworkAdapterInfo> GetNetworkAdapters(bool isEn = false)
    {
        var list = new List<NetworkAdapterInfo>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                         && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel))
            {
                var props = nic.GetIPProperties();
                string ip = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.Address.ToString() ?? "—";

                byte[] macBytes = nic.GetPhysicalAddress().GetAddressBytes();
                string mac = macBytes.Length > 0
                    ? string.Join(":", macBytes.Select(b => b.ToString("X2")))
                    : "—";

                long speedBps = 0;
                try { speedBps = nic.Speed; } catch { }
                string gbps = isEn ? "Gbps" : "Гбит/с";
                string mbps = isEn ? "Mbps" : "Мбит/с";
                string kbps = isEn ? "Kbps" : "Кбит/с";
                string speed = speedBps > 0
                    ? speedBps >= 1_000_000_000 ? $"{speedBps / 1_000_000_000.0:0.#} {gbps}"
                    : speedBps >= 1_000_000 ? $"{speedBps / 1_000_000.0:0.#} {mbps}"
                    : $"{speedBps / 1000.0:0.#} {kbps}"
                    : "—";

                list.Add(new NetworkAdapterInfo(
                    Name: nic.Name,
                    Description: nic.Description,
                    MacAddress: mac,
                    IpAddress: ip,
                    Speed: speed,
                    IsUp: nic.OperationalStatus == OperationalStatus.Up
                ) {
                    StatusText = nic.OperationalStatus == OperationalStatus.Up
                        ? (isEn ? "Connected" : "Подключено")
                        : (isEn ? "Disconnected" : "Отключено")
                });
            }
        }
        catch { }
        return list;
    }

    // ─── UPTIME ────────────────────────────────────────────────────────────
    public static string GetUptime(bool isEn = false)
    {
        try
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            string dayUnit = isEn ? "d" : "д";
            return $"{(int)uptime.TotalDays}{dayUnit} {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
        }
        catch { return "—"; }
    }

    // ─── STARTUP ───────────────────────────────────────────────────────────
    // See StartupService.cs
}
