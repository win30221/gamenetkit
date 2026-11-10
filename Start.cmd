@echo off
setlocal
set "RF_LAUNCH_PWSH=pwsh.exe"
where pwsh.exe >nul 2>nul
if not errorlevel 1 goto launch
set "RF_LAUNCH_PWSH=%ProgramFiles%\PowerShell\7\pwsh.exe"
if exist "%RF_LAUNCH_PWSH%" goto launch
set "RF_LAUNCH_PWSH=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe"
if exist "%RF_LAUNCH_PWSH%" goto launch
echo PowerShell 7 was not found. Install PowerShell 7 and open Start.cmd again.
pause
exit /b 1
:launch
"%RF_LAUNCH_PWSH%" -NoLogo -NoProfile -NoExit -File "%~dp0examples\Run.ps1"
