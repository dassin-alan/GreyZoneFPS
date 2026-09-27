<#
    GreyZone Core test runner
    -------------------------
    Compiles Assets/GreyZone/Scripts/Core/*.cs + Tools/CoreTests/Program.cs into a console
    executable and runs it. Exit code 0 = all checks passed.

    Usage:
        powershell -ExecutionPolicy Bypass -File Tools\CoreTests\run_tests.ps1
#>
[CmdletBinding()]
param([string]$ProjectRoot = '')

$ErrorActionPreference = 'Continue'

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}
$testsDir = $PSScriptRoot
$outDir = Join-Path $testsDir 'out'
$exe = Join-Path $outDir 'core_tests.exe'
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

function Find-Csc {
    $known = @(
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
    )
    foreach ($k in $known) { if (Test-Path -LiteralPath $k) { return $k } }
    foreach ($root in @("$env:ProgramFiles\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio")) {
        if (Test-Path -LiteralPath $root) {
            $hit = Get-ChildItem -LiteralPath $root -Recurse -Filter 'csc.exe' -ErrorAction SilentlyContinue |
                   Where-Object { $_.FullName -like '*Roslyn*' } | Select-Object -First 1
            if ($hit) { return $hit.FullName }
        }
    }
    $fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (Test-Path -LiteralPath $fw) { return $fw }
    return $null
}

$csc = Find-Csc
if (-not $csc) { Write-Host 'FAIL: no C# compiler found.' -ForegroundColor Red; exit 1 }

$coreDir = Join-Path $ProjectRoot 'Assets\GreyZone\Scripts\Core'
$sources = @()
if (Test-Path -LiteralPath $coreDir) {
    $sources += @(Get-ChildItem -LiteralPath $coreDir -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
}
$sources += (Join-Path $testsDir 'Program.cs')
if ($sources.Count -le 1) { Write-Host 'FAIL: Core sources not found.' -ForegroundColor Red; exit 1 }

Write-Host "compiler: $csc"
Write-Host "sources : $($sources.Count)"
Write-Host $ProjectRoot

$args = @('-nologo', '-langversion:7.3', "-out:$exe") + $sources
$compileOut = & $csc $args 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    Write-Host $compileOut
    Write-Host 'FAIL: test compilation failed.' -ForegroundColor Red
    exit 1
}

$testOut = & $exe 2>&1 | Out-String
Write-Host $testOut
exit $LASTEXITCODE
