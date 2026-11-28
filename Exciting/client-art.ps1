# Makes what our mods take from your own copy of the game (it is not ours to share, so it is not in the download):
# the new orbs' art (Exciting Items) and the Body Auras' effects. Needs Python 3 (and Pillow for the orbs).
#   setup.cmd runs it every time; play.cmd runs it with -IfMissing, which makes the Body Auras once data\client.db
#   exists (the first start makes it), so the second start has them.
param([switch]$IfMissing)

$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $repo
function Note([string]$text) { Write-Host "   $text" }
function Ok([string]$text) { Write-Host "   $text" -ForegroundColor Green }
# Windows PowerShell 5.1 turns a program's error output into a script-stopping error when it is redirected and
# $ErrorActionPreference is Stop: these run a program with its error output dropped (Quiet) or kept as text (WithErrors).
function Quiet([scriptblock]$Command) { $ErrorActionPreference = 'Continue'; & $Command 2>$null }
function WithErrors([scriptblock]$Command) { $ErrorActionPreference = 'Continue'; & $Command 2>&1 | ForEach-Object { "$_" } }

$orbs = 'Mods\Binaries\excitingitems\tools\make_orb_art.py'
$auras = 'Mods\Binaries\bodyauras\tools\make_auras.py'
$aurasDone = Test-Path 'Mods\Binaries\bodyauras\content\auras.json'
if ($IfMissing -and ((-not (Test-Path $auras)) -or $aurasDone -or -not (Test-Path 'data\client.db'))) { exit 0 }

# Python 3.9 or newer: the py launcher, else python (not the Microsoft Store's stand-in, which prints no version).
$python = $null
foreach ($try in @(@('py', '-3'), @('python'))) {
    if (-not (Get-Command $try[0] -ErrorAction SilentlyContinue)) { continue }
    $v = WithErrors { & $try[0] @($try | Select-Object -Skip 1) --version }
    if ("$v" -match '^Python 3\.(\d+)' -and [int]$Matches[1] -ge 9) { $python = $try; break }
}

Write-Host ''
Write-Host '== Made from your own client: the orbs'' art and the Body Auras' -ForegroundColor Cyan
if (-not $python) {
    Note 'skipped: needs Python 3 (https://www.python.org/downloads/windows/, tick "Add python.exe to PATH"), then run setup.cmd again.'
    Note 'Without it Body Auras has no auras and the new orbs keep simple art; everything else works.'
    exit 0
}
$py = $python[0]; $pyArgs = @($python | Select-Object -Skip 1)

if (-not $IfMissing -and (Test-Path $orbs)) {
    Quiet { & $py @pyArgs -c 'import PIL' }
    if ($LASTEXITCODE -ne 0) { Note 'the orbs keep simple art: Python''s Pillow is missing (get it with: py -m pip install pillow, then run setup.cmd again)' }
    else {
        & $py @pyArgs $orbs --client .
        if ($LASTEXITCODE -eq 0) { Ok 'the orbs'' art' } else { Note 'the orbs keep simple art (read what it printed above)' }
    }
}

if (Test-Path $auras) {
    if (-not (Test-Path 'data\client.db')) { Note 'Body Auras: made at your second start (the first start makes data\client.db, which it reads)' }
    else {
        Note 'Body Auras: reading about 750 effect files from your client (a minute or two) ...'
        & $py @pyArgs $auras --client .
        if ($LASTEXITCODE -eq 0) { Ok 'Body Auras'' effects (they go into the game as it starts)' } else { Note 'Body Auras failed: read what it printed above' }
    }
}
exit 0
