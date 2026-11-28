@echo off
rem Gets the newest Exciting Items build (git pull), then installs its mods and NativeMods again.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" -Update %*
pause
