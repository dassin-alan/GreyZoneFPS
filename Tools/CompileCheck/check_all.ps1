<#
    GreyZone compile check
    ----------------------
    Compiles every gameplay script (Assets/GreyZone/Scripts/Core + .../Game) with a real
    C# compiler against Unity reference assemblies. Catches syntax / type / API errors
    without needing the Unity Editor.

    Usage:
        powershell -ExecutionPolicy Bypass -File Tools\CompileCheck\check_all.ps1

    Reference assemblies are looked up in this order:
        1) Tools/CompileCheck/refs/*.dll                       (local, optional)
        2) <Unity install>\Editor\Data\Managed\UnityEngine\*.dll
    Exit code 0 = compiled clean, 1 = errors.
#>
[CmdletBinding()]
param(
    [string]$ProjectRoot = '',
    [string]$LangVersion = '7.3'
)

$ErrorActionPreference = 'Continue'

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}
$checkDir  = $PSScriptRoot
$outDir    = Join-Path $checkDir 'out'
$outDll    = Join-Path $outDir 'GreyZone.check.dll'
$stubFile  = Join-Path $checkDir 'CoreApiStub.cs'

if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

# ---------------------------------------------------------------- compiler
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

# ---------------------------------------------------------------- references
function Find-Refs {
    $local = Join-Path $checkDir 'refs'
    if (Test-Path -LiteralPath $local) {
        $dlls = Get-ChildItem -LiteralPath $local -Filter '*.dll' -ErrorAction SilentlyContinue
        if ($dlls -and ($dlls | Where-Object { $_.Name -eq 'UnityEngine.CoreModule.dll' })) {
            return @{ Dir = $local; Files = $dlls; Source = 'local refs/' }
        }
    }
    $roots = @()
    foreach ($hub in @("$env:ProgramFiles\Unity\Hub\Editor", "${env:ProgramFiles(x86)}\Unity\Hub\Editor", 'D:\Unity\Hub\Editor', 'D:\Program Files\Unity\Hub\Editor', 'E:\Unity\Hub\Editor')) {
        if (Test-Path -LiteralPath $hub) {
            Get-ChildItem -LiteralPath $hub -Directory -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending |
                ForEach-Object { $roots += (Join-Path $_.FullName 'Editor\Data\Managed\UnityEngine') }
        }
    }
    $roots += "$env:ProgramFiles\Unity\Editor\Data\Managed\UnityEngine"
    foreach ($r in $roots) {
        if (Test-Path -LiteralPath $r) {
            $dlls = Get-ChildItem -LiteralPath $r -Filter '*.dll' -ErrorAction SilentlyContinue
            if ($dlls -and ($dlls | Where-Object { $_.Name -eq 'UnityEngine.CoreModule.dll' })) {
                return @{ Dir = $r; Files = $dlls; Source = "Unity install: $r" }
            }
        }
    }
    return $null
}

$csc = Find-Csc
if (-not $csc) { Write-Host 'FAIL: no C# compiler found (looked for Roslyn csc and .NET Framework csc).' -ForegroundColor Red; exit 1 }

$refInfo = Find-Refs
if (-not $refInfo) {
    Write-Host 'FAIL: no Unity reference assemblies found.' -ForegroundColor Red
    Write-Host '      Put UnityEngine*.dll into Tools\CompileCheck\refs\ or install Unity.' -ForegroundColor Yellow
    exit 1
}

# ---------------------------------------------------------------- sources
$coreDir = Join-Path $ProjectRoot 'Assets\GreyZone\Scripts\Core'
$gameDir = Join-Path $ProjectRoot 'Assets\GreyZone\Scripts\Game'
$sources = @()
$coreFiles = @()
$gameFiles = @()
if (Test-Path -LiteralPath $coreDir) { $coreFiles = @(Get-ChildItem -LiteralPath $coreDir -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue) }
if (Test-Path -LiteralPath $gameDir) { $gameFiles = @(Get-ChildItem -LiteralPath $gameDir -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue) }

$usingStub = $false
if ($coreFiles.Count -eq 0) {
    $usingStub = $true
    $sources += $stubFile
} else {
    $sources += ($coreFiles | ForEach-Object { $_.FullName })
}
$sources += ($gameFiles | ForEach-Object { $_.FullName })

if ($sources.Count -eq 0) { Write-Host 'FAIL: no .cs source files found.' -ForegroundColor Red; exit 1 }

Write-Host "compiler : $csc"
Write-Host "refs     : $($refInfo.Source)  ($($refInfo.Files.Count) dll)"
Write-Host "core     : $($coreFiles.Count) file(s)$(if ($usingStub) { ' -> using CoreApiStub.cs' } else { '' })"
Write-Host "game     : $($gameFiles.Count) file(s)"
Write-Host "sources  : $($sources.Count)"
Write-Host ''

$args = @('-nologo', '-target:library', "-langversion:$LangVersion", "-out:$outDll",
          '-define:UNITY_2021_3_OR_NEWER', '-nowarn:0649,0414,0169,0067')
foreach ($d in $refInfo.Files) { $args += ('/r:' + $d.FullName) }
foreach ($s in $sources) { $args += $s }

$output = & $csc $args 2>&1 | Out-String
$code = $LASTEXITCODE
Write-Host $output

$errors = ($output -split "`r?`n") | Where-Object { $_ -match ':\s*error\s' }
if ($code -eq 0 -and $errors.Count -eq 0) {
    Write-Host "PASS: compiled clean -> $outDll" -ForegroundColor Green
    exit 0
}
Write-Host ("FAIL: {0} compiler error(s)" -f $errors.Count) -ForegroundColor Red
exit 1
