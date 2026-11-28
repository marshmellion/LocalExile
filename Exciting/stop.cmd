@echo off
rem Stops this install's game and servers (the same as Waypoint's Stop all when Waypoint has this folder chosen).
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\dev\live.ps1" -Stop %*
pause
