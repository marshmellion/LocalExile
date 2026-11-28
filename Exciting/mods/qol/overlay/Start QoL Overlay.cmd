@echo off
rem Starts the qol overlay by hand (the qol mod also starts it with the server).
start "" powershell -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%~dp0qol-overlay.ps1"
