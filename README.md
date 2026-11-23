# CoreSentinel

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0078D6)](https://github.com/levluklukyanov/core-sentinel)
[![Framework](https://img.shields.io/badge/.NET-10.0%20WPF-512BD4)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/C%23-13.0-239120)](https://docs.microsoft.com/dotnet/csharp/)
[![Release](https://img.shields.io/badge/Release-v1.0.0-success)](https://github.com/levluklukyanov/core-sentinel/releases)

Высокопроизводительный системный монитор, оптимизатор оперативной памяти/процессора и игровой центр для **Windows 10/11 (x64)**.

---

## ⚡ Основные возможности

1. **Аппаратная телеметрия в реальном времени**:
   - Постоянный опрос нагрузки CPU, распределения RAM и накопителей (WMI + Win32 Native API).
   - Интерактивный журнал событий с отметками времени и горизонтальной прокруткой.

2. **Оптимизация ОЗУ и ЦП**:
   - Сброс системного кэша и принудительное высвобождение рабочих наборов (Working Set).
   - Балансировка очередей выполнения процессора и понижение фоновых задач.

3. **Game Sentinel**:
   - Автоматическое распознавание игровых процессов и приоритизация в реальном времени.
   - Сканирование установленных игр с фильтрацией лаунчеров Minecraft.

4. **Менеджер хранилища**:
   - Анализ разделов накопителей и пакетная очистка временных файлов (Temp, Delivery Optimization, DISM).

5. **Плавающий HUD**:
   - Компактный полупрозрачный оверлей для мониторинга поверх полноэкранных игр и приложений.

6. **Кастомизация и эргономика**:
   - Светлая и тёмная темы оформления (инженерный минимализм).
   - Хранилище вкладок (Tab Vault) с возможностью скрывать и переупорядочивать разделы.
   - 5 встроенных шрифтов (Golos, Inter, JetBrains Mono, Manrope, Onest).
   - Автоматическое сохранение конфигурации пользователя между сессиями.

---

## 📦 Быстрый старт

1. Перейдите в раздел **[Releases](https://github.com/levluklukyanov/core-sentinel/releases)**.
2. Скачайте автономный файл `CoreSentinel.exe`.
3. Запустите программу.

> **Почему размер файла около 65 МБ?**  
> Приложение собрано в режиме `Self-Contained Single-File`. Весь рантайм .NET 10, зависимости и шрифты зашиты прямо внутрь бинарника. Программа работает «из коробки» на любой чистой Windows 10/11 без необходимости устанавливать дополнительные пакеты .NET.

---

## 🛠️ Сборка из исходников

Требования:
- Windows 10/11 x64
- [.NET SDK 10.0](https://dotnet.microsoft.com/)

```bash
# Клонирование репозитория
git clone https://github.com/levluklukyanov/core-sentinel.git
cd core-sentinel

# Сборка решения
dotnet build CoreSentinel.sln -c Release

# Публикация автономного монолитного бинарника
dotnet publish CoreSentinel.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```
