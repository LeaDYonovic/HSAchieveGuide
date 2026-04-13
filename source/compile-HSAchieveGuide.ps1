Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolsDir = $PSScriptRoot
$csc      = Join-Path $toolsDir '_nuget\Microsoft.Net.Compilers.Toolset.4.10.0\tasks\net472\csc.exe'
$src      = Join-Path $toolsDir 'HSAchieveGuide.cs'
$out      = Join-Path $toolsDir 'HSAchieveGuide.exe'
$jsonDir  = Join-Path $toolsDir 'json'

$resources = @(
    @{ F = (Join-Path $jsonDir 'hs-achievement-data.json'); N = 'EmbedJson__hs_achievement_data' },
    @{ F = (Join-Path $jsonDir 'guide-table.json');          N = 'EmbedJson__guide_table' }
)

$fxRef = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path $fxRef)) { $fxRef = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319' }

$cscArgs = [System.Collections.Generic.List[string]]::new()
foreach ($a in @('/target:winexe','/platform:x86','/langversion:latest','/utf8output','/optimize+')) { $cscArgs.Add($a) }
foreach ($dll in @('System','System.Core','System.Data','System.Drawing','System.Windows.Forms','System.Web.Extensions')) {
    $cscArgs.Add('/reference:' + $fxRef + '\' + $dll + '.dll')
}
foreach ($r in $resources) {
    if (Test-Path $r.F) { $cscArgs.Add('/resource:' + $r.F + ',' + $r.N) }
}
$cscArgs.Add('/out:' + $out)
$cscArgs.Add($src)

Write-Host ('Compiling... resources: ' + ($cscArgs | Where-Object { $_ -like '/resource:*' }).Count)
& $csc $cscArgs.ToArray()

if ($LASTEXITCODE -ne 0) { throw ('Compile failed: ' + $LASTEXITCODE) }
$sizeMB = [math]::Round((Get-Item $out).Length / 1MB, 2)
Write-Host ('Done: ' + $out + ' (' + $sizeMB + ' MB)')
