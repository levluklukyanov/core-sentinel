using System;
using System.Collections.Generic;

namespace CoreSpectre.Services;

public static class LocalizationService
{
    public static event Action<string>? LanguageChanged;

    private static string _currentLanguage = "EN";

    public static string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            string normalized = value?.ToUpperInvariant() == "RU" ? "RU" : "EN";
            if (_currentLanguage != normalized)
            {
                _currentLanguage = normalized;
                HardwareTelemetry.IsRussianLanguage = IsRussian;
                LanguageChanged?.Invoke(_currentLanguage);
            }
        }
    }

    public static bool IsRussian => _currentLanguage == "RU";
    public static bool IsEnglish => _currentLanguage == "EN";

    private static readonly Dictionary<string, (string EN, string RU)> Strings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Navigation
        ["Nav.Navigation"] = ("NAVIGATION", "НАВИГАЦИЯ"),
        ["Nav.Overview"] = ("Overview", "Обзор"),
        ["Nav.RamPurge"] = ("RAM Purge", "Очистка ОЗУ"),
        ["Nav.CpuOpt"] = ("CPU Optimization", "Оптимизация ЦП"),
        ["Nav.Storage"] = ("Storage", "Хранилище"),
        ["Nav.Games"] = ("Game Sentinel", "Игровой модуль"),
        ["Nav.Settings"] = ("Settings", "Настройки"),

        // Dashboard / Overview
        ["Dash.Title"] = ("System Overview", "Обзор системы"),
        ["Dash.Subtitle"] = ("Real-time hardware telemetry and background process monitoring", "Мониторинг физических ресурсов и фоновых процессов в реальном времени"),
        ["Dash.CardCpu"] = ("PROCESSOR (CPU)", "ПРОЦЕССОР (CPU)"),
        ["Dash.CardRam"] = ("PHYSICAL MEMORY", "ОПЕРАТИВНАЯ ПАМЯТЬ"),
        ["Dash.CardEnv"] = ("Gaming environment and storage status", "Состояние игрового окружения и накопителей"),
        ["Dash.GamePriority"] = ("Game Process Priority", "Приоритет игровых процессов"),
        ["Dash.HealthStatus"] = ("System Health Status", "Статус здоровья системы"),
        ["Dash.QuickActions"] = ("Quick Actions", "Быстрые действия"),
        ["Dash.FlushRam"] = ("Flush RAM Cache", "Сбросить кэш ОЗУ"),
        ["Dash.ToggleHud"] = ("Toggle HUD Overlay", "Включить HUD Оверлей"),
        ["Dash.CleanJunk"] = ("Clean Temporary Files", "Очистить временные файлы"),

        // ToolTips
        ["Tip.TaskMgr"] = ("Open Windows Task Manager", "Открыть Диспетчер задач Windows"),
        ["Tip.RamUsage"] = ("RAM Usage", "Использование ОЗУ"),
        ["Tip.OpenExplorer"] = ("Open drive in Windows Explorer", "Открыть накопитель в Проводнике"),
        ["Tip.ToggleHud"] = ("Toggle compact floating HUD overlay", "Открыть компактный плавающий оверлей HUD"),
        ["Tip.CpuAction"] = ("Switch to CPU Optimization tab", "Перейти на вкладку Оптимизации ЦП"),
        ["Tip.RamAction"] = ("Switch to Memory Purge tab", "Перейти на вкладку Очистки ОЗУ"),

        // Purge Tab
        ["Purge.RamTitle"] = ("Memory Purge", "Очистка ОЗУ"),
        ["Purge.RamSub"] = ("Working set trimming: signals OS to trim memory pages (temporary effect)", "Сброс рабочих наборов: кратковременно освобождает физическую память"),
        ["Purge.RamActionBtn"] = ("Purge Working Set", "Очистить рабочие наборы"),
        ["Purge.CpuTitle"] = ("CPU Optimization", "Оптимизация ЦП"),
        ["Purge.CpuSub"] = ("Normalizes background worker thread priority and throttles idle utilities", "Нормализует приоритеты фоновых потоков и снижает нагрузку фоновых утилит"),
        ["Purge.CpuActionBtn"] = ("Optimize CPU Load", "Оптимизировать нагрузку ЦП"),
        ["Purge.SwitchRamMode"] = ("Memory Purge Mode", "Режим очистки ОЗУ"),
        ["Purge.SwitchCpuMode"] = ("CPU Optimization Mode", "Режим оптимизации ЦП"),
        ["Purge.ClearConsole"] = ("Clear Console", "Очистить консоль"),

        // Storage Tab
        ["Storage.Title"] = ("Storage & System Cleanup", "Хранилище и системная очистка"),
        ["Storage.Subtitle"] = ("Monitor drive capacity and safely remove temporary cache files", "Контроль свободного места накопителей и безопасное удаление временных файлов"),
        ["Storage.Capacity"] = ("Drive Capacity:", "Емкость накопителя:"),
        ["Storage.Targets"] = ("SAFE CLEANUP TARGETS", "ОБЛАСТИ БЕЗОПАСНОЙ ОЧИСТКИ"),
        ["Storage.ScanBtn"] = ("Scan Cache", "Сканировать кэш"),
        ["Storage.CleanBtn"] = ("Clean System Cache", "Очистить систему"),
        ["Storage.CleaningBtn"] = ("Cleaning...", "Очистка..."),
        ["Storage.RollbackBtn"] = ("Rollback", "Откат системы"),
        ["Storage.AdminBtn"] = ("Run as Admin", "Запуск от админа"),
        ["Storage.ConsoleTitle"] = ("ACTIVITY & CLEANUP CONSOLE", "КОНСОЛЬ АКТИВНОСТИ И ОЧИСТКИ"),
        ["Storage.SortSizeDesc"] = ("Size (Largest first)", "По размеру (убывание)"),
        ["Storage.SortSizeAsc"] = ("Size (Smallest first)", "По размеру (возрастание)"),
        ["Storage.SortNameAsc"] = ("Name (A-Z)", "По имени (А-Я)"),

        // Games Tab
        ["Games.Title"] = ("Game Sentinel & Catalog", "Игровой модуль и каталог игр"),
        ["Games.Subtitle"] = ("Active games telemetry, priority acceleration, and installed disk libraries", "Телеметрия активных игр, повышение приоритета и каталог установленных игр"),
        ["Games.BannerTitle"] = ("Game Priority Booster", "Оптимизация приоритета игр"),
        ["Games.BannerSub"] = ("Elevates process CPU priority to High for active games (Minecraft, CS2, Dota 2, and others) to ensure maximum FPS.", "Повышает приоритет ЦП до Высокого для запущенных игр (Minecraft, CS2, Dota 2 и др.) для максимального FPS."),
        ["Games.BoostBtn"] = ("Boost Game Priority", "Повысить приоритет игр"),
        ["Games.ActiveTitle"] = ("ACTIVE GAMES", "АКТИВНЫЕ ИГРЫ"),
        ["Games.InstalledTitle"] = ("GAMES ON THIS DEVICE", "ИГРЫ НА УСТРОЙСТВЕ"),
        ["Games.InstalledSub"] = ("Scan storage drives for installed games, libraries and launchers", "Поиск установленных игр и библиотек на дисках компьютера"),
        ["Games.ScanDrivesBtn"] = ("Scan Drives", "Сканировать диски"),
        ["Games.NoActive"] = ("No games currently running.", "В данный момент игры не запущены."),
        ["Games.NoScan"] = ("No scan performed yet. Select a drive and click 'Scan Drives'.", "Сканирование еще не выполнялось. Выберите диск и нажмите 'Сканировать диски'."),
        ["Games.AllDrives"] = ("All Drives", "Все диски"),

        // Settings Tab
        ["Settings.Title"] = ("Settings", "Настройки"),
        ["Settings.Subtitle"] = ("System configuration, customization and hardware specifications", "Конфигурация системы, кастомизация и информация об оборудовании"),
        ["Settings.Customization"] = ("Customization", "Кастомизация"),
        ["Settings.Monitoring"] = ("Monitoring & Thresholds", "Мониторинг и пороги"),
        ["Settings.SysInfo"] = ("System Hardware & Specs", "Характеристики оборудования"),
        ["Settings.Language"] = ("Interface Language", "Язык интерфейса"),
        ["Settings.Theme"] = ("Interface Theme", "Тема оформления"),
        ["Settings.ThemeDark"] = ("Dark", "Тёмная"),
        ["Settings.ThemeLight"] = ("Light", "Светлая"),
        ["Settings.TimeFormat"] = ("Time Format", "Формат времени"),
        ["Settings.HudOpacity"] = ("HUD Overlay Opacity", "Прозрачность HUD оверлея"),
        ["Settings.Tabs"] = ("Navigation Tabs", "Вкладки навигации"),
        ["Settings.TabStorage"] = ("Tab Storage", "Хранилище вкладок"),
        ["Settings.ResetBtn"] = ("Reset Defaults", "Сбросить настройки"),

        // Status & Messages
        ["Status.Ready"] = ("Ready", "Готов к работе"),
        ["Status.Scanning"] = ("Scanning temporary junk files...", "Сканирование временных файлов..."),
        ["Status.Cleaned"] = ("Cleanup completed.", "Очистка завершена.")
    };

    public static string Get(string key)
    {
        if (Strings.TryGetValue(key, out var pair))
        {
            return IsRussian ? pair.RU : pair.EN;
        }
        return key;
    }

    public static string Get(string key, params object[] args)
    {
        string raw = Get(key);
        try
        {
            return string.Format(raw, args);
        }
        catch
        {
            return raw;
        }
    }
}
