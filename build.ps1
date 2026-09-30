<#
.SYNOPSIS
    Builds Callsign Lookup.

.DESCRIPTION
    Runs the tests, then publishes a self-contained, single-file exe (no .NET
    runtime needed on the target PC) with its Data\ folder into publish\.

.PARAMETER Version
    Version stamped into the exe.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Version 1.0.0
#>

[CmdletBinding()]
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$solution = Join-Path $root "Callsign Lookup.slnx"
$project = Join-Path $root "Callsign Lookup.csproj"
$publishDir = Join-Path $root "publish"

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
    -p:Version=$Version `
    -p:FileVersion="$Version.0" `
    -p:AssemblyVersion="$Version.0" `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $publishDir "Callsign Lookup.exe"
if (-not (Test-Path $exe)) { throw "Published exe not found at $exe" }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "  $exe  ($sizeMb MB)" -ForegroundColor Green
Write-Host "  Ship it together with the publish\Data folder." -ForegroundColor Green
Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
Write-Host ""
