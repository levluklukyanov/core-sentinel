@echo off
:: CoreSentinel Standalone Deep Cleanup Script
:: Проверка прав администратора. Если их нет — запрашиваем автоматически.
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

chcp 65001 > nul

echo ========================================================
echo        CoreSentinel: Системная очистка Windows
echo ========================================================
echo.

echo [0/6] Создание точки восстановления Windows для безопасного отката...
powershell -NoProfile -Command "Checkpoint-Computer -Description 'CoreSentinel Before clean.bat' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction SilentlyContinue" >nul 2>&1

echo [1/6] Очистка временных папок Temp...
del /s /f /q "%TEMP%\*" 2>nul
del /s /f /q "%SystemRoot%\Temp\*" 2>nul

echo [2/6] Очистка кэша и оптимизации обновлений Windows...
del /s /f /q "%SystemRoot%\SoftwareDistribution\Download\*" 2>nul
del /s /f /q "%SystemRoot%\SoftwareDistribution\DeliveryOptimization\*" 2>nul

echo [3/6] Очистка отчетов об ошибках и системных логов...
del /s /f /q "%PROGRAMDATA%\Microsoft\Windows\WER\ReportArchive\*" 2>nul
del /s /f /q "%PROGRAMDATA%\Microsoft\Windows\WER\ReportQueue\*" 2>nul
del /f /q "%SystemRoot%\*.log" 2>nul
del /s /f /q "%SystemRoot%\Logs\CBS\*" 2>nul

echo [4/6] Сброс сетевого DNS-кэша...
ipconfig /flushdns > nul

echo [5/6] Очистка Корзины со всех накопителей...
powershell -NoProfile -Command "Clear-RecycleBin -Confirm:$false -ErrorAction SilentlyContinue"

echo [6/6] Очистка хранилища компонентов Windows (DISM)...
echo Это может занять несколько минут, пожалуйста, подождите...
dism /online /cleanup-image /startcomponentcleanup >nul

echo.
echo --------------------------------------------------------
echo Очистка успешно завершена! Система освобождена от мусора.
echo ВНИМАНИЕ: Для отката любых изменений системы запустите:
echo   rstrui.exe (Мастер восстановления системы Windows)
echo --------------------------------------------------------
timeout /t 5 > nul
exit
