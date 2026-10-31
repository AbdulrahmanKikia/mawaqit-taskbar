@echo off
setlocal EnableExtensions
title Install Mawaqit Widget

:: Double-click this file to install. No programming knowledge needed.
set "PRODUCT=Mawaqit Widget"
set "SRC=%~dp0"
set "DEST=%LOCALAPPDATA%\%PRODUCT%"
set "EXE=%DEST%\%PRODUCT%.exe"
set "STARTMENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs\%PRODUCT%.lnk"

echo.
echo  Installing %PRODUCT%...
echo.

if not exist "%SRC%%PRODUCT%.exe" (
  echo  ERROR: %PRODUCT%.exe not found next to Install.bat
  echo  Download the portable zip from GitHub Releases and extract it first.
  echo.
  pause
  exit /b 1
)

:: Stop a previous instance
taskkill /F /IM "Mawaqit Widget.exe" >nul 2>&1
taskkill /F /IM widget.exe >nul 2>&1
timeout /t 1 /nobreak >nul

if exist "%DEST%" rmdir /S /Q "%DEST%" >nul 2>&1
mkdir "%DEST%" >nul 2>&1
robocopy "%SRC%." "%DEST%" /E /NFL /NDL /NJH /NJS /nc /ns /np >nul
if errorlevel 8 (
  echo  ERROR: failed to copy files to %DEST%
  pause
  exit /b 1
)

:: Start Menu shortcut
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s = New-Object -ComObject WScript.Shell; $l = $s.CreateShortcut('%STARTMENU%'); $l.TargetPath = '%EXE%'; $l.WorkingDirectory = '%DEST%'; $l.Description = 'Taskbar prayer times'; $l.IconLocation = '%EXE%,0'; $l.Save()"

:: Start with Windows
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "%PRODUCT%" /t REG_SZ /d "\"%EXE%\"" /f >nul

echo  Installed to:
echo    %DEST%
echo.
echo  Starting %PRODUCT%...
start "" "%EXE%"

echo.
echo  Done.
echo  Look for a chip on the taskbar (next to the clock).
echo  Right-click it → Change mosque… to choose your mosque.
echo  If nothing appears on the bar, use the tray icon.
echo.
pause
endlocal
