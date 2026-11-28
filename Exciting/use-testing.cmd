@echo off
rem Switches to the testing build (branch exciting-testing): changes being tried before they are released.
rem Back to the released build: use-release.cmd. Your characters, accounts and settings stay.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" -Branch exciting-testing %*
pause
