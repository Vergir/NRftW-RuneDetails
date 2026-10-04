# Builds the release zip into dist/. The file name carries no version, so
# https://github.com/vergir/NRftW-RuneDetails/releases/latest/download/RuneDetails.zip always points at the newest release.
#   RuneDetails.zip   Mods/RuneDetails.dll, README.md, LICENSE, CHANGELOG.md - extract into the game folder.
# Needs MelonLoader's generated assemblies in the game folder (see README > Build).
# Usage: pwsh ./package.ps1 [-GameDir <path>]
param([string]$GameDir = "")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$props = @("-c", "Release", "-p:DeployToGame=false", "--nologo", "-v", "quiet")
if ($GameDir) { $props += "-p:GameDir=$GameDir" }
dotnet build RuneDetails.csproj @props
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$version = ([xml](Get-Content RuneDetails.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$dist = Join-Path $PSScriptRoot "dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
$stage = Join-Path $dist "stage"
New-Item -ItemType Directory (Join-Path $stage "Mods") -Force | Out-Null
Copy-Item "bin/Release/RuneDetails.dll" (Join-Path $stage "Mods")
Copy-Item README.md, LICENSE, CHANGELOG.md $stage
$zip = Join-Path $dist "RuneDetails.zip"
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Remove-Item $stage -Recurse -Force

Write-Host "Version $version"
Get-ChildItem $dist | ForEach-Object { "{0,-30} {1,8:N0} KB  SHA256 {2}" -f $_.Name, ($_.Length / 1KB), (Get-FileHash $_.FullName).Hash.Substring(0, 16) }
