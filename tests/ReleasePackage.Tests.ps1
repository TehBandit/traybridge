param([Parameter(Mandatory=$true)][string]$PackageDirectory, [Parameter(Mandatory=$true)][string]$PublishedDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manifest = Get-Content -LiteralPath (Join-Path $PackageDirectory 'release.json') -Raw | ConvertFrom-Json
$passed = 0
function Check([bool]$Value, [string]$Name) { if (-not $Value) { throw $Name }; $script:passed++; Write-Host "PASS $Name" }
if ($manifest.prerelease) {
    Check (-not $manifest.development -and $manifest.signing -eq 'unsigned') 'unsigned preview is distinct from a development snapshot'
    $revision = & git "--git-dir=$projectRoot\.git" "--work-tree=$projectRoot" rev-parse "$($manifest.sourceTag)^{commit}"
    Check ($LASTEXITCODE -eq 0 -and $revision -eq $manifest.sourceRevision) 'preview provenance identifies the exact release tag'
    try { & (Join-Path $projectRoot 'scripts\NewReleasePackage.ps1') -PublishedDirectory $PublishedDirectory -OutputDirectory (Join-Path $projectRoot 'artifacts\refused-signed-package'); throw 'Unsigned preview was accepted as signed' }
    catch { Check ($_.Exception.Message -match 'Unsigned release component') 'unsigned preview cannot pass signed release requirements' }
}
foreach ($line in Get-Content -LiteralPath (Join-Path $PackageDirectory 'SHA256SUMS.txt')) {
    $hash, $name = $line -split '  ', 2
    Check ((Get-FileHash -LiteralPath (Join-Path $PackageDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant() -eq $hash) "archive checksum matches $name"
}
$binary = [IO.Compression.ZipFile]::OpenRead((Join-Path $PackageDirectory $manifest.artifacts[0]))
try {
    foreach ($name in @('TrayBridge.exe','TrayBridge.Native.dll','TrayBridge.Symbols.exe','LICENSE','README.md','THIRD-PARTY-NOTICES.md','SECURITY.md','ReleaseNotes.md')) { Check ($null -ne $binary.GetEntry($name)) "binary archive includes $name" }
    Check (@($binary.Entries | Where-Object { $_.FullName -match '(^|/)(bin|obj|\.tools|\.git)/' -or $_.FullName -match '\.(pfx|p12|lib|exp)$' }).Count -eq 0) 'binary archive excludes build tools and signing keys'
} finally { $binary.Dispose() }
$source = [IO.Compression.ZipFile]::OpenRead((Join-Path $PackageDirectory $manifest.artifacts[1]))
try {
    foreach ($name in @('build.ps1','global.json','Native/Bridge.cpp','Native/Restoration.h','TrayBridge/Uninstaller.cs','scripts/ReleaseSigning.ps1','tests/RecoveryChecks.cs','docs/assets/traybridge-demo.gif','docs/demo/Program.cs','LICENSE','SECURITY.md')) { Check ($null -ne $source.GetEntry($name)) "source archive includes $name" }
    Check (@($source.Entries | Where-Object { $_.FullName -match '(^|/)(bin|obj|\.tools|\.git)/' -or $_.FullName -match '\.(pfx|p12)$' }).Count -eq 0) 'source archive excludes build output and signing keys'
} finally { $source.Dispose() }
if ($manifest.development) {
    try { & (Join-Path $projectRoot 'scripts\NewReleasePackage.ps1') -PublishedDirectory $PublishedDirectory -OutputDirectory (Join-Path $projectRoot 'artifacts\refused-public-package'); throw 'Public package was accepted' }
    catch { Check ($_.Exception.Message -match 'Commit the complete release source|Tag .*must identify|Unsigned release component|unknown revision|ambiguous argument') 'development output cannot pass public source/signature requirements' }
}
Write-Host "Passed $passed release-package checks."
