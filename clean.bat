@echo off
:: Проверка прав администратора. Если их нет — запрашиваем автоматически.
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

chcp 65001 > nul

echo [1/6] Очистка временных папок Temp...
del /s /f /q %temp%\* 2>nul
del /s /f /q C:\Windows\Temp\* 2>nul

echo [2/6] Очистка кэша и оптимизации обновлений Windows...
:: Очищаем обычный кэш обновлений
del /s /f /q C:\Windows\SoftwareDistribution\Download\* 2>nul
:: Очищаем кэш доставки обновлений (Peer-to-Peer)
del /s /f /q C:\Windows\SoftwareDistribution\DeliveryOptimization\* 2>nul

echo [3/6] Очистка отчетов об ошибках и системных логов...
del /s /f /q %PROGRAMDATA%\Microsoft\Windows\WER\ReportArchive\* 2>nul
del /s /f /q %PROGRAMDATA%\Microsoft\Windows\WER\ReportQueue\* 2>nul
del /f /q C:\Windows\*.log 2>nul
del /s /f /q C:\Windows\Logs\CBS\* 2>nul

echo [4/6] Сброс сетевого DNS-кэша...
ipconfig /flushdns > nul

echo [5/6] Очистка Корзины со всех дисков...
powershell -NoProfile -Command "Clear-RecycleBin -Confirm:$false -ErrorAction SilentlyContinue"

echo [6/6] Глубокое сжатие и очистка базы обновлений (DISM)...
echo Это может занять пару минут, подожди...
dism /online /cleanup-image /startcomponentcleanup /resetbase >nul

echo --------------------------------------------------
echo Очистка успешно завершена! Система дышит свободнее.
timeout /t 5 > nul
exit