@echo off
rem Adds a mod from our mods repository (github.com/marshmellion/LocalExile-Mods): lists them, you type one.
rem Or name it straight away: add-mod.cmd statecontrol
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-all.ps1" -Add "%~1"
pause
