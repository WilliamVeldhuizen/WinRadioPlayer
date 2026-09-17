<#
.SYNOPSIS
    Builds the ZapperRadio MSI installer.
.DESCRIPTION
    Publishes the app self-contained (no .NET or Windows App SDK install needed on the target PC)
    and packs it into artifacts\installer\ZapperRadio-<version>-<arch>.msi.
.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version 1.1.0 -Arch arm64
#>
param(
    [string] $Version = '1.1.2',
    [ValidateSet('x64', 'arm64')]
    [string] $Arch = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish\$Arch\"
$outputDir = Join-Path $root 'artifacts\installer'

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "Publishing ZapperRadio $Version ($Arch)..." -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\ZapperRadio\ZapperRadio.csproj') `
    -c Release -r "win-$Arch" --self-contained true `
    -p:Platform=$Arch -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Write-Host 'Building MSI...' -ForegroundColor Cyan
dotnet build (Join-Path $root 'installer\ZapperRadio.Installer.wixproj') `
    -c Release -p:Platform=$Arch -p:ProductVersion=$Version -p:PublishDir=$publishDir -o $outputDir
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }

$msi = Join-Path $outputDir "ZapperRadio-$Version-$Arch.msi"
Write-Host "Installer: $msi ($([math]::Round((Get-Item $msi).Length / 1MB, 1)) MB)" -ForegroundColor Green
