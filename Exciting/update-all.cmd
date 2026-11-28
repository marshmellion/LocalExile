@echo off
rem Updates everything to the newest: this build (LocalExile and its mods, like update.cmd) and every other mod you
rem installed from our mods repository. Your characters, accounts and settings stay.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-all.ps1" %*
pause
