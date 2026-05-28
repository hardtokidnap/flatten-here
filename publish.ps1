#!/usr/bin/env pwsh
# AOT publish wrapper for FlattenHere.
#
# Why this script exists: the .NET 8 AOT build chain shells out to vswhere.exe
# to locate link.exe from the MSVC toolchain. vswhere.exe lives in
# "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\" which is NOT on the
# default user PATH, so `dotnet publish` fails with MSB3073 from a regular
# pwsh / cmd shell. CI on windows-latest already has vswhere on PATH, so this
# script is dev-machine only but is safe to run anywhere.

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$installerDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
$vswhere = Join-Path $installerDir 'vswhere.exe'

if (-not (Test-Path $vswhere)) {
    throw @"
vswhere.exe not found at:
  $vswhere

Install Visual Studio 2022/2026 (or VS Build Tools) with the
"Desktop development with C++" workload, then re-run this script.
"@
}

# Verify the C++ tools are actually installed (not just the Installer shim).
$vcInstall = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if (-not $vcInstall) {
    throw "vswhere found, but no install reports the C++ tools component (Microsoft.VisualStudio.Component.VC.Tools.x86.x64). Open the VS Installer and add 'Desktop development with C++'."
}

if (-not ($env:PATH -split ';' | Where-Object { $_ -eq $installerDir })) {
    $env:PATH = "$installerDir;$env:PATH"
}

$root = Split-Path -Parent $PSCommandPath
$csproj = Join-Path $root 'src\FlattenHere.csproj'

Write-Host "Publishing Native AOT ($Configuration, $Runtime)" -ForegroundColor Cyan
& dotnet publish $csproj -c $Configuration -r $Runtime --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit $LASTEXITCODE)"
}

$pubDir = Join-Path $root "src\bin\$Configuration\net8.0-windows\$Runtime\publish"
$exe = Join-Path $pubDir 'FlattenHere.exe'
if (-not (Test-Path $exe)) {
    throw "Expected $exe to exist after publish, but it doesn't."
}

$sizeKB = [math]::Round((Get-Item $exe).Length / 1KB, 1)
Write-Host ""
Write-Host "Built: $exe" -ForegroundColor Green
Write-Host "Size:  $sizeKB KB" -ForegroundColor Green
