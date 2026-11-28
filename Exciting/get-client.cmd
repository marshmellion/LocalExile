@echo off
rem Downloads the Path of Exile 3.29.3d client from your own Steam account into Client\ (fetches DepotDownloader for you).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0get-client.ps1" %*
pause
