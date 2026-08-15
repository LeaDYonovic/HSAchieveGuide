param(
    [switch]$NoCompilerDownload
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$sourceDir = Join-Path $repoRoot 'src'
$dataDir = Join-Path $repoRoot 'data'
$assetsDir = Join-Path $repoRoot 'assets'
$buildDir = Join-Path $repoRoot '.build'
$distDir = Join-Path $repoRoot 'dist'
$compilerVersion = '4.10.0'
$compilerDir = Join-Path $buildDir ('microsoft.net.compilers.toolset.' + $compilerVersion)
$compilerPackage = Join-Path $buildDir ('microsoft.net.compilers.toolset.' + $compilerVersion + '.nupkg')
$csc = Join-Path $compilerDir 'tasks\net472\csc.exe'
$generatedIcon = Join-Path $buildDir 'HSAchieveGuide.generated.ico'

New-Item -ItemType Directory -Force -Path $buildDir, $distDir | Out-Null

if (-not (Test-Path -LiteralPath $csc)) {
    if ($NoCompilerDownload) {
        throw "Roslyn compiler was not found at $csc"
    }

    $packageUrl = 'https://api.nuget.org/v3-flatcontainer/microsoft.net.compilers.toolset/' +
        $compilerVersion + '/microsoft.net.compilers.toolset.' + $compilerVersion + '.nupkg'
    Write-Host "Downloading Roslyn compiler $compilerVersion from NuGet..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri $packageUrl -OutFile $compilerPackage

    if (Test-Path -LiteralPath $compilerDir) {
        throw "Incomplete compiler directory already exists: $compilerDir"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($compilerPackage, $compilerDir)
}

$frameworkReference = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'
if (-not (Test-Path -LiteralPath $frameworkReference)) {
    $frameworkReference = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
}
if (-not (Test-Path -LiteralPath $frameworkReference)) {
    throw '.NET Framework 4.x reference assemblies were not found.'
}

function New-PngIconFromImage {
    param(
        [Parameter(Mandatory = $true)][string]$SourceImage,
        [Parameter(Mandatory = $true)][string]$DestinationIco
    )

    Add-Type -AssemblyName System.Drawing
    $image = [System.Drawing.Image]::FromFile($SourceImage)
    try {
        $size = 256
        $bitmap = New-Object System.Drawing.Bitmap $size, $size
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

                $scale = [Math]::Min($size / $image.Width, $size / $image.Height)
                $drawWidth = [int][Math]::Round($image.Width * $scale)
                $drawHeight = [int][Math]::Round($image.Height * $scale)
                $offsetX = [int][Math]::Floor(($size - $drawWidth) / 2)
                $offsetY = [int][Math]::Floor(($size - $drawHeight) / 2)
                $graphics.DrawImage($image, $offsetX, $offsetY, $drawWidth, $drawHeight)
            }
            finally {
                $graphics.Dispose()
            }

            $pngStream = New-Object System.IO.MemoryStream
            try {
                $bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
                $pngBytes = $pngStream.ToArray()
                $stream = [System.IO.File]::Open(
                    $DestinationIco,
                    [System.IO.FileMode]::Create,
                    [System.IO.FileAccess]::Write)
                try {
                    $writer = New-Object System.IO.BinaryWriter $stream
                    try {
                        $writer.Write([UInt16]0)
                        $writer.Write([UInt16]1)
                        $writer.Write([UInt16]1)
                        $writer.Write([Byte]0)
                        $writer.Write([Byte]0)
                        $writer.Write([Byte]0)
                        $writer.Write([Byte]0)
                        $writer.Write([UInt16]1)
                        $writer.Write([UInt16]32)
                        $writer.Write([UInt32]$pngBytes.Length)
                        $writer.Write([UInt32]22)
                        $writer.Write($pngBytes)
                    }
                    finally {
                        $writer.Dispose()
                    }
                }
                finally {
                    $stream.Dispose()
                }
            }
            finally {
                $pngStream.Dispose()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }
    finally {
        $image.Dispose()
    }
}

$logo = Join-Path $assetsDir 'logo.png'
if (Test-Path -LiteralPath $logo) {
    New-PngIconFromImage -SourceImage $logo -DestinationIco $generatedIcon
}

$commonReferences = @('System', 'System.Core', 'System.Web.Extensions') |
    ForEach-Object { '/reference:' + (Join-Path $frameworkReference ($_.ToString() + '.dll')) }

$mainArgs = [System.Collections.Generic.List[string]]::new()
foreach ($argument in @('/target:winexe', '/platform:x86', '/langversion:latest', '/utf8output', '/optimize+')) {
    $mainArgs.Add($argument)
}
foreach ($reference in $commonReferences) {
    $mainArgs.Add($reference)
}
foreach ($assembly in @('System.Data', 'System.Drawing', 'System.Windows.Forms')) {
    $mainArgs.Add('/reference:' + (Join-Path $frameworkReference ($assembly + '.dll')))
}
if (Test-Path -LiteralPath $generatedIcon) {
    $mainArgs.Add('/win32icon:' + $generatedIcon)
}
$mainArgs.Add('/resource:' + (Join-Path $dataDir 'hs-achievement-data.json') + ',EmbedJson__hs_achievement_data')
$mainArgs.Add('/resource:' + (Join-Path $dataDir 'guide-table.json') + ',EmbedJson__guide_table')
$mainOutput = Join-Path $distDir 'HSAchieveGuide.exe'
$mainArgs.Add('/out:' + $mainOutput)
$mainArgs.Add((Join-Path $sourceDir 'HSAchieveGuide.cs'))

Write-Host 'Compiling HSAchieveGuide.exe...'
& $csc $mainArgs.ToArray()
if ($LASTEXITCODE -ne 0) {
    throw "Main application compilation failed with exit code $LASTEXITCODE"
}

$helperArgs = [System.Collections.Generic.List[string]]::new()
foreach ($argument in @('/target:exe', '/platform:x86', '/langversion:latest', '/utf8output', '/optimize+')) {
    $helperArgs.Add($argument)
}
foreach ($reference in $commonReferences) {
    $helperArgs.Add($reference)
}
if (Test-Path -LiteralPath $generatedIcon) {
    $helperArgs.Add('/win32icon:' + $generatedIcon)
}
$helperOutput = Join-Path $distDir 'ExportMindVisionAchievements.v3.exe'
$helperArgs.Add('/out:' + $helperOutput)
$helperArgs.Add((Join-Path $sourceDir 'ExportMindVisionAchievements.cs'))

Write-Host 'Compiling ExportMindVisionAchievements.v3.exe...'
& $csc $helperArgs.ToArray()
if ($LASTEXITCODE -ne 0) {
    throw "Export helper compilation failed with exit code $LASTEXITCODE"
}

$calibrationSource = Join-Path $dataDir 'official-calibration'
$calibrationOutput = Join-Path $distDir 'json\official-calibration'
New-Item -ItemType Directory -Force -Path $calibrationOutput | Out-Null
foreach ($name in @('flat.zh-CN.json', 'hierarchy.zh-CN.json', 'summary.json', 'README.txt')) {
    Copy-Item -LiteralPath (Join-Path $calibrationSource $name) -Destination (Join-Path $calibrationOutput $name) -Force
}
foreach ($name in @('README.md', 'README.en.md', 'NOTICE.md', 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination (Join-Path $distDir $name) -Force
}

Write-Host ''
Write-Host ('Build complete: ' + $distDir)
Get-Item -LiteralPath $mainOutput, $helperOutput |
    ForEach-Object { Write-Host ('  ' + $_.Name + '  ' + [Math]::Round($_.Length / 1MB, 2) + ' MB') }
