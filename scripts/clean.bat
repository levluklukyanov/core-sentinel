@echo off
:: CoreSpectre Standalone Deep Cleanup Script
:: Optional standalone batch utility (bundled as clean.bat in release zip packages)
:: Checks administrator privileges and skips NTFS junctions/reparse points cleanly.
net session >nul 2>&1
if %errorLevel% neq 0 (
    set "__CS_SELF=%~f0"
    powershell -NoProfile -Command "Start-Process -FilePath $env:__CS_SELF -Verb RunAs"
    exit /b
)

chcp 65001 > nul

echo ========================================================
echo        CoreSpectre: System Cleanup (Windows)
echo ========================================================
echo.

echo [0/6] Creating Windows System Restore Point...
powershell -NoProfile -NonInteractive -Command "Checkpoint-Computer -Description 'CoreSpectre Before clean.bat' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction SilentlyContinue" >nul 2>&1

echo [1/6] Cleaning Temporary folders safely (skipping ReparsePoints)...
powershell -NoProfile -NonInteractive -Command "function Clear-Safe($r){ if(-not(Test-Path -LiteralPath $r)){return}; Get-ChildItem -LiteralPath $r -Force -ErrorAction SilentlyContinue | ForEach-Object { if(($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $_.Name -eq '.net'){return}; if($_.PSIsContainer){ Clear-Safe $_.FullName; if(-not(Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue)){ Remove-Item -LiteralPath $_.FullName -Force -Recurse -Confirm:$false -ErrorAction SilentlyContinue } } else { Remove-Item -LiteralPath $_.FullName -Force -Confirm:$false -ErrorAction SilentlyContinue } } }; Clear-Safe $env:TEMP; Clear-Safe (Join-Path $env:SystemRoot 'Temp')" >nul 2>&1

echo [2/6] Cleaning Windows Update Download cache...
net stop wuauserv >nul 2>&1
net stop dosvc >nul 2>&1
powershell -NoProfile -NonInteractive -Command "function Clear-Safe($r){ if(-not(Test-Path -LiteralPath $r)){return}; Get-ChildItem -LiteralPath $r -Force -ErrorAction SilentlyContinue | ForEach-Object { if($_.Attributes -band [IO.FileAttributes]::ReparsePoint){return}; if($_.PSIsContainer){ Clear-Safe $_.FullName; if(-not(Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue)){ Remove-Item -LiteralPath $_.FullName -Force -Recurse -Confirm:$false -ErrorAction SilentlyContinue } } else { Remove-Item -LiteralPath $_.FullName -Force -Confirm:$false -ErrorAction SilentlyContinue } } }; Clear-Safe (Join-Path $env:SystemRoot 'SoftwareDistribution\Download'); Clear-Safe (Join-Path $env:SystemRoot 'SoftwareDistribution\DeliveryOptimization')" >nul 2>&1
net start wuauserv >nul 2>&1
net start dosvc >nul 2>&1

echo [3/6] Cleaning crash dumps and Windows Error Reporting archives...
powershell -NoProfile -NonInteractive -Command "function Clear-Safe($r){ if(-not(Test-Path -LiteralPath $r)){return}; Get-ChildItem -LiteralPath $r -Force -ErrorAction SilentlyContinue | ForEach-Object { if($_.Attributes -band [IO.FileAttributes]::ReparsePoint){return}; if($_.PSIsContainer){ Clear-Safe $_.FullName; if(-not(Get-ChildItem -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue)){ Remove-Item -LiteralPath $_.FullName -Force -Recurse -Confirm:$false -ErrorAction SilentlyContinue } } else { Remove-Item -LiteralPath $_.FullName -Force -Confirm:$false -ErrorAction SilentlyContinue } } }; Clear-Safe (Join-Path $env:LOCALAPPDATA 'CrashDumps'); Clear-Safe (Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\WER\ReportArchive'); Clear-Safe (Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\WER\ReportQueue'); Clear-Safe (Join-Path $env:ProgramData 'Microsoft\Windows\WER\ReportArchive'); Clear-Safe (Join-Path $env:ProgramData 'Microsoft\Windows\WER\ReportQueue')" >nul 2>&1

echo [4/6] Flushing DNS resolver cache...
ipconfig /flushdns >nul 2>&1

echo [5/6] Emptying Recycle Bin (skipped by default for safety)...
:: powershell -Command "Clear-RecycleBin -Confirm:$false -ErrorAction SilentlyContinue"

echo [6/6] Cleaning Windows Component Store (WinSxS)...
Dism.exe /online /Cleanup-Image /StartComponentCleanup

echo.
echo ========================================================
echo      Cleanup completed.
echo ========================================================
pause
