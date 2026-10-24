param([string]$OutputRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$projectFile = Join-Path $projectRoot 'src\XingyuConnect.csproj'
[xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw
$version = $projectXml.Project.PropertyGroup.Version
$packageName = "XingyuConnect-$version-win-x64"
$releaseDirectory = Join-Path $OutputRoot $packageName
$archivePath = Join-Path $OutputRoot "$packageName.zip"
if ((Test-Path -LiteralPath $releaseDirectory) -or (Test-Path -LiteralPath $archivePath)) {
    throw "Output already exists. Choose another OutputRoot or increment the version: $releaseDirectory"
}
& dotnet publish $projectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $releaseDirectory
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$unexpected = Get-ChildItem -LiteralPath $releaseDirectory -Recurse -File | Where-Object { $_.Extension -in @('.bat','.cmd','.ps1','.vbs','.pdb','.lib','.psd','.csproj') }
if ($unexpected) { throw "Developer files found in user package: $($unexpected.Name -join ', ')" }
& dotnet run --project (Join-Path $projectRoot 'tests\Smoke\Smoke.csproj') -c Release -- $releaseDirectory
if ($LASTEXITCODE -ne 0) { throw 'Release smoke checks failed.' }
Compress-Archive -LiteralPath $releaseDirectory -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputRoot "$packageName.sha256"), "$hash  $packageName.zip`n", [Text.UTF8Encoding]::new($false))
Write-Output "Release package: $archivePath"
