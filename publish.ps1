param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    [string]$OutputPath = "$PSScriptRoot\publish\CopyFinder-Standalone",

    [string]$ZipPath = '',

    [switch]$IncludeDebugSymbols
)

$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$projectPath = Join-Path $projectRoot 'CopyFinder.csproj'
$projectXml = [xml](Get-Content -LiteralPath $projectPath)
$version = [string]$projectXml.Project.PropertyGroup.Version
$targetFramework = [string]$projectXml.Project.PropertyGroup.TargetFramework
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = 'dev'
}
if ([string]::IsNullOrWhiteSpace($targetFramework)) {
    throw "TargetFramework was not found in $projectPath"
}

$outputFullPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$projectRootWithSlash = $projectRoot.TrimEnd('\') + '\'

if (-not $outputFullPath.StartsWith($projectRootWithSlash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean publish output outside the project root: $outputFullPath"
}

$publishRoot = Join-Path $projectRoot 'publish'
if (-not (Test-Path -LiteralPath $publishRoot)) {
    New-Item -ItemType Directory -Path $publishRoot | Out-Null
}

if ([string]::IsNullOrWhiteSpace($ZipPath)) {
    $ZipPath = Join-Path $publishRoot "CopyFinder-v$version-$Runtime-Standalone.zip"
}

$zipFullPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ZipPath)
if (-not $zipFullPath.StartsWith($projectRootWithSlash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write zip output outside the project root: $zipFullPath"
}

if (Test-Path -LiteralPath $outputFullPath) {
    Remove-Item -LiteralPath $outputFullPath -Recurse -Force
}

Get-ChildItem -LiteralPath $publishRoot -Filter "CopyFinder-v*-$Runtime-Standalone.zip" -File |
    Remove-Item -Force

Get-ChildItem -LiteralPath $publishRoot -Filter "CopyFinder-v*-$Runtime-Standalone.zip.sha256.txt" -File |
    Remove-Item -Force

if (Test-Path -LiteralPath $zipFullPath) {
    Remove-Item -LiteralPath $zipFullPath -Force
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false `
    --output $outputFullPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$runtimeBuildOutput = Join-Path $projectRoot "bin\$Configuration\$targetFramework\$Runtime"
if (-not (Test-Path -LiteralPath $runtimeBuildOutput)) {
    throw "Runtime build output not found: $runtimeBuildOutput"
}

$xamlArtifacts = Get-ChildItem -LiteralPath $runtimeBuildOutput -File |
    Where-Object { $_.Extension -eq '.xbf' -or $_.Name -eq 'CopyFinder.pri' }

if (-not ($xamlArtifacts | Where-Object { $_.Extension -eq '.xbf' })) {
    throw "No compiled XAML .xbf files were found in $runtimeBuildOutput"
}

if (-not ($xamlArtifacts | Where-Object { $_.Name -eq 'CopyFinder.pri' })) {
    throw "CopyFinder.pri was not found in $runtimeBuildOutput"
}

foreach ($artifact in $xamlArtifacts) {
    Copy-Item -LiteralPath $artifact.FullName -Destination (Join-Path $outputFullPath $artifact.Name) -Force
}

$cmdPath = Join-Path $outputFullPath 'Start-CopyFinder.cmd'
@'
@echo off
cd /d "%~dp0"
start "" "%~dp0CopyFinder.exe"
'@ | Set-Content -LiteralPath $cmdPath -Encoding ASCII

if (-not $IncludeDebugSymbols) {
    Get-ChildItem -LiteralPath $outputFullPath -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

Compress-Archive -Path (Join-Path $outputFullPath '*') -DestinationPath $zipFullPath -Force

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zipFullPath
$hashPath = "$zipFullPath.sha256.txt"
"$($hash.Hash)  $(Split-Path -Leaf $zipFullPath)" | Set-Content -LiteralPath $hashPath -Encoding ASCII

Write-Host "CopyFinder published to: $outputFullPath"
Write-Host "Run: $(Join-Path $outputFullPath 'CopyFinder.exe')"
Write-Host "Zip created: $zipFullPath"
Write-Host "SHA-256 created: $hashPath"
