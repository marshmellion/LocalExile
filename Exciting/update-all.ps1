# Updates everything to the newest: the Exciting build (LocalExile plus our changes and the mods that come with it, the
# same as update.cmd), then every other mod in Mods\Binaries that our mods repository has a newer version of
# (https://github.com/marshmellion/LocalExile-Mods, kept in Tools\LocalExile-Mods\). Your characters, accounts, keys and
# mod settings stay (they are in data\). The in-game buttons (NativeMods) stay as you set them.
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

# 1. The build and its own mods: setup.ps1 -Update (git pull, then the mods, the client-made art, ...).
$setupArgs = @{ Update = $true }
& (Join-Path $kit 'setup.ps1') @setupArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 2. Every other installed mod our mods repository has a newer version of.
Step 'Your other mods (from our mods repository)'
$cache = 'Tools\LocalExile-Mods'
if (Test-Path "$cache\.git") {
    & git -C $cache fetch -q --depth 1 origin main
    if ($LASTEXITCODE -eq 0) { & git -C $cache reset -q --hard FETCH_HEAD }
}
else {
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force }
    & git clone -q --depth 1 $modsUrl $cache
}
if ($LASTEXITCODE -ne 0) { Fail 'Getting our mods repository failed: read what git printed above (the build itself is updated).' }

$ours = @(Get-ChildItem "$kit\mods" -Directory | ForEach-Object { $_.Name })   # the build's own: already updated above
$updated = 0
foreach ($mod in Get-ChildItem 'Mods\Binaries' -Directory -ErrorAction SilentlyContinue) {
    if ($mod.Name -in $ours) { continue }
    $source = Join-Path $cache "mods\$($mod.Name)"
    if (-not (Test-Path (Join-Path $source 'mod.json'))) { continue }   # not one of the repository's mods
    $have = Version $mod.FullName
    $new = Version $source
    if ($new -le $have) { continue }
    # /MIR: a file the new version dropped goes too; saves\ (a mod's own records) stays.
    & robocopy $source $mod.FullName /MIR /XD saves /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "Updating the mod $($mod.Name) failed (robocopy code $LASTEXITCODE)." }
    Ok "$($mod.Name): $have -> $new"
    $updated++
}
if ($updated -eq 0) { Note 'all up to date' }
Note "more mods to add: $($modsUrl -replace '\.git$', '') (or Waypoint's Mods -> Browse)"

Write-Host ''
Write-Host 'Everything is up to date. Start the game with Exciting\play.cmd (or Waypoint).' -ForegroundColor Green
exit 0
