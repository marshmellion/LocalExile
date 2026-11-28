# Updates everything to the newest: the Exciting build (LocalExile plus our changes and the mods that come with it, the
# same as update.cmd), then every other mod in Mods\Binaries that our mods repository has a newer version of
# (https://github.com/marshmellion/LocalExile-Mods, kept in Tools\LocalExile-Mods\). Your characters, accounts, keys and
# mod settings stay (they are in data\). The in-game buttons (NativeMods) stay as you set them.
#   add-mod.cmd (update-all.ps1 -Add <id>) adds a mod of that repository (it lists them when you give none).
param([string]$Add = '')
$ErrorActionPreference = 'Stop'
$kit = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $kit
Set-Location $repo
$modsUrl = 'https://github.com/marshmellion/LocalExile-Mods.git'

function Step([string]$text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Ok([string]$text) { Write-Host "   $text" -ForegroundColor Green }
function Note([string]$text) { Write-Host "   $text" }
function Fail([string]$text) { Write-Host ''; Write-Host $text -ForegroundColor Red; exit 1 }
function Version([string]$folder) {
    $file = Join-Path $folder 'mod.json'
    if (-not (Test-Path $file)) { return [version]'0.0.0' }
    try { return [version]((Get-Content $file -Raw | ConvertFrom-Json).version) } catch { return [version]'0.0.0' }
}

function GetRepository {
    $cache = 'Tools\LocalExile-Mods'
    if (Test-Path "$cache\.git") {
        & git -C $cache fetch -q --depth 1 origin main
        if ($LASTEXITCODE -eq 0) { & git -C $cache reset -q --hard FETCH_HEAD }
    }
    else {
        if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
        & git clone -q --depth 1 $modsUrl $cache
    }
    if ($LASTEXITCODE -ne 0) { Fail 'Getting our mods repository failed: read what git printed above.' }
    return $cache
}
# Where a repository mod goes: a project mod (a .csproj) as source in Mods\Source\<id>\, which the next start builds
# into Mods\Binaries\<id>\; a script mod straight into Mods\Binaries\<id>\.
function Target([string]$source, [string]$id) {
    if (Get-ChildItem $source -Filter '*.csproj' -ErrorAction SilentlyContinue) { return "Mods\Source\$id" }
    return "Mods\Binaries\$id"
}
function CopyMod([string]$source, [string]$to) {
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    # /MIR: a file the new version dropped goes too; saves\ (a mod's own records), bin\ and obj\ (builds) stay.
    & robocopy $source $to /MIR /XD saves bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "Copying the mod to $to failed (robocopy code $LASTEXITCODE)." }
}
$ours = @(Get-ChildItem "$kit\mods" -Directory | ForEach-Object { $_.Name })   # the build's own (setup installs them)

if ($PSBoundParameters.ContainsKey('Add')) {
    Step 'Adding a mod from our mods repository'
    $cache = GetRepository
    $all = @(Get-ChildItem "$cache\mods" -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'mod.json') } | ForEach-Object {
        $m = Get-Content (Join-Path $_.FullName 'mod.json') -Raw | ConvertFrom-Json
        [pscustomobject]@{ Id = $_.Name; Name = $m.name; Text = "$($m.description)" } })
    while (-not ($all | Where-Object { $_.Id -eq $Add })) {
        if ($Add) { Note "There is no mod '$Add' there." }
        foreach ($m in $all) { $t = if ($m.Text.Length -gt 90) { $m.Text.Substring(0, 87) + '...' } else { $m.Text }; Write-Host ("   {0,-24} {1}" -f $m.Id, $t) }
        $Add = (Read-Host '   >> Type the id of the mod to add (the first column), or press Enter to stop').Trim()
        if (-not $Add) { exit 0 }
    }
    if ($Add -in $ours) { Ok "$Add comes with this build: it is already installed"; exit 0 }
    $source = Join-Path $cache "mods\$Add"
    $to = Target $source $Add
    CopyMod $source $to
    Ok "added $Add ($(Version $source)) to $to"
    Note 'Restart the servers (or start the game) to load it; its settings are in Waypoint''s Mods panel, or in data\mods\.'
    exit 0
}

# 1. The build and its own mods: setup.ps1 -Update (git pull, then the mods, the client-made art, ...).
& (Join-Path $kit 'setup.ps1') -Update
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 2. Every other installed mod our mods repository has a newer version of.
Step 'Your other mods (from our mods repository)'
$cache = GetRepository
$installed = @(Get-ChildItem 'Mods\Binaries', 'Mods\Source' -Directory -ErrorAction SilentlyContinue | ForEach-Object { $_.Name } | Select-Object -Unique)
$updated = 0
foreach ($id in $installed) {
    if ($id -in $ours) { continue }
    $source = Join-Path $cache "mods\$id"
    if (-not (Test-Path (Join-Path $source 'mod.json'))) { continue }   # not one of the repository's mods
    $to = Target $source $id
    $have = [version[]]@((Version "Mods\Source\$id"), (Version "Mods\Binaries\$id")) | Sort-Object | Select-Object -Last 1
    $new = Version $source
    if ($new -le $have) { continue }
    CopyMod $source $to
    Ok "$id`: $have -> $new"
    $updated++
}
if ($updated -eq 0) { Note 'all up to date' }
Note "more mods: Exciting\add-mod.cmd lists and adds them (or Waypoint's Mods -> Browse)"

Write-Host ''
Write-Host 'Everything is up to date. Start the game with Exciting\play.cmd (or Waypoint).' -ForegroundColor Green
exit 0
