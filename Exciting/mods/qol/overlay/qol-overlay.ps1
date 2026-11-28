# The qol overlay: buttons over the game and keybinds, for the qol mod. Started by the mod when the server starts (its
# setting startOverlay), or by hand: powershell -NoProfile -ExecutionPolicy Bypass -STA -File qol-overlay.ps1
# Keybinds are in qol-overlay-keys.json beside this file; the port is the mod's (settings.json, "port").
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$created = $false
$mutex = New-Object System.Threading.Mutex($true, 'LocalExileQolOverlay', [ref]$created)
if (-not $created) { exit 0 }   # one overlay at a time

$port = 6170
$settings = Join-Path (Split-Path -Parent $here) 'settings.json'
if (Test-Path $settings) {
    $m = [regex]::Match((Get-Content $settings -Raw), '"port"\s*:\s*(\d+)')
    if ($m.Success) { $port = [int]$m.Groups[1].Value }
}
$keys = New-Object 'System.Collections.Generic.Dictionary[string,string]'
$keyFile = Join-Path $here 'qol-overlay-keys.json'
if (Test-Path $keyFile) {
    $json = Get-Content $keyFile -Raw | ConvertFrom-Json
    foreach ($p in $json.PSObject.Properties) { if ($p.Value) { $keys[$p.Name] = [string]$p.Value } }
}

try {
    Add-Type -Path (Join-Path $here 'QolOverlay.cs') -ReferencedAssemblies System.Windows.Forms, System.Drawing
} catch {
    $log = Join-Path $here 'qol-overlay.log'
    "$(Get-Date -Format s) the overlay did not compile: $($_.Exception.Message)" | Out-File $log -Append
    throw
}
[QolOverlay]::Run($port, $keys)
