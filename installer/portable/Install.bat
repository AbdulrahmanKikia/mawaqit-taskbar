@echo off
setlocal EnableExtensions
title Install Mawaqit Widget

:: Double-click this file to install. No Rust / PowerShell knowledge needed.
set "PRODUCT=Mawaqit Widget"
set "SRC=%~dp0"
set "DEST=%LOCALAPPDATA%\%PRODUCT%"
set "EXE=%DEST%\%PRODUCT%.exe"
set "STARTMENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs\%PRODUCT%.lnk"

echo.
echo  Installing %PRODUCT%...
echo.

if not exist "%SRC%widget.exe" (
  echo  ERROR: widget.exe not found next to Install.bat
  echo  Download the portable zip from GitHub Releases and extract it first.
  echo.
  pause
  exit /b 1
)

:: Stop a previous instance
taskkill /F /IM "Mawaqit Widget.exe" >nul 2>&1
taskkill /F /IM widget.exe >nul 2>&1
timeout /t 1 /nobreak >nul

mkdir "%DEST%" >nul 2>&1
copy /Y "%SRC%widget.exe" "%EXE%" >nul
if exist "%SRC%icon.ico" copy /Y "%SRC%icon.ico" "%DEST%\icon.ico" >nul

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
echo  Look for a strip above the taskbar (left side).
echo  Right-click it → Change mosque… to choose your mosque.
echo.
pause
endlocal
