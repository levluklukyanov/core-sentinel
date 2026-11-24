# Changelog

All notable changes to **CoreSentinel** will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.1.0] - 2026-11-24

### Added
- **Configurable C# Cleanup Engine**: Granular user selection of cleanup targets (Temporary Files, SoftwareDistribution cache, WER/CBS logs, DNS cache, Recycle Bin, DISM Component Store, DISM ResetBase) with persistent checkboxes.
- **Cleanup Gear Menu & Target Sorting**: Added gear dropdown menu for cleanup customization and dynamic target list sorting (Size descending, Size ascending, Name A-Z) persisted in `user_settings.json`.
- **System Restore Point & Rollback Integration**: Integrated automatic and manual Windows Restore Point creation (`Checkpoint-Computer`) and one-click access to the Windows System Restore wizard (`rstrui.exe`).
- **Thread-Safe Logging with Auto-Rotation**: Implemented `Logger` writing to `%LOCALAPPDATA%\CoreSentinel\logs\app.log` and `crash_log.txt` with automatic 1 MB rotation to `.old`.
- **AppData Settings & Portable Mode**: Configuration now persists safely in `%APPDATA%\CoreSentinel\user_settings.json`, with automatic migration from the program directory and support for `portable.flag`.
- **Centralized Localization**: Introduced `LocalizationService` supporting dynamic runtime switching between English and Russian across all views.
- **Unit Test Suite**: Added `CoreSentinel.Tests` (xUnit) testing file size formatting, safety defaults, settings serialization, launcher exclusions, and localization parity.
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
- Initial public release of CoreSentinel.
- Real-time hardware telemetry monitoring (CPU, RAM, Disks via WMI and Win32).
- Native Win32 memory working set trimming (`EmptyWorkingSet`).
- Game process detection and priority elevation engine.
- Disk partition diagnostics and temporary file cleanup.
- Floating semi-transparent HUD overlay.
