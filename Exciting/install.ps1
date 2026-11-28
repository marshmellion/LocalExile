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

# The client in $dir: $null if it is not a Path of Exile folder, else what it is.
function ClientInfo([string]$dir) {
    $exe = @('PathOfExileSteam.exe', 'PathOfExile.exe') | ForEach-Object { Join-Path $dir $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $exe) { return $null }
    if (-not ((Test-Path (Join-Path $dir 'Bundles2\_.index.bin')) -or (Test-Path (Join-Path $dir 'Content.ggpk')))) { return $null }
    $parent = Split-Path -Parent $dir
    [pscustomobject]@{
        Path    = $dir
        Is3d    = (Get-FileHash $exe).Hash -eq 'E9BA3D31C4D922E1826EF01D02FD22178A001B29E17264B68FC2446EFD41F53D'
        # LocalExile's content patcher changed it: copying it would carry another install's changes along.
        Patched = (Test-Path (Join-Path $dir 'Bundles2\LocalExile')) -or (Test-Path (Join-Path $dir 'Game.patched.exe'))
        # A folder another program owns: Steam's own install, or another LocalExile install's Client folder.
        Owned   = ($dir -match '\\steamapps\\common\\') -or (Test-Path (Join-Path $parent 'GameServer.slnx'))
    }
}
# Path of Exile folders on this PC: Steam's libraries, then every drive two folders deep (a few seconds).
function FindClients {
    $dirs = New-Object System.Collections.Generic.List[string]
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        $libs = @($steam)
        if (Test-Path $vdf) { $libs += [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"') | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' } }
        foreach ($lib in $libs | Select-Object -Unique) { $dirs.Add((Join-Path $lib 'steamapps\common\Path of Exile')) }
    }
    foreach ($drive in Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Free -ne $null }) {
        $root = $drive.Root
        $dirs.Add($root)
        Get-ChildItem $root -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin 'Windows', '$Recycle.Bin', 'System Volume Information' } | ForEach-Object {
            $dirs.Add($_.FullName)
            Get-ChildItem $_.FullName -Directory -ErrorAction SilentlyContinue | ForEach-Object { $dirs.Add($_.FullName) }
        }
    }
    $seen = @{}
    foreach ($d in $dirs) {
        $key = $d.TrimEnd('\').ToLowerInvariant()
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $info = ClientInfo $d
        if ($info) { $info }
    }
}
# The install's folder when starting past step 2: asks until it is one.
function AskInstallFolder {
    while ($true) {
        $f = (Read-Host '  >> The folder you installed LocalExile (the Exciting build) in').Trim('"', ' ')
        if (Test-Path (Join-Path $f 'Exciting\setup.ps1')) { return $f }
        Warn "$f has no Exciting\setup.ps1: that is not the Exciting build (start at step 2 to get it)."
    }
}

Clear-Host
Title 'LocalExile: the Exciting build, guided install'
Say 'This puts a Path of Exile server on your own PC (LocalExile) with our mods, and walks you through each step.'
Say 'You need:'
Say '  - Windows 10 or 11 and about 60 GB free on the drive you install to (the game client is about 45 GB).'
Say '  - A Steam account that owns Path of Exile (it is free): the right game version is downloaded from it for you.'
Say '  - About an hour, most of it downloading.'
Say 'Nothing is installed without you clicking it yourself.'
Say ''
Say 'The steps:'
Say '  1  the programs LocalExile needs (Git, .NET, Node, and Python if you like)'
Say '  2  download LocalExile, the Exciting build (or turn a LocalExile you already have into it)'
Say '  3  the game client 3.29.3d (download it, or use one you already have)'
Say '  4  set up (login keys, patched client, the mods, your login account)'
Say '  5  Waypoint, the launcher (optional)'
Say '  6  play'
Say 'Steps that are already done are noticed and skipped by themselves. Done some of it before? Start at its step.'
$start = (Read-Host '  >> Press Enter to start at step 1, or type a step number (1-6)').Trim()
$start = if ($start -match '^[1-6]$') { [int]$start } else { 1 }

# ---------------------------------------------------------------- 1. programs
# The required programs are always checked (quick, and the later steps need them); it only stops if one is missing.
Title 'Step 1 of 6: the programs LocalExile needs'
Say 'A missing one opens its download page. Install it with the normal choices, then come back here and press Enter.'
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
$hasPython = [bool](FindPython)
if ($start -le 1) { $hasPython = Need 'Python 3 (optional, for Body Auras and the new orbs'' art)' { [bool](FindPython) } 'https://www.python.org/downloads/windows/' @(
    'Python makes the Body Auras and the new orbs'' pictures from your game files. Everything else works without it.',
    'Click "Download Python 3...", run it, TICK "Add python.exe to PATH" at the bottom of the first page, then "Install Now".') -Optional }
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
if ($start -le 2) {
    Title 'Step 2 of 6: downloading LocalExile (the Exciting build)'
    if (-not $Folder) {
        Say 'Choose a folder for it on a drive with space, for example D:\LocalExile (a new folder; no spaces is safest).'
        Say 'Already have LocalExile from Git (Waypoint''s "Source code" install)? Type its folder: it can be turned into'
        Say 'the Exciting build, keeping your characters and its client.'
        $Folder = Read-Host '  >> Folder (Enter for C:\LocalExile)'
        if (-not $Folder) { $Folder = 'C:\LocalExile' }
    }
    $Folder = $Folder.Trim('"', ' ')
    if (Test-Path (Join-Path $Folder 'Exciting\setup.ps1')) {
        Good "already downloaded in $Folder"
        Push-Location $Folder
        $current = (& git rev-parse --abbrev-ref HEAD 2>$null)
        if ($current -notin 'exciting', 'exciting-testing') { Warn "That folder is on the branch '$current': update.cmd follows '$current'." }
        Pop-Location
    }
    elseif ((Test-Path (Join-Path $Folder 'GameServer.slnx')) -and (Test-Path (Join-Path $Folder '.git'))) {
        Say "$Folder is a LocalExile install. Turning it into the Exciting build adds our copy of LocalExile to it and"
        Say "switches it to the branch $branch; your characters, accounts and client stay. (Back to normal LocalExile later:"
        Say 'git switch main in that folder.)'
        if (-not (Ask 'Turn it into the Exciting build?')) { Wait 'Press Enter to close'; exit 1 }
        Push-Location $Folder
        try {
            if (@(& git status --porcelain --untracked-files=no).Count -gt 0) { Warn 'It has changed files (git status lists them): put them back first, then run this again.'; Wait 'Press Enter to close'; exit 1 }
            if (-not (& git remote | Where-Object { $_ -eq 'exciting' })) { & git remote add exciting $repoUrl }
            & git fetch exciting $branch
            if ($LASTEXITCODE -eq 0) { & git switch -c $branch --track "exciting/$branch" 2>$null; if ($LASTEXITCODE -ne 0) { & git switch $branch } }
            if ($LASTEXITCODE -ne 0) { Warn 'Switching failed: read what git printed above.'; Wait 'Press Enter to close'; exit 1 }
        }
        finally { Pop-Location }
        Good "$Folder is now the Exciting build"
    }
    else {
        if ((Test-Path $Folder) -and @(Get-ChildItem $Folder -Force).Count -gt 0) { Warn "$Folder is not empty and is not LocalExile: choose a new folder and run this again."; Wait 'Press Enter to close'; exit 1 }
        Say "Downloading into $Folder (a minute or two) ..."
        & git clone -b $branch $repoUrl $Folder
        if ($LASTEXITCODE -ne 0) { Warn 'The download failed: check your internet connection and run this again.'; Wait 'Press Enter to close'; exit 1 }
        Good "downloaded into $Folder"
    }
}
elseif (-not $Folder -or -not (Test-Path (Join-Path $Folder 'Exciting\setup.ps1'))) { $Folder = AskInstallFolder }

# ---------------------------------------------------------------- 3. the game client
$client = ''
$installClient = Join-Path $Folder 'Client'
$downloading = Test-Path (Join-Path $installClient '.DepotDownloader')
$finished = Test-Path (Join-Path $installClient '.complete-3.29.3d')
if ($start -le 3) {
    Title 'Step 3 of 6: the game client (Path of Exile 3.29.3d)'
    Say 'LocalExile runs the 3.29.3d version of the game (your normal Steam install is newer).'
    if ((Test-Path (Join-Path $installClient 'Bundles2\_.index.bin')) -and ($finished -or -not $downloading)) { Good 'the client is already in the install' }
    else {
        $choice = ''
        if (-not $downloading) {
            Say 'Looking for a 3.29.3d client already on this PC (a few seconds) ...'
            $found = @(FindClients | Where-Object { $_.Path.TrimEnd('\') -ne $installClient.TrimEnd('\') })
            $usable = @($found | Where-Object { $_.Is3d -and -not $_.Patched })
            foreach ($f in $found | Where-Object { -not $_.Is3d }) { Say "  (found $($f.Path): another version, not usable)" }
            foreach ($f in $found | Where-Object { $_.Is3d -and $_.Patched }) { Say "  (found $($f.Path): 3.29.3d, but another LocalExile changed it; not usable)" }
            Say ''
            for ($i = 0; $i -lt $usable.Count; $i++) { Say "  $($i + 1)  use $($usable[$i].Path)" }
            Say '  D  download it from Steam (about 45 GB)'
            Say '  F  use another folder (type its path next)'
            $choice = (Read-Host "  >> Your choice$(if ($usable.Count) { ' (a number, D or F)' } else { ' (D or F)' })").Trim()
        }
        $source = $null
        if ($choice -match '^\d+$' -and [int]$choice -ge 1 -and [int]$choice -le $usable.Count) { $source = $usable[[int]$choice - 1] }
        elseif ($choice -match '^[fF]') {
            while (-not $source) {
                $p = (Read-Host '  >> The client folder (where PathOfExileSteam.exe is; or D to download instead)').Trim('"', ' ')
                if ($p -match '^[dD]$') { break }
                $info = ClientInfo $p
                if (-not $info) { Warn "$p has no PathOfExileSteam.exe with its Bundles2 folder." }
                elseif ($info.Patched) { Warn 'Another LocalExile changed that client: run its content restore first, or download a fresh one.' }
                else {
                    if (-not $info.Is3d) { Warn 'That is not 3.29.3d: LocalExile may still run it if it is 3.29.3c, but the in-game panels need 3.29.3d.' }
                    $source = $info
                }
            }
        }
        if ($source) {
            $sameDrive = (Split-Path -Qualifier $source.Path) -eq (Split-Path -Qualifier ([IO.Path]::GetFullPath($Folder)))
            $move = $false
            if (-not $source.Owned -and $sameDrive) {
                Say "Move it into the install (instant, no extra space; $($source.Path) is then gone), or copy it (keeps it,"
                $move = (Read-Host '  >> needs about 45 GB more and a few minutes)? Type M to move or C to copy').Trim() -match '^[mM]'
            }
            elseif ($source.Owned) { Say 'It belongs to Steam or another LocalExile install, so it is copied (never moved).' }
            if ($move) {
                if ((Test-Path $installClient) -and @(Get-ChildItem $installClient -Force).Count -eq 0) { Remove-Item $installClient -Force }
                if (Test-Path $installClient) { Warn "$installClient already has files: copying instead."; $client = $source.Path }
                else { Move-Item $source.Path $installClient; Good "moved into $installClient" }
            }
            else { $client = $source.Path; Good "setup copies it from $client next" }
        }
        else {
            if ($downloading) { Say 'An earlier download stopped before the end: it carries on now.' }
            Say 'It downloads from your own Steam account with a small tool (DepotDownloader) it fetches for you.'
            Say 'Not in your Steam library yet? On the page that comes up, click "Play Game" (it is free), then come back.'
            Open 'https://store.steampowered.com/app/238960/Path_of_Exile/'
            Wait 'Press Enter to start (it asks for your Steam account name next)'
            & (Join-Path $Folder 'Exciting\get-client.ps1') -Out $installClient
            if ($LASTEXITCODE -ne 0) { Warn 'The client download did not finish: run install.cmd again, it carries on where it stopped.'; Wait 'Press Enter to close'; exit 1 }
        }
    }
}

# ---------------------------------------------------------------- 4. setup
if ($start -le 4) {
Title 'Step 4 of 6: setting up (login keys, the patched client, the mods)'
Say 'This patches the install''s copy of the client so it talks to your own server, installs the mods,'
Say 'and asks you for a login name and password for your server (any name you like; it stays on your PC).'
Wait 'Press Enter to start'
$setupArgs = @{}
if ($client) { $setupArgs.ClientSource = $client }
& (Join-Path $Folder 'Exciting\setup.ps1') @setupArgs
if ($LASTEXITCODE -ne 0) { Warn 'Setup stopped: read the red line above, fix that, and run install.cmd again.'; Wait 'Press Enter to close'; exit 1 }
}

# ---------------------------------------------------------------- 5. Waypoint
if ($start -le 5) {
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
