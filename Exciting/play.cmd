@echo off
rem Starts the servers and the game (the same as LocalExile's own scripts\dev\live.ps1).
cd /d "%~dp0.."
rem Makes the Body Auras from your client once the first start has made the game data (does nothing after that).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0client-art.ps1" -IfMissing
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\dev\live.ps1" %*
pause
