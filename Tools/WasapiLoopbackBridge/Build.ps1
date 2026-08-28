$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputDirectory = Join-Path $projectRoot 'Assets\StreamingAssets\WasapiLoopbackBridge'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$netstandard = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\netstandard.dll'
$naudioDirectory = Join-Path $projectRoot 'Assets\Plugins\NAudio'

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
& $compiler /nologo /target:exe /optimize+ /platform:anycpu `
    /out:"$outputDirectory\WasapiLoopbackBridge.exe" `
    /reference:"$netstandard" `
    /reference:"$naudioDirectory\NAudio.Core.dll" `
    /reference:"$naudioDirectory\NAudio.Wasapi.dll" `
    "$PSScriptRoot\Program.cs" `
    "$PSScriptRoot\HardRealtimeOnsetDetector.cs"

if ($LASTEXITCODE -ne 0) { throw "WasapiLoopbackBridge build failed: $LASTEXITCODE" }

Copy-Item -Force "$naudioDirectory\NAudio.Core.dll" $outputDirectory
Copy-Item -Force "$naudioDirectory\NAudio.Wasapi.dll" $outputDirectory
