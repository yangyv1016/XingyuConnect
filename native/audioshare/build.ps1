param(
    [Parameter(Mandatory=$true)][string]$ZigPath,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (!$OutputPath) { $OutputPath = Join-Path $projectRoot 'vendor\audioshare\audio_capture_xingyu.dll' }
& $ZigPath c++ (Join-Path $PSScriptRoot 'audio_capture.cpp') -shared -target x86_64-windows-gnu -O2 -std=c++17 -fms-extensions -static -lws2_32 -lole32 -lwinmm -luuid -o $OutputPath
if ($LASTEXITCODE -ne 0) { throw 'AudioShare native extension build failed.' }
