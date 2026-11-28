# Downloads the Path of Exile 3.29.3d client (game build 328228) from Steam with DepotDownloader into a new folder.
# DepotDownloader (https://github.com/SteamRE/DepotDownloader/releases) signs in with your own Steam account: it asks
# for your password and Steam Guard code itself, in its own window. About 40-50 GB. Run it again to carry on after a stop.
param([string]$DepotDownloader = '', [string]$Out = '')
$ErrorActionPreference = 'Stop'
if (-not $DepotDownloader) { $DepotDownloader = Read-Host 'Full path of DepotDownloader.exe' }
$DepotDownloader = $DepotDownloader.Trim('"', ' ')
if (-not (Test-Path $DepotDownloader)) { Write-Host "$DepotDownloader not found." -ForegroundColor Red; exit 1 }
if (-not $Out) { $Out = Read-Host 'Folder to download into (a new, empty folder, for example D:\PoE 3.29.3d)' }
$Out = $Out.Trim('"', ' ')
$user = Read-Host 'Your Steam account name (not your email or display name)'
if (-not $user) { exit 1 }

Write-Host '== 1 of 2: the Windows executables (depot 238962)' -ForegroundColor Cyan
& $DepotDownloader -app 238960 -depot 238962 -manifest 8687196073893351989 -username $user -remember-password -dir $Out -os windows
if ($LASTEXITCODE -ne 0) { Write-Host 'The download failed: run this again, it carries on where it stopped.' -ForegroundColor Red; exit 1 }
Write-Host '== 2 of 2: the game files (depot 238961, the big one)' -ForegroundColor Cyan
& $DepotDownloader -app 238960 -depot 238961 -manifest 5379264335337066375 -username $user -remember-password -dir $Out -max-downloads 8
if ($LASTEXITCODE -ne 0) { Write-Host 'The download failed: run this again, it carries on where it stopped.' -ForegroundColor Red; exit 1 }

$exe = (Get-FileHash (Join-Path $Out 'PathOfExileSteam.exe')).Hash -eq 'E9BA3D31C4D922E1826EF01D02FD22178A001B29E17264B68FC2446EFD41F53D'
$index = (Get-FileHash (Join-Path $Out 'Bundles2\_.index.bin')).Hash -eq '53560CC6391D2B11276F480CE5EBE86EB75A26935064C846AA99499F376BAC29'
if ($exe -and $index) { Write-Host "Done: $Out is the 3.29.3d client. Give this folder to Exciting\setup.cmd." -ForegroundColor Green }
else { Write-Host "The files in $Out are not exactly 3.29.3d (executable: $exe, index: $index)." -ForegroundColor Red; exit 1 }
