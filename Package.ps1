param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') }
$releaseFolder = Join-Path $PSScriptRoot 'dist'
$archivePath = Join-Path $releaseFolder 'SAMEnhanced-v7.0.41.1-windows-x86.zip'
Compress-Archive -Path (Join-Path $releaseFolder 'windows-x86/*') -DestinationPath $archivePath -Force
$checksumLine = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $archivePath -Leaf)
[System.IO.File]::WriteAllText((Join-Path $releaseFolder 'SHA256SUMS.txt'), $checksumLine + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Output "Release package ready: $archivePath"
