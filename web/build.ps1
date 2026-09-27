<#
    GreyZone demo builder: inlines sim.js / gl.js / game.js into a single portable HTML file.
    Usage: powershell -ExecutionPolicy Bypass -File web\build.ps1
    Output: GreyZone_Demo.html (project root) and a copy in D:\Downloads
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$web = $PSScriptRoot
$root = Split-Path -Parent $web

$template = Get-Content -LiteralPath (Join-Path $web 'template.html') -Raw -Encoding UTF8
$sim = Get-Content -LiteralPath (Join-Path $web 'src\sim.js') -Raw -Encoding UTF8
$gl = Get-Content -LiteralPath (Join-Path $web 'src\gl.js') -Raw -Encoding UTF8
$game = Get-Content -LiteralPath (Join-Path $web 'src\game.js') -Raw -Encoding UTF8

$out = $template.Replace('/*__SIM__*/', "`n" + $sim + "`n").Replace('/*__GL__*/', "`n" + $gl + "`n").Replace('/*__GAME__*/', "`n" + $game + "`n")

$target = Join-Path $root 'GreyZone_Demo.html'
[System.IO.File]::WriteAllText($target, $out, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("wrote {0} ({1:N1} KB)" -f $target, ((Get-Item -LiteralPath $target).Length / 1KB))

$destDir = 'D:\Downloads'
if (Test-Path -LiteralPath $destDir) {
    $dest = Join-Path $destDir 'GreyZone_Demo.html'
    Copy-Item -LiteralPath $target -Destination $dest -Force
    Write-Host ("copy  {0} ({1:N1} KB)" -f $dest, ((Get-Item -LiteralPath $dest).Length / 1KB))
}
