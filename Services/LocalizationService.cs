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
    public static IReadOnlyCollection<string> Keys => Strings.Keys;

    private static readonly Dictionary<string, (string EN, string RU)> Strings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Navigation
        ["Nav.Navigation"] = ("NAVIGATION", "НАВИГАЦИЯ"),

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
        ["Tip.ToggleHud"] = ("Toggle compact floating HUD overlay", "Открыть компактный плавающий оверлей HUD"),
        ["Tip.CpuAction"] = ("Switch to CPU Optimization tab", "Перейти на вкладку Оптимизации ЦП"),
        ["Tip.RamAction"] = ("Switch to Memory Purge tab", "Перейти на вкладку Очистки ОЗУ"),

        // Storage Tab
        ["Storage.Subtitle"] = ("Monitor drive capacity and safely remove temporary cache files", "Контроль свободного места накопителей и безопасное удаление временных файлов"),
        ["Storage.Targets"] = ("SAFE CLEANUP TARGETS", "ОБЛАСТИ БЕЗОПАСНОЙ ОЧИСТКИ"),
        ["Storage.ScanBtn"] = ("Scan Cache", "Сканировать кэш"),
        ["Storage.CleanBtn"] = ("Clean System Cache", "Очистить систему"),
        ["Storage.RollbackBtn"] = ("Rollback", "Откат системы"),
        ["Storage.ConsoleTitle"] = ("ACTIVITY & CLEANUP CONSOLE", "КОНСОЛЬ АКТИВНОСТИ И ОЧИСТКИ"),

        // Games Tab
        ["Games.Title"] = ("Game Mode & Catalog", "Игровой модуль и каталог игр"),
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

        // Settings Tab
        ["Settings.Title"] = ("Settings", "Настройки"),
        ["Settings.Subtitle"] = ("System configuration, customization and hardware specifications", "Конфигурация системы, кастомизация и информация об оборудовании"),
        ["Settings.Customization"] = ("Customization", "Кастомизация"),
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
        ["Status.Ready"] = ("Ready", "Готов к работе")
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
