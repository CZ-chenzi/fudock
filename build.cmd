@echo off
rem Build Fudock.exe using the .NET Framework 4.8 compiler bundled with Windows.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)
echo.
pause
