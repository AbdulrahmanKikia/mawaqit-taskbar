@echo off
setlocal EnableExtensions
title Uninstall Mawaqit Widget

set "PRODUCT=Mawaqit Widget"
set "DEST=%LOCALAPPDATA%\%PRODUCT%"
set "STARTMENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs\%PRODUCT%.lnk"

echo.
echo  Uninstalling %PRODUCT%...
echo.

taskkill /F /IM "Mawaqit Widget.exe" >nul 2>&1
taskkill /F /IM widget.exe >nul 2>&1
timeout /t 1 /nobreak >nul

reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "%PRODUCT%" /f >nul 2>&1
if exist "%STARTMENU%" del /F /Q "%STARTMENU%" >nul 2>&1
if exist "%DEST%" rmdir /S /Q "%DEST%" >nul 2>&1

echo  Removed the app and startup entry.
echo  Your mosque settings (if any) are still in:
echo    %APPDATA%\Mawaqit\%PRODUCT%\
echo  Delete that folder too if you want a full reset.
echo.
pause
endlocal
