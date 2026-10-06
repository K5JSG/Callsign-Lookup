<#
.SYNOPSIS
    Builds Callsign Lookup.

.DESCRIPTION
    Runs the tests, publishes a self-contained, single-file exe (no .NET
    runtime needed on the target PC) with its Data\ folder into publish\,
    and, if Inno Setup is installed, compiles the installer into dist\.

.PARAMETER Version
    Version stamped into the exe and the installer filename.

.PARAMETER SkipInstaller
    Publish the exe only; do not build the installer.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Version 1.0.0
#>

[CmdletBinding()]
param(
    [string]$Version = "1.7.0",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$solution = Join-Path $root "Callsign Lookup.slnx"
$project = Join-Path $root "Callsign Lookup.csproj"
$publishDir = Join-Path $root "publish"
$distDir = Join-Path $root "dist"

Write-Host ""
Write-Host "Callsign Lookup - build v$Version" -ForegroundColor Cyan
Write-Host ("=" * 40)

# --- 1. Test ---------------------------------------------------------------

Write-Host ""
Write-Host "Running tests..." -ForegroundColor Yellow
dotnet test $solution -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

# --- 2. Publish ------------------------------------------------------------

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host ""
Write-Host "Publishing self-contained exe..." -ForegroundColor Yellow

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -p:FileVersion="$Version.0" `
    -p:AssemblyVersion="$Version.0" `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $publishDir "Callsign Lookup.exe"
if (-not (Test-Path $exe)) { throw "Published exe not found at $exe" }

# The installer ships only the exe and Data\, so a native DLL left next to the
# exe (WPF's UI Automation needs four) would be missing on installed PCs and
# crash the app at startup. IncludeNativeLibrariesForSelfExtract bundles them.
$looseDlls = Get-ChildItem $publishDir -Filter *.dll
if ($looseDlls) { throw "Loose DLLs next to the exe would not be installed: $($looseDlls.Name -join ', ')" }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "  $exe  ($sizeMb MB)" -ForegroundColor Green

# --- 3. Installer ------------------------------------------------------------

if ($SkipInstaller) {
    Write-Host ""
    Write-Host "Skipping installer (-SkipInstaller)." -ForegroundColor DarkGray
    exit 0
}

$iscc = @(
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup was not found, so no installer was built." -ForegroundColor Yellow
    Write-Host "Install it from https://jrsoftware.org/isdl.php and run this again," -ForegroundColor Yellow
    Write-Host "or distribute publish\Callsign Lookup.exe together with publish\Data." -ForegroundColor Yellow
    exit 0
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

Write-Host ""
Write-Host "Building installer..." -ForegroundColor Yellow

& $iscc "/DMyAppVersion=$Version" (Join-Path $root "Installer\InnoSetup\Callsign Lookup.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }

$setup = Join-Path $distDir "Callsign Lookup Setup $Version.exe"
if (Test-Path $setup) {
    $setupMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
    Write-Host ""
    Write-Host "  $setup  ($setupMb MB)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
Write-Host ""
