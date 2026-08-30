param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Za-z._-]+$')]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$distDir = Join-Path $repoRoot 'dist'
$releaseRoot = Join-Path $repoRoot 'release'
$packageName = 'HSAchieveGuide-' + $Version
$packageDir = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot ($packageName + '.zip')

if (-not (Test-Path -LiteralPath (Join-Path $distDir 'HSAchieveGuide.exe'))) {
    throw 'Build output not found. Run scripts\compile-HSAchieveGuide.ps1 first.'
}
if (Test-Path -LiteralPath $packageDir) {
    throw "Release directory already exists: $packageDir"
}
if (Test-Path -LiteralPath $zipPath) {
    throw "Release archive already exists: $zipPath"
}

New-Item -ItemType Directory -Path (Join-Path $packageDir 'json\official-calibration') -Force | Out-Null
foreach ($name in @(
    'HSAchieveGuide.exe',
    'README.md',
    'README.en.md',
    'NOTICE.md',
    'LICENSE'
)) {
    Copy-Item -LiteralPath (Join-Path $distDir $name) -Destination (Join-Path $packageDir $name)
}
foreach ($name in @('flat.zh-CN.json', 'hierarchy.zh-CN.json', 'summary.json', 'README.txt')) {
    Copy-Item -LiteralPath (Join-Path $distDir ('json\official-calibration\' + $name)) `
        -Destination (Join-Path $packageDir ('json\official-calibration\' + $name))
}

Compress-Archive -LiteralPath $packageDir -DestinationPath $zipPath -CompressionLevel Optimal
$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath

$defender = Join-Path $env:ProgramFiles 'Windows Defender\MpCmdRun.exe'
if (Test-Path -LiteralPath $defender) {
    foreach ($scanPath in @((Join-Path $packageDir 'HSAchieveGuide.exe'), $zipPath)) {
        & $defender -Scan -ScanType 3 -File $scanPath
        if ($LASTEXITCODE -ne 0) {
            throw "Microsoft Defender did not approve the release file: $scanPath"
        }
    }
    Write-Host 'Microsoft Defender: no threats found.'
}
else {
    Write-Warning 'Microsoft Defender command-line scanner was not found; release files were not scanned.'
}

Write-Host ('Release: ' + $zipPath)
Write-Host ('SHA256: ' + $hash.Hash)
