@echo off
rem Switches back to the released build (branch exciting). Your characters, accounts and settings stay.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" -Branch exciting %*
pause
