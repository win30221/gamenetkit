@echo off
setlocal
set "TD_SOLO_SCRIPT=%~dp0Solo\Play-Solo.ps1"
if exist "%TD_SOLO_SCRIPT%" goto launch
set "TD_SOLO_SCRIPT=%~dp0..\Builds\Solo\Solo\Play-Solo.ps1"
if exist "%TD_SOLO_SCRIPT%" goto launch
echo Build the optional test bundle first: pwsh -File "%~dp0Build.ps1"
pause
exit /b 1
:launch
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%TD_SOLO_SCRIPT%" %*
if not errorlevel 1 exit /b 0
echo Tower Duel could not start. Check the error above.
pause
exit /b 1
