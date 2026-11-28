@echo off
rem Starts the servers and the game (the same as LocalExile's own scripts\dev\live.ps1).
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\dev\live.ps1" %*
pause
