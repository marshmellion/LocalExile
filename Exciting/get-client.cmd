@echo off
rem Downloads the Path of Exile 3.29.3d client from Steam with DepotDownloader (you need to own the game on Steam).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0get-client.ps1" %*
pause
