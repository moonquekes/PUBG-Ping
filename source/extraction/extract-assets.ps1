param(
    [string]$GameRoot = 'D:\SteamLibrary\steamapps\common\PUBG',
    [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
$bundle = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$cache = if ($env:PUBG_ASSET_CACHE) { $env:PUBG_ASSET_CACHE } else { Join-Path $bundle 'data\extraction-cache' }
$paks = Join-Path $GameRoot 'TslGame\Content\Paks'
if (-not (Test-Path -LiteralPath $paks)) { throw 'PUBG Paks directory was not found.' }
$pythonExe = (Get-Command $Python -ErrorAction Stop).Source
& $pythonExe -c 'import PIL, cryptography'
if ($LASTEXITCODE -ne 0) { throw 'Python packages Pillow and cryptography are required. See README.md.' }
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$oodle = Join-Path $cache 'oodle.zip'
$vgm = Join-Path $cache 'vgmstream.zip'
if (-not (Test-Path -LiteralPath $oodle)) {
    Invoke-WebRequest -Uri 'https://github.com/WorkingRobot/OodleUE/releases/download/2026-06-04-1357/clang-cl-x64-release.zip' -OutFile $oodle
}
if (-not (Test-Path -LiteralPath $vgm)) {
    Invoke-WebRequest -Uri 'https://github.com/vgmstream/vgmstream/releases/download/r2117/vgmstream-win64.zip' -OutFile $vgm
}
$oldCache = $env:PUBG_ASSET_CACHE
$oldPaks = $env:PUBG_PAK_DIR
$oldEncoding = $env:PYTHONIOENCODING
try {
    $env:PUBG_ASSET_CACHE = $cache
    $env:PUBG_PAK_DIR = $paks
    $env:PYTHONIOENCODING = 'utf-8'
    foreach ($script in @('derive_key.py', 'extract_tables.py', 'pubg_uasset.py', 'export_pubg_textures.py', 'export_pubg_sounds.py', 'export_pubg_catalog.py')) {
        & $pythonExe (Join-Path $PSScriptRoot $script)
        if ($LASTEXITCODE -ne 0) { throw "Extraction failed at $script; game files have not been modified." }
    }
} finally {
    $env:PUBG_ASSET_CACHE = $oldCache
    $env:PUBG_PAK_DIR = $oldPaks
    $env:PYTHONIOENCODING = $oldEncoding
}
Write-Host 'Done. Only local bundle assets/cache were written. No game process was accessed.'
