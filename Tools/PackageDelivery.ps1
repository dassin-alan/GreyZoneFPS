<#
    GreyZone - package the playable prototype into a delivery zip.
    Usage: powershell -ExecutionPolicy Bypass -File Tools\PackageDelivery.ps1 [-Name GreyZone_M0_PlayablePrototype]
    Output: D:\Downloads\<Name>.zip + <Name>.zip.sha256
    Excludes: compile-check reference assemblies and all build output (out/) directories.
    (No destructive file operations are used: staging happens in a fresh temp folder and an
     existing zip is never overwritten - a timestamp suffix is used instead.)
#>
[CmdletBinding()]
param(
    [string]$Dest = 'D:\Downloads',
    [string]$Name = 'GreyZone_M0_PlayablePrototype'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot   # this script lives directly in Tools\
$stage = Join-Path $env:TEMP ('gz_pkg_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null
New-Item -ItemType Directory -Force -Path $Dest | Out-Null

$excludePrefixes = @('tools\compilecheck\refs', 'tools\compilecheck\out', 'tools\coretests\out')

function Copy-Tree([string]$relative) {
    $src = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $src)) { return }
    Get-ChildItem -LiteralPath $src -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($root.Length).TrimStart('\')
        $lower = $rel.ToLowerInvariant()
        foreach ($ex in $excludePrefixes) { if ($lower.StartsWith($ex)) { return } }
        $dst = Join-Path $stage $rel
        $dir = Split-Path -Parent $dst
        if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        Copy-Item -LiteralPath $_.FullName -Destination $dst -Force
    }
}

foreach ($tree in @('Assets', 'Packages', 'ProjectSettings', 'docs', 'Tools\CompileCheck', 'Tools\CoreTests')) {
    Copy-Tree $tree
}
foreach ($f in @('README.md', '.gitignore')) {
    $p = Join-Path $root $f
    if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination (Join-Path $stage $f) -Force }
}

$zip = Join-Path $Dest ($Name + '.zip')
if (Test-Path -LiteralPath $zip) {
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $zip = Join-Path $Dest ($Name + '_' + $stamp + '.zip')
}

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal -Force

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
Set-Content -LiteralPath ($zip + '.sha256') -Value ("{0}  {1}" -f $hash, (Split-Path -Leaf $zip)) -Encoding ASCII

Write-Host ""
Write-Host ("zip    : {0}" -f $zip)
Write-Host ("size   : {0:N1} KB" -f ((Get-Item -LiteralPath $zip).Length / 1KB))
Write-Host ("sha256 : {0}" -f $hash)
