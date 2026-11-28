# Downloads the Path of Exile 3.29.3d client (game build 328228) from Steam with DepotDownloader, straight into this
# install's Client\ folder (or the folder you give). Path of Exile must be in your Steam library (it is free: "Play Game"
# on https://store.steampowered.com/app/238960 adds it).
# DepotDownloader (https://github.com/SteamRE/DepotDownloader, GPL 2) is fetched for you into Tools\DepotDownloader\ if
# you have none. It signs in with your own Steam account: it asks for your password and Steam Guard code itself, and
# they go to Steam only. About 45 GB. Run it again to carry on after a stop: finished files are kept.
param([string]$DepotDownloader = '', [string]$Out = '')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is many times slower with its progress bar
$kit = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $kit

function Say([string]$text) { Write-Host "   $text" }
function Fail([string]$text) { Write-Host ''; Write-Host $text -ForegroundColor Red; exit 1 }

# ---- DepotDownloader: yours, the one fetched before, or fetch it now (pinned release, checked by its SHA-256).
$ddVersion = '3.4.0'
$ddUrl = "https://github.com/SteamRE/DepotDownloader/releases/download/DepotDownloader_$ddVersion/DepotDownloader-windows-x64.zip"
$ddHash = '41C9E9F0DF54B3AD02E67A11726756E5C73283BD7C2E1B04ACFA5AE4C2ED3767'
$ddHere = Join-Path $repo 'Tools\DepotDownloader\DepotDownloader.exe'
if (-not $DepotDownloader) { $DepotDownloader = $ddHere }
$DepotDownloader = $DepotDownloader.Trim('"', ' ')
if (-not (Test-Path $DepotDownloader)) {
    if ($DepotDownloader -ne $ddHere) { Fail "$DepotDownloader not found." }
    Write-Host "== Getting DepotDownloader $ddVersion (about 35 MB) from github.com/SteamRE/DepotDownloader" -ForegroundColor Cyan
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $zip = Join-Path $env:TEMP "DepotDownloader-$ddVersion.zip"
    Invoke-WebRequest -UseBasicParsing $ddUrl -OutFile $zip
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $ddHash) { Remove-Item $zip -Force; Fail 'The DepotDownloader download is not the expected file: try again later.' }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ddHere) | Out-Null
    Expand-Archive $zip (Split-Path -Parent $ddHere) -Force
    Remove-Item $zip -Force
    if (-not (Test-Path $ddHere)) { Fail 'Unpacking DepotDownloader failed.' }
    Say "ready: $ddHere"
}

# ---- Where to: this install's Client\ unless you say otherwise (setup.cmd then needs no copy).
if (-not $Out) {
    $default = Join-Path $repo 'Client'
    $answer = Read-Host "   Download into $default ? Press Enter for yes, or type another (new, empty) folder"
    $Out = if ($answer) { $answer } else { $default }
}
$Out = $Out.Trim('"', ' ')
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$drive = (Get-Item $Out).PSDrive
if ($drive -and $drive.Free -ne $null -and $drive.Free -lt 50GB -and -not (Test-Path (Join-Path $Out 'Bundles2'))) {
    Write-Host "   Only $([math]::Round($drive.Free / 1GB)) GB free on $($drive.Name): the client needs about 50 GB." -ForegroundColor Yellow
    if ((Read-Host '   Carry on anyway? (y/n)') -notmatch '^\s*y') { exit 1 }
}

Write-Host ''
Say 'DepotDownloader now signs in to Steam with YOUR account (the account that owns Path of Exile):'
Say '  - type your Steam ACCOUNT NAME here (the login name, not your email or profile name);'
Say '  - then DepotDownloader asks for your password, and for your Steam Guard code or to approve in the Steam app.'
Say 'Your password goes to Steam only, and DepotDownloader remembers the sign-in so the second part does not ask again.'
$user = (Read-Host '   Steam account name').Trim()
if (-not $user) { exit 1 }

Write-Host '== 1 of 2: the Windows executables (depot 238962, small)' -ForegroundColor Cyan
& $DepotDownloader -app 238960 -depot 238962 -manifest 8687196073893351989 -username $user -remember-password -dir $Out -os windows
if ($LASTEXITCODE -ne 0) { Fail 'The download failed (a wrong password or code?): run this again, it carries on where it stopped.' }
Write-Host '== 2 of 2: the game files (depot 238961, about 45 GB: this takes a while)' -ForegroundColor Cyan
& $DepotDownloader -app 238960 -depot 238961 -manifest 5379264335337066375 -username $user -remember-password -dir $Out -max-downloads 8
if ($LASTEXITCODE -ne 0) { Fail 'The download failed: run this again, it carries on where it stopped.' }

Say 'Checking that it is exactly 3.29.3d ...'
$exe = (Get-FileHash (Join-Path $Out 'PathOfExileSteam.exe')).Hash -eq 'E9BA3D31C4D922E1826EF01D02FD22178A001B29E17264B68FC2446EFD41F53D'
$index = (Get-FileHash (Join-Path $Out 'Bundles2\_.index.bin')).Hash -eq '53560CC6391D2B11276F480CE5EBE86EB75A26935064C846AA99499F376BAC29'
if ($exe -and $index) {
    Set-Content (Join-Path $Out '.complete-3.29.3d') (Get-Date -Format s)   # install.ps1: the download finished
    Write-Host "Done: $Out is the 3.29.3d client." -ForegroundColor Green; exit 0
}
Fail "The files in $Out are not exactly 3.29.3d (executable: $exe, index: $index): run this again to repair them."
