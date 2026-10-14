param([switch]$Package)
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
foreach ($project in @('SAM.Game/SAM.Game.csproj', 'SAM.Enhanced.Tests/SAM.Enhanced.Tests.csproj')) {
    & dotnet build $project -c Release -p:Platform=x86 --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
& './SAM.Enhanced.Tests/bin/x86/Release/net48/SAM.Enhanced.Tests.exe'
if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed.' }
$bundlePath = Join-Path $PSScriptRoot 'dist/windows-x86'
New-Item -ItemType Directory -Path $bundlePath -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'upload') -File |
    Where-Object { $_.Extension -in @('.exe', '.dll', '.config') } |
    Copy-Item -Destination $bundlePath -Force
foreach ($document in @('README.md', 'README.UPSTREAM.md', 'LICENSE.txt', 'NOTICE.txt', 'NOTICE.md', 'QUICKSTART.txt', 'Start.cmd', 'Preview.cmd')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $bundlePath -Force
}
Write-Output "Ready: $bundlePath — run SAM.Picker.exe"

if (Test-Path -LiteralPath (Join-Path $PSScriptRoot "docs")) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "docs") -Destination $bundlePath -Recurse -Force
}
} finally { Pop-Location }
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'assets')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination $bundlePath -Recurse -Force
}
if ($Package) { & (Join-Path $PSScriptRoot 'Package.ps1') -SkipBuild }
