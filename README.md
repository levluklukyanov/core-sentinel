# CoreSpectre

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0078D6)](https://github.com/levluklukyanov/core-spectre)
[![Framework](https://img.shields.io/badge/.NET-10.0%20WPF-512BD4)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/C%23-13.0-239120)](https://learn.microsoft.com/dotnet/csharp/)
[![Release](https://img.shields.io/badge/Release-v1.2.0-success)](https://github.com/levluklukyanov/core-spectre/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

High-performance Windows system utility, real-time hardware telemetry monitor, and gaming resource manager built with C# 13 and .NET 10 (WPF).

![CoreSpectre Preview](docs/preview.png)

---

## Overview

CoreSpectre provides real-time system monitoring, dual-engine memory and CPU optimization, game process prioritization, and disk maintenance within a single, low-overhead desktop interface designed with engineering minimalism.

## Features

### Hardware Telemetry
- Real-time tracking of CPU load, RAM allocation, and storage drives via WMI and Win32 Native APIs.
- Detailed operation log with timestamps and horizontal navigation.

### Resource Optimization
- Memory purge: working set trimming via native Win32 `EmptyWorkingSet` (protecting critical Windows and antivirus processes) plus on-demand Windows **Standby List** flushing (`NtSetSystemInformation`) with seamless UAC elevation.
- Reversible CPU balancing: applies Windows EcoQoS (`PROCESS_POWER_THROTTLING_EXECUTION_SPEED`), `MEMORY_PRIORITY_LOW`, and `BelowNormal` priority to background utilities with one-click restore (`Restore CPU`).

### Game Mode
- Automatic detection of active game sessions with instant execution priority boost and automatic priority restoration on exit.
- Storage-wide game library scanner with NTFS Junction deduplication and Minecraft launcher filtering.

### Storage Management & System Maintenance
- **Configurable Cleanup Engine:** Granular per-target checkboxes directly in the target list (Temporary files with age filter, Windows Update `SoftwareDistribution` cache, Windows Error Reporting and CBS logs, GPU shader cache, browser HTTP cache, DNS cache, Recycle Bin, DISM Component Store maintenance, and optional ResetBase) with automatic 30-second background cache rescanning.
- **CompactOS Integration:** Toggle Windows WOF system file compression (`compact.exe /CompactOS`) directly from Storage with on-demand UAC elevation.
- **Disk Analyzer & File Preview:** Inspect heavy folders, files over 100 MB, backup archives, and preview largest files inside each cleanup target before deleting.
- **System Restore Point & Rollback Protection:** Automated Windows System Restore Point creation (`Checkpoint-Computer`) before deep cleanup operations, with one-click access to the Windows System Restore wizard (`rstrui.exe`).
- **Interactive Target Sorting:** Organize detected targets by Size (Descending / Ascending) or Alphabetical Name (A–Z), persisted across application sessions.

### Floating Telemetry HUD
- Low-overhead, semi-transparent overlay widget for on-screen monitoring during gaming sessions and compute workloads.

### Interface & Ergonomics
- Clean Dark and Light themes adhering to Swiss modernist and engineering design principles.
- Dynamic runtime localization (English and Russian) across all views via `LocalizationService`.
- Tab Vault system to hide, restore, and reorder navigation tabs.
- Bundled system fonts: Golos Text, Inter, JetBrains Mono, Manrope, Onest.
- Safe settings storage in `%APPDATA%\CoreSpectre\user_settings.json` with automatic legacy migration and portable mode (`portable.flag`).

### Architecture & Reliability
- **Modular Structure:** Decoupled UserControls (`OverviewView`, `PurgeView`, `StorageView`, `GamesView`, `SettingsView`) and dedicated system services.
- **Thread-Safe Rotating Logging:** Comprehensive diagnostics at `%LOCALAPPDATA%\CoreSpectre\logs\app.log` and `crash_log.txt` with automatic 1 MB rotation (`.old`).
- **Encrypted-Checksum `.csb` Backups & Diagnostics Export:** Save/load SHA-256 verified settings backups (with HKCU `.csb` double-click file association) and export system diagnostic reports.
- **Automated Unit Testing:** Dedicated xUnit test suite (`CoreSpectre.Tests`) validating size calculations, safety defaults, protected process guards, settings/backup integrity, game detection, launcher exclusions, and UI resource loading.

---

## Memory Trimming & Honest Expectations

CoreSpectre uses the native Win32 `EmptyWorkingSet` API to trim memory working sets. This function requests Windows to move unreferenced pages from physical RAM into the standby pool or pagefile.

- **Realistic Effect:** Trimming working sets provides an immediate, observable reduction in reported RAM usage, which can be useful before launching demanding workloads or games.
- **No Magic Acceleration:** It does not magically accelerate Windows. As active applications demand their data, Windows will fault required pages back into physical RAM. During this transition, a brief increase in disk I/O may occur. CoreSpectre does not make unsubstantiated claims of "doubling PC speed".

---

## What the App Modifies in the System

CoreSpectre operates transparently and strictly modifies only documented system locations:

### 1. File System Cleanup Targets
- **User Space (Standard Rights):**
  - `%TEMP%` (Current user temporary directory, with optional age filter)
  - `%LOCALAPPDATA%\CrashDumps` (User crash dump files)
  - `%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive` and `ReportQueue` (User error reports)
  - `%LOCALAPPDATA%\D3DSCache`, `%LOCALAPPDATA%\NVIDIA\{DXCache,GLCache}`, `LocalLow\NVIDIA\PerDriverVersion\DXCache`, `%LOCALAPPDATA%\AMD\{DxCache,GLCache}` (Optional GPU shader caches)
  - Chromium browser HTTP/JS caches (`Cache\Cache_Data` and `Code Cache`) for Chrome, Edge, Yandex Browser, and Brave (Optional)
  - `$Recycle.Bin` (Emptied via native Shell32 API across mounted volumes)
- **System Space (Requires Administrator Privileges):**
  - `%SystemRoot%\Temp` (Windows system temporary files)
  - `%SystemRoot%\SoftwareDistribution\Download` (Cached Windows update installation files)
  - `%SystemRoot%\SoftwareDistribution\DeliveryOptimization` (Peer-to-peer update distribution cache)
  - `%SystemRoot%\Logs\CBS` (Archived Component-Based Servicing logs; active `CBS.log` is strictly preserved)
  - `%ProgramData%\Microsoft\Windows\WER\ReportArchive` and `ReportQueue` (Machine-wide error report archives)
  - Windows Component Store: `dism.exe /Online /Cleanup-Image /StartComponentCleanup` to reclaim superseded WinSxS payloads. Note: CoreSpectre does not execute `/ResetBase` by default to preserve update rollback capabilities.

### 2. Windows Registry & Task Scheduler
- **Startup Manager:**
  - `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` and `StartupApproved\Run` (User autostart items and optional CoreSpectre `--minimized` entry).
  - `HKCU\Software\Classes\.csb` and `CoreSpectre.Backup` (Per-user `.csb` backup file association).
  - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`, `WOW6432Node\...\Run`, and `StartupApproved\Run` (Machine-wide autostart items; requires Administrator privileges).
  - Non-Microsoft Logon/Boot Scheduled Tasks via Windows Task Scheduler (`schtasks.exe` / `ScheduledTasks`).

### 3. Process Execution Priorities
- **CPU Optimization:** Temporarily applies EcoQoS (`PROCESS_POWER_THROTTLING_EXECUTION_SPEED`), `MEMORY_PRIORITY_LOW`, and `BelowNormal` priority to select background utilities (`OneDrive`, `Dropbox`, `GoogleDriveSync`, `SearchIndexer`, `CompatTelRunner`, `mscorsvw`, `AdobeUpdateService`, `armsvc`, `EpicWebHelper`) and tracks original states for instant rollback.
- **Game Mode:** Elevates the execution priority of recognized active game processes to `High` to ensure consistent frame pacing, and restores original priorities on game exit.

### 4. Administrator Privileges (UAC)
The core application is fully operational under a standard, non-elevated user account for telemetry, working set trimming, user temp cleanup, and HKCU startup management. Deep system maintenance (`Windows\Temp`, `SoftwareDistribution`, `CBS` logs, DISM), Windows Standby List purging, CompactOS compression, and HKLM registry modifications request elevation on demand via UAC and execute through a hidden `--elevated-worker` process over an ACL-protected Named Pipe without closing the main window.
Optional standalone CLI maintenance is also available via [scripts/clean.bat](scripts/clean.bat) (bundled as `clean.bat` in `CoreSpectre-win-x64.zip`).

### 5. System Restore Points
Before executing deep cleanup routines, CoreSpectre can trigger a native Windows System Restore checkpoint via PowerShell (`Checkpoint-Computer -Description 'CoreSpectre Before Deep Cleanup' -RestorePointType 'MODIFY_SETTINGS'`). This allows users to revert system state via `rstrui.exe` at any time.

---

## Windows SmartScreen and Antivirus Notice

CoreSpectre is a free, open-source utility distributed without an expensive commercial Extended Validation (EV) Code Signing certificate. Because the binary interacts with Win32 process APIs, system cleanup paths, and registry autostart keys, Windows SmartScreen ("Windows protected your PC") or certain antivirus heuristics may display an alert upon first launch.

The entire source code is 100% open, auditable, and can be built directly from source using the instructions below.

---

## Installation

1. Navigate to the [Releases](https://github.com/levluklukyanov/core-spectre/releases) page.
2. Download the standalone executable `CoreSpectre.exe`.
3. Launch the application.

> **Note on executable size (~65 MB):**  
> The application is packaged as a Self-Contained Single-File binary. The complete .NET 10 runtime, native WPF dependencies, and typography assets are embedded directly into the executable. It runs out of the box on clean Windows 10/11 installations without requiring prior installation of the .NET SDK or Runtime packages.

---

## Building from Source

### Prerequisites
- Windows 10 / 11 (x64)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/)

### Build Commands

```bash
# Clone the repository
git clone https://github.com/levluklukyanov/core-spectre.git
cd core-spectre

# Run unit test suite
dotnet test CoreSpectre.sln -c Release

# Build the solution in Release configuration
dotnet build CoreSpectre.sln -c Release

# Publish standalone single-file binary
dotnet publish CoreSpectre.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
```

---

## License

Distributed under the [MIT License](LICENSE).

> **Disclaimer:** CoreSpectre interacts with Windows cleanup paths, system restore checkpoints, Win32 memory APIs, and startup registry keys. Although protected system directories and restore points are safeguarded by design, all system maintenance operations are performed at the user's own discretion and responsibility.
