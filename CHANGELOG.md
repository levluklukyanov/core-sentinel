# Changelog

All notable changes to **CoreSpectre** will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.2.0] - 2026-11-26

### Added
- **Windows Standby List & CompactOS with Seamless UAC Elevation:** Added on-demand UAC elevation over ACL-protected Named Pipes (`--elevated-worker`) for Standby List purging (`NtSetSystemInformation`) and Windows CompactOS compression (`compact.exe /CompactOS`) without closing the main window.
- **Reversible CPU Optimization (`EcoQoS` + Rollback):** Background throttling now stores original process priorities and restores them cleanly via **Restore CPU**.
- **Disk Analyzer, Dry-Run Preview & Temp Age Filter:** Inspect heavy folders, files >100 MB, backup archives, preview largest files per cleanup target, and filter `%TEMP%` cleanup by file age (`All`, `>3d`, `>7d`).
- **SHA-256 Verified `.csb` Settings Backups & Diagnostics Export:** Added `.csb` backup export/import with HKCU double-click file association and `.txt` hardware report export.
- **Tray Minimize, Global Hotkeys & Autostart:** Added system tray minimization, `Ctrl+Shift+H` / `L` / `P` hotkeys, custom process whitelist/game list, and optional Windows startup launch (`--minimized`).
- **MemoryService Unit Tests:** Added `MemoryServiceTests` covering protected process filtering, memory telemetry, and non-elevated Standby List permission checks.

### Changed
- **Renamed Game Sentinel to Game Mode (`GameBoostService`):** Unified naming across UI, localization dictionaries, service classes, tests, and documentation.
- **Streamlined UI & Architecture:** Removed redundant active process tables, unused ViewModels (`CommunityToolkit.Mvvm`), duplicate font deployment copies, and dead localization keys.

### Fixed & Hardened
- **CPU Throttling & CompactOS Reliability:** Fixed Win32 `ProcessPriorityClass` bitmask comparison so background processes running at `Normal` priority are properly throttled; fixed `IsCompactOsEnabledAsync` OEM encoding + exit code `103` detection on localized Windows and eliminated `compact.exe` stdout deadlock.
- **HUD Telemetry & Game Detection Guards:** Shared a single `HardwareTelemetry` snapshot pipeline with `TelemetryHudWindow`, cached `GPU Engine` performance counters safely across polls, and excluded browsers/chats/IDEs from false-positive window-title game detection.

### Security & Reliability
- **ACL Named Pipe IPC:** Eliminated on-disk job/result path passing for `--elevated-worker` by streaming jobs and logs strictly over a user-SID/Administrators-restricted Named Pipe.
- **Protected Process Safeguards:** `PurgeMemory` now skips critical Windows system, audio (`audiodg`), and Defender (`MsMpEng`) processes to prevent unnecessary page-fault stutters.
- **Hardened Exception & Single-Instance Handling:** Tightened `DispatcherUnhandledException` benign filter to explicit clipboard/window COM/Win32 codes, converted `ResetSettingsAsync` to `async Task`, and enforced single-instance process activation.

---

## [1.1.1] - 2026-11-25

### Fixed
- Resolved MVVM regressions from the initial decomposition and restored per-target cleanup checkboxes in `StorageView`.
- Implemented headless UAC cleanup worker over ACL Named Pipes and fixed duplicate instance accumulation.
- Refined UI layout, theme icon resources, UTC offset footer format, and Tab Vault restoration controls.

---

## [1.1.0] - 2026-11-24

### Added
- **Configurable C# Cleanup Engine**: Granular user selection of cleanup targets (Temporary Files, SoftwareDistribution cache, WER/CBS logs, DNS cache, Recycle Bin, DISM Component Store, DISM ResetBase) with persistent checkboxes.
- **Cleanup Gear Menu & Target Sorting**: Added gear dropdown menu for cleanup customization and dynamic target list sorting (Size descending, Size ascending, Name A-Z) persisted in `user_settings.json`.
- **System Restore Point & Rollback Integration**: Integrated automatic and manual Windows Restore Point creation (`Checkpoint-Computer`) and one-click access to the Windows System Restore wizard (`rstrui.exe`).
- **Thread-Safe Logging with Auto-Rotation**: Implemented `Logger` writing to `%LOCALAPPDATA%\CoreSpectre\logs\app.log` and `crash_log.txt` with automatic 1 MB rotation to `.old`.
- **AppData Settings & Portable Mode**: Configuration now persists safely in `%APPDATA%\CoreSpectre\user_settings.json`, with automatic migration from the program directory and support for `portable.flag`.
- **Centralized Localization**: Introduced `LocalizationService` supporting dynamic runtime switching between English and Russian across all views.
- **Unit Test Suite**: Added `CoreSpectre.Tests` (xUnit) testing file size formatting, safety defaults, settings serialization, launcher exclusions, and localization parity.
- **Automated GitHub Actions Release Pipeline**: Workflow `.github/workflows/release.yml` for automated testing, single-file compilation, and release packaging upon pushing `v*` tags.

### Changed
- **Architectural Decomposition**: Refactored monolithic `MainWindow` into dedicated UserControls (`OverviewView`, `PurgeView`, `StorageView`, `GamesView`, `SettingsView`) and MVVM ViewModels (`StorageViewModel`, `GamesViewModel`).
- **Honest Telemetry & Zero Fake Metrics**: Removed artificial fallback calculations in CPU optimization; replaced synthetic 8 GB / 50% fallbacks with honest `0L / N/A` indicators; replaced thread-halving with physical core WMI telemetry.
- **Administrative Access Guards**: Added explicit admin requirement checks and informative warnings before touching protected system paths (`SoftwareDistribution`, `CBS.log`, `DISM`).
- **Safety Parity in `clean.bat`**: Aligned standalone batch script with C# engine safety (Restore Point creation, `%SystemRoot%` dynamic paths, and removal of forced `/resetbase`).
- **Synchronized Documentation**: Updated `README.md` to accurately disclose all system modifications, SmartScreen warnings, and real Win32 `EmptyWorkingSet` memory trimming expectations.

### Fixed
- Audited and eliminated 48 silent empty `catch` blocks across all services, replacing them with structured debug and warning logging.
- Replaced dangerous global `e.Handled = true` in unhandled exception dispatcher with user error notifications and full stack trace capture.
- Converted background `async void` methods into proper awaitable `async Task` methods.

---

## [1.0.1] - 2026-11-20

### Added
- Tab Vault drawer for hiding, restoring, and reordering navigation tabs.
- Real-time CPU and RAM health score indicators.
- Embedded custom typography (Inter, JetBrains Mono, Golos Text, Manrope, Onest).

### Changed
- Refactored background telemetry polling to reduce UI thread dispatch overhead.
- Improved Dark and Light theme contrast for high-DPI displays.

---

## [1.0.0] - 2026-11-15

### Added
- Initial public release of CoreSpectre.
- Real-time hardware telemetry monitoring (CPU, RAM, Disks via WMI and Win32).
- Native Win32 memory working set trimming (`EmptyWorkingSet`).
- Game process detection and priority elevation engine.
- Disk partition diagnostics and temporary file cleanup.
- Floating semi-transparent HUD overlay.
