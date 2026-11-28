# The guided install of LocalExile with the Exciting build (our fork) and its mods, step by step.
# Download install.cmd and double-click it, or run this file. It tells you what to do at each step, opens the pages
# you need, and waits for you. Run it again any time: steps that are already done are skipped.
# It never installs anything without asking, and changes nothing outside the folder you choose.
param([string]$Folder = '', [switch]$Testing)

$ErrorActionPreference = 'Stop'
$repoUrl = 'https://github.com/marshmellion/LocalExile.git'
$branch = if ($Testing) { 'exciting-testing' } else { 'exciting' }
$modsUrl = 'https://github.com/marshmellion/LocalExile-Mods'

function Title([string]$text) { Write-Host ''; Write-Host ('=' * 70) -ForegroundColor DarkCyan; Write-Host " $text" -ForegroundColor Cyan; Write-Host ('=' * 70) -ForegroundColor DarkCyan }
function Say([string]$text) { Write-Host "  $text" }
function Good([string]$text) { Write-Host "  OK: $text" -ForegroundColor Green }
function Warn([string]$text) { Write-Host "  $text" -ForegroundColor Yellow }
function Wait([string]$text = 'Press Enter when you have done that') { Write-Host ''; [void](Read-Host "  >> $text") }
function Ask([string]$question) { $a = Read-Host "  >> $question (y/n)"; return $a -match '^\s*y' }
function Open([string]$url) { Say "Opening $url"; Start-Process $url }
function Has([string]$command) { return [bool](Get-Command $command -ErrorAction SilentlyContinue) }
# A program installed while this window is open is not on its PATH yet: read PATH again from Windows.
function RefreshPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machine;$user"
}
function HasDotnet10 { return (Has 'dotnet') -and @(& dotnet --list-sdks 2>$null | Where-Object { $_ -match '^10\.' }).Count -gt 0 }
function FindPython {
    foreach ($try in @(@('py', '-3'), @('python'))) {
        if (-not (Has $try[0])) { continue }
        $v = & $try[0] @($try | Select-Object -Skip 1) --version 2>&1
        if ("$v" -match '^Python 3\.(\d+)' -and [int]$Matches[1] -ge 9) { return ,$try }
    }
    return $null
}
# Waits until $check passes, sending the player to $url with $how; Skip leaves it (only for optional programs).
function Need([string]$name, [scriptblock]$check, [string]$url, [string[]]$how, [switch]$Optional) {
    while (-not (& $check)) {
        Warn "$name is not installed yet."
        foreach ($line in $how) { Say $line }
        Open $url
        if ($Optional) {
            $a = Read-Host '  >> Press Enter when it is installed, or type skip to go on without it'
            if ($a -match '^\s*skip') { return $false }
        }
        else { Wait 'Press Enter when it is installed' }
        RefreshPath
    }
    Good "$name"
    return $true
}

Clear-Host
Title 'LocalExile: the Exciting build, guided install'
Say 'This puts a Path of Exile server on your own PC (LocalExile) with our mods, and walks you through each step.'
Say 'You need:'
Say '  - Windows 10 or 11 and about 130 GB free (the game client is about 70 GB, and you need a second copy of it).'
Say '  - Path of Exile on Steam (the game is downloaded from your own Steam account), or a 3.29.3d client folder.'
Say '  - About an hour, most of it downloading.'
Say 'Nothing is installed without you clicking it yourself.'
Wait 'Press Enter to begin'

# ---------------------------------------------------------------- 1. programs
Title 'Step 1 of 6: the programs LocalExile needs'
Say 'Each one opens its download page. Install it with the normal choices, then come back here and press Enter.'
Say ''
[void](Need 'Git' { Has 'git' } 'https://git-scm.com/download/win' @(
        'Git downloads the server and keeps it up to date.',
        'Click "Click here to download", run the installer, and click Next through it (the normal choices are right).'))
[void](Need '.NET 10 SDK' { HasDotnet10 } 'https://dotnet.microsoft.com/download/dotnet/10.0' @(
        '.NET builds and runs the servers.',
        'Under "SDK 10.0...", click the Windows "x64" link of the Installers row, run it, and click Install.'))
[void](Need 'Node.js' { Has 'node' } 'https://nodejs.org/en/download' @(
        'Node unpacks the game files.',
        'Download the LTS "Windows Installer (.msi)", run it, and click Next through it.'))
$python = $null
$hasPython = Need 'Python 3 (optional, for Body Auras and the new orbs'' art)' { [bool](FindPython) } 'https://www.python.org/downloads/windows/' @(
    'Python makes the Body Auras and the new orbs'' pictures from your game files. Everything else works without it.',
    'Click "Download Python 3...", run it, TICK "Add python.exe to PATH" at the bottom of the first page, then "Install Now".') -Optional
if ($hasPython) {
    $python = FindPython
    $py = $python[0]; $pyArgs = @($python | Select-Object -Skip 1)
    & $py @pyArgs -c 'import PIL' 2>$null
    if ($LASTEXITCODE -ne 0) {
        Say 'The new orbs'' pictures also need Pillow, a small Python add-on.'
        if (Ask 'Install Pillow now (py -m pip install pillow)?') { & $py @pyArgs -m pip install --user pillow }
    }
}

# ---------------------------------------------------------------- 2. the server
Title 'Step 2 of 6: downloading LocalExile (the Exciting build)'
if (-not $Folder) {
    Say 'Choose a folder for it on a drive with space, for example D:\LocalExile (a new folder, no spaces is safest).'
    $Folder = Read-Host '  >> Folder (Enter for C:\LocalExile)'
    if (-not $Folder) { $Folder = 'C:\LocalExile' }
}
$Folder = $Folder.Trim('"', ' ')
if (Test-Path (Join-Path $Folder 'Exciting\setup.ps1')) {
    Good "already downloaded in $Folder"
    Push-Location $Folder
    $current = (& git rev-parse --abbrev-ref HEAD 2>$null)
    if ($current -ne $branch) { Warn "That folder is on the branch '$current', not '$branch': the steps below still work, but update.cmd follows '$current'." }
    Pop-Location
}
else {
    if ((Test-Path $Folder) -and @(Get-ChildItem $Folder -Force).Count -gt 0) { Warn "$Folder is not empty: choose a new folder and run this again."; Wait 'Press Enter to close'; exit 1 }
    Say "Downloading into $Folder (a minute or two) ..."
    & git clone -b $branch $repoUrl $Folder
    if ($LASTEXITCODE -ne 0) { Warn 'The download failed: check your internet connection and run this again.'; Wait 'Press Enter to close'; exit 1 }
    Good "downloaded into $Folder"
}

# ---------------------------------------------------------------- 3. the game client
Title 'Step 3 of 6: the game client (Path of Exile 3.29.3d)'
Say 'LocalExile runs the 3.29.3d version of the game. Your normal Steam install is newer, so you get a copy of 3.29.3d'
Say 'from your own Steam account with a small tool called DepotDownloader. (If you already have a 3.29.3d folder, skip this.)'
$client = ''
if (Test-Path (Join-Path $Folder 'Client\Bundles2\_.index.bin')) { Good 'the client is already copied into the install' }
elseif (Ask 'Do you already have a 3.29.3d client folder?') { $client = (Read-Host '  >> Its folder').Trim('"', ' ') }
else {
    Say ''
    Say '1. Download DepotDownloader: on the page that opens, under "Assets", click DepotDownloader-windows-x64.zip.'
    Open 'https://github.com/SteamRE/DepotDownloader/releases/latest'
    Say '2. Right-click the zip in your Downloads, choose "Extract All...", and extract it.'
    Wait 'Press Enter when it is extracted'
    $dd = ''
    $guess = Get-ChildItem (Join-Path $env:USERPROFILE 'Downloads') -Recurse -Filter 'DepotDownloader.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($guess -and (Ask "Use $($guess.FullName)?")) { $dd = $guess.FullName }
    while (-not $dd -or -not (Test-Path $dd)) { $dd = (Read-Host '  >> Full path of DepotDownloader.exe (you can drag the file into this window)').Trim('"', ' ') }
    $client = (Read-Host '  >> A new folder to download the client into (for example D:\PoE 3.29.3d)').Trim('"', ' ')
    Say ''
    Say '3. It now signs in to Steam with YOUR account: type your Steam account name, then your Steam password and'
    Say '   Steam Guard code when DepotDownloader asks (they go to Steam only). Then it downloads about 45 GB.'
    Say '   If it stops, run install.cmd again: it carries on where it stopped.'
    Wait 'Press Enter to start the download'
    & (Join-Path $Folder 'Exciting\get-client.ps1') -DepotDownloader $dd -Out $client
    if ($LASTEXITCODE -ne 0) { Warn 'The client download did not finish: run install.cmd again to carry on.'; Wait 'Press Enter to close'; exit 1 }
}

# ---------------------------------------------------------------- 4. setup
Title 'Step 4 of 6: setting up (copies the client, makes your login keys, installs the mods)'
Say 'This copies the client into the install (a few minutes), patches the copy so it talks to your own server,'
Say 'and asks you for a login name and password for your server (any name you like; it stays on your PC).'
Wait 'Press Enter to start'
$setupArgs = @{}
if ($client) { $setupArgs.ClientSource = $client }
& (Join-Path $Folder 'Exciting\setup.ps1') @setupArgs
if ($LASTEXITCODE -ne 0) { Warn 'Setup stopped: read the red line above, fix that, and run install.cmd again.'; Wait 'Press Enter to close'; exit 1 }

# ---------------------------------------------------------------- 5. Waypoint
Title 'Step 5 of 6 (optional): Waypoint, the launcher with a mods panel'
Say 'You can play with Exciting\play.cmd alone. Waypoint is a launcher with buttons for start/stop, settings for every'
Say 'mod and a mod browser. If you want it:'
Say '  a. Install the .NET 10 Desktop Runtime (the "Desktop Runtime" x64 installer on the .NET page).'
Say '  b. Download Waypoint (the newest release, the .zip or setup .exe under Assets) and start it.'
Say "  c. In Waypoint choose ""Add a folder you already have..."" and pick $Folder."
Say '  d. To browse our mods there too: Mods, then Browse, then the three dots menu, "Add repository", and paste'
Say "     $modsUrl"
Say '     (Waypoint warns that it is not the official list: that is expected.)'
if (Ask 'Open the Waypoint and .NET Desktop Runtime pages now?') {
    Open 'https://dotnet.microsoft.com/download/dotnet/10.0'
    Open 'https://github.com/LocalExile/Waypoint/releases/latest'
}

# ---------------------------------------------------------------- 6. play
Title 'Step 6 of 6: play'
Say "Start the game with $Folder\Exciting\play.cmd (or Waypoint's Start)."
Say 'The FIRST start builds the servers and reads the game data: it takes a few minutes before the game opens.'
if ($hasPython) { Say 'The Body Auras are made at the second start (they read data the first start makes).' }
Say ''
Say "To update later: $Folder\Exciting\update.cmd. More mods: $modsUrl"
Say "Help and settings: $Folder\Exciting\README.md"
if (Ask 'Start the game now?') { Start-Process (Join-Path $Folder 'Exciting\play.cmd') }
Write-Host ''
Write-Host '  All done. Have fun!' -ForegroundColor Green
Wait 'Press Enter to close'
