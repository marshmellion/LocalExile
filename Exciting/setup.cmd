@echo off
rem Sets up this LocalExile checkout to play the Exciting Items build (safe to run again).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" %*
pause
