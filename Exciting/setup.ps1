# Exciting Items build of LocalExile: sets up (or updates) this checkout to play.
#   setup.cmd            first time, and whenever you like: checks what you need, copies your client, makes your
#                        login keys and patched client, installs the mods (NativeMods only with -NativeMods)
#   update.cmd           pulls the newest build first (git pull), then does the same
#   use-testing.cmd      switches to the testing build (branch exciting-testing: what is being tried before a release)
#   use-release.cmd      switches back to the released build (branch exciting)
# The mods' client content (new items' art and text) goes into Client\ when the game starts (scripts\dev\live.ps1).
# Safe to run again: each step does nothing when it is already done. Nothing outside this folder is changed.
param([switch]$Update, [string]$ClientSource = '', [switch]$NativeMods, [ValidateSet('', 'exciting', 'exciting-testing')][string]$Branch = '')

$ErrorActionPreference = 'Stop'
$kit = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $kit
Set-Location $repo

function Step([string]$text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Ok([string]$text) { Write-Host "   $text" -ForegroundColor Green }
function Note([string]$text) { Write-Host "   $text" }
function Fail([string]$text) { Write-Host ''; Write-Host $text -ForegroundColor Red; exit 1 }
function Has([string]$command) { return [bool](Get-Command $command -ErrorAction SilentlyContinue) }

# The 3.29.3d client NativeMods is made for (PathOfExileSteam.exe and PathOfExile.exe are the same file).
$build3293d = 'E9BA3D31C4D922E1826EF01D02FD22178A001B29E17264B68FC2446EFD41F53D'

Step 'Checking that the game and the servers are closed'
if (Get-Process -Name 'Game.patched' -ErrorAction SilentlyContinue) { Fail 'The game is running: close it, then run this again.' }
if (Get-Process -Name 'GameServer.Gateway', 'GameServer.Auth', 'GameServer.Patch' -ErrorAction SilentlyContinue) { Fail 'The servers are running: stop them (close their windows or Waypoint''s Stop), then run this again.' }
Ok 'closed'

Step 'Checking the programs you need'
if (-not (Has 'git')) { Fail 'Git is missing: install it from https://git-scm.com/download/win, then run this again.' }
if (-not (Has 'dotnet')) { Fail '.NET is missing: install the .NET 10 SDK from https://dotnet.microsoft.com/download, then run this again.' }
$sdks = @(& dotnet --list-sdks 2>$null | Where-Object { $_ -match '^10\.' })
if ($sdks.Count -eq 0) { Fail 'The .NET 10 SDK is missing (dotnet --list-sdks shows no 10.x): install it from https://dotnet.microsoft.com/download.' }
if (-not (Has 'node')) { Fail 'Node.js is missing: install it from https://nodejs.org (the LTS version), then run this again.' }
Ok "git, .NET $(($sdks[-1] -split ' ')[0]), Node $(& node --version)"
if (-not ((Has 'py') -or (Has 'python'))) { Note 'Python 3 not found (optional: Body Auras and the new orbs'' art need it): https://www.python.org/downloads/windows/' }

if ($Branch) { $Update = $true }
if ($Update) {
    Step 'Getting the newest build'
    $dirty = @(& git status --porcelain --untracked-files=no)
    if ($dirty.Count -gt 0) {
        Note 'You changed these files of the checkout:'
        $dirty | ForEach-Object { Note "  $_" }
        Fail 'Put them back first (git checkout -- <file>, or git stash), or ask for help: the update would mix them with the new build.'
    }
    if ($Branch) {
        & git fetch origin $Branch
        if ($LASTEXITCODE -ne 0) { Fail "Getting the branch $Branch failed: read what git printed above." }
        & git switch $Branch 2>$null
        if ($LASTEXITCODE -ne 0) { & git switch -c $Branch --track "origin/$Branch" }
        if ($LASTEXITCODE -ne 0) { Fail "Switching to $Branch failed: read what git printed above. Your checkout is unchanged." }
        Ok "now on $(if ($Branch -eq 'exciting') { 'the released build' } else { 'the testing build' }) ($Branch)"
    }
    & git pull --ff-only
    if ($LASTEXITCODE -ne 0) { Fail 'git pull failed: read what git printed above. Your checkout is unchanged.' }
    Ok "now at $(& git log -1 --format='%h, %cd' --date=short)"
}

Step 'The decompressor for the game files (Tools\ooz)'
if (-not (Test-Path 'Tools\ooz\node_modules\ooz-wasm')) {
    Push-Location 'Tools\ooz'
    try { & npm install --no-audit --no-fund } finally { Pop-Location }
    if (-not (Test-Path 'Tools\ooz\node_modules\ooz-wasm')) { Fail 'npm install in Tools\ooz failed: read what it printed above.' }
}
Ok 'ready'

Step 'Your game client (Client\)'
$steam = Test-Path 'Client\Bundles2\_.index.bin'
$standalone = Test-Path 'Client\Content.ggpk'
if (-not $steam -and -not $standalone) {
    $source = $ClientSource
    if (-not $source) {
        Note 'This needs a copy of your Path of Exile 3.29.3d client (Steam layout: PathOfExileSteam.exe and a Bundles2 folder).'
        Note 'README.md says how to get one. Your own install is only read, never changed.'
        $source = Read-Host '   Folder of the client to copy'
    }
    $source = $source.Trim('"', ' ')
    if (-not (Test-Path (Join-Path $source 'Bundles2\_.index.bin')) -and -not (Test-Path (Join-Path $source 'Content.ggpk'))) {
        Fail "$source holds neither Bundles2\_.index.bin (Steam) nor Content.ggpk (standalone): that is not a Path of Exile folder."
    }
    Note 'Copying it (a few minutes for about 70 GB) ...'
    & robocopy $source 'Client' /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "Copying the client failed (robocopy code $LASTEXITCODE)." }
    $steam = Test-Path 'Client\Bundles2\_.index.bin'
}
$exe = if (Test-Path 'Client\PathOfExileSteam.exe') { 'Client\PathOfExileSteam.exe' } elseif (Test-Path 'Client\PathOfExile.exe') { 'Client\PathOfExile.exe' } else { '' }
if (-not $exe) { Fail 'Client\ has no PathOfExileSteam.exe or PathOfExile.exe.' }
$copyExe = -not (Test-Path 'Client\Game.exe')
if (-not $copyExe) { $copyExe = (Get-FileHash $exe).Hash -ne (Get-FileHash 'Client\Game.exe').Hash }
if ($copyExe) { Copy-Item $exe 'Client\Game.exe' -Force; Note "Client\Game.exe is now a copy of $(Split-Path -Leaf $exe)" }
$is3293d = (Get-FileHash 'Client\Game.exe').Hash -eq $build3293d
if ($is3293d) { Ok "3.29.3d$(if ($steam) { ' (Steam layout)' } else { ' (standalone)' })" } else { Note 'not 3.29.3d: LocalExile may still run it (it says so at the next step), but NativeMods and the in-game panels need 3.29.3d' }

Step 'Your login keys and the patched client'
if (-not (Test-Path 'data\login-keys.json')) {
    & dotnet run --project src\GameServer.Auth -c Release -- keygen Client\Game.exe
    if ($LASTEXITCODE -ne 0) { Fail 'Making the login keys failed: read what it printed above.' }
}
$patch = -not (Test-Path 'Client\Game.patched.exe')
if (-not $patch) { $patch = (Get-Item 'Client\Game.exe').LastWriteTimeUtc -gt (Get-Item 'Client\Game.patched.exe').LastWriteTimeUtc }
if ($patch) {
    & dotnet run --project src\GameServer.Auth -c Release -- patch Client\Game.exe Client\Game.patched.exe
    if ($LASTEXITCODE -ne 0) { Fail 'Patching the client failed: read what it printed above.' }
}
Ok 'Client\Game.patched.exe trusts your keys (the item symbol hook and the mods'' content are added when the game starts)'

Step 'NativeMods (optional: the QoL panels inside the game)'
if (-not $NativeMods) { Note 'not installed (setup.cmd -NativeMods installs it; it is experimental on Windows)'; if (Test-Path 'Client\version.dll') { Move-Item 'Client\version.dll' 'Client\version.dll.off' -Force; Note 'turned an earlier NativeMods off (Client\version.dll.off)' } }
elseif (-not $is3293d) { Note 'skipped: NativeMods is made for the 3.29.3d client' }
else {
    $hostDir = 'Client\nativemods-host'
    New-Item -ItemType Directory -Force -Path $hostDir, "$hostDir\addresses" | Out-Null
    Copy-Item "$kit\nativemods\version.dll", "$kit\nativemods\LocalExile.ClientHost.dll" 'Client\' -Force
    Copy-Item "$kit\nativemods\host\*.dll" $hostDir -Force
    Copy-Item "$kit\nativemods\host\addresses\*.json" "$hostDir\addresses" -Force
    # NativeMods runs on .NET inside the game: a private copy of your .NET 10 runtime goes beside it (nothing is installed).
    $runtimes = @(& dotnet --list-runtimes | ForEach-Object {
        if ($_ -match '^Microsoft\.NETCore\.App (10\.\d+\.\d+) \[(.+)\]$') {
            [pscustomobject]@{ Version = [version]$Matches[1]; Text = $Matches[1]; Shared = $Matches[2] } } } | Sort-Object Version)
    if ($runtimes.Count -eq 0) { Fail 'No .NET 10 runtime found (dotnet --list-runtimes): install the .NET 10 SDK.' }
    $rt = $runtimes[-1]
    $runtimeDir = Join-Path $rt.Shared $rt.Text
    $fxr = Join-Path (Split-Path -Parent (Split-Path -Parent $rt.Shared)) "host\fxr\$($rt.Text)\hostfxr.dll"
    if (-not (Test-Path $fxr)) { Fail "$fxr is missing: repair your .NET 10 install." }
    & robocopy $runtimeDir $hostDir /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "Copying the .NET runtime failed (robocopy code $LASTEXITCODE)." }
    Copy-Item $fxr $hostDir -Force
    $config = @"
{
  "runtimeOptions": {
    "tfm": "net10.0",
    "includedFrameworks": [ { "name": "Microsoft.NETCore.App", "version": "$($rt.Text)" } ],
    "configProperties": {
      "System.Globalization.Invariant": true,
      "System.Reflection.Metadata.MetadataUpdater.IsSupported": false
    }
  }
}
"@
    [IO.File]::WriteAllText((Join-Path $repo "$hostDir\LocalExile.Client.Host.runtimeconfig.json"), $config)
    Ok "installed (on .NET $($rt.Text)); its log: Client\nativemods-host\nativemods-shim.log"
}

Step 'The mods'
New-Item -ItemType Directory -Force -Path 'Mods\Binaries', 'Mods\Client' | Out-Null
foreach ($mod in Get-ChildItem "$kit\mods" -Directory) {
    # /MIR: a file the new version dropped goes too; saves\ (a mod's own records) stays, and so does what Body Auras
    # made from your client (its content\ and lists).
    $keep = if ($mod.Name -eq 'bodyauras') { @('/XD', 'saves', 'content', '/XF', 'auras.index.json', 'cosmeticdrops.json') } else { @('/XD', 'saves') }
    & robocopy $mod.FullName "Mods\Binaries\$($mod.Name)" /MIR @keep /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "Copying the mod $($mod.Name) failed (robocopy code $LASTEXITCODE)." }
    Note "server mod: $($mod.Name)"
}
if ($is3293d -and $NativeMods) {
    foreach ($mod in Get-ChildItem "$kit\client-mods" -Directory) {
        $to = "Mods\Client\$($mod.Name)"
        $keep = Test-Path "$to\settings.json"
        if ($keep) { & robocopy $mod.FullName $to /E /XD source /XF settings.json SOURCE.md /NFL /NDL /NJH /NJS /NP | Out-Null }
        else { & robocopy $mod.FullName $to /E /XD source /XF SOURCE.md /NFL /NDL /NJH /NJS /NP | Out-Null }
        if ($LASTEXITCODE -ge 8) { Fail "Copying the client mod $($mod.Name) failed (robocopy code $LASTEXITCODE)." }
        Note "in-game panels: $($mod.Name)$(if ($keep) { ' (your settings.json kept)' })"
    }
}
Ok 'their settings are kept in data\mods\ (Waypoint''s Mods panel edits them)'

# The orbs' art and Body Auras' effects are made from your own client (client-art.ps1).
& (Join-Path $kit 'client-art.ps1')

Step 'Your login account'
if (-not (Test-Path 'data\auth.db')) {
    $name = Read-Host '   Make a login account now? Type a name (3-32 letters, digits, _ or -), or press Enter to skip'
    if ($name) {
        $secure = Read-Host '   Password (at least 4 characters)' -AsSecureString
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
        $plain | & dotnet run --project src\GameServer.Auth -c Release -- register $name
        $plain = $null
    }
    else { Note 'skipped: README.md says how to make one later' }
}
else { Ok 'you have one (data\auth.db)' }

Write-Host ''
Write-Host 'Done. Start the game with Exciting\play.cmd (or Waypoint). The first start builds the servers and the' -ForegroundColor Green
Write-Host 'game data (a few minutes), then puts the mods'' content into the game.' -ForegroundColor Green

exit 0
