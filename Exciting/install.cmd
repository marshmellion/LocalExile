@echo off
rem The guided install of LocalExile with the Exciting build: download this one file and double-click it.
rem It fetches the step-by-step guide (install.ps1) from github.com/marshmellion/LocalExile and runs it.
if not exist "%~dp0install.ps1" goto fetch
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
goto end
:fetch
powershell -NoProfile -ExecutionPolicy Bypass -Command "$f = Join-Path $env:TEMP 'localexile-install.ps1'; [Net.ServicePointManager]::SecurityProtocol = 'Tls12'; Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/marshmellion/LocalExile/exciting/Exciting/install.ps1' -OutFile $f; & $f %*"
:end
pause
