param([Parameter(Mandatory=$true)][string]$PublishedDirectory, [string]$OutputDirectory, [switch]$UnsignedDevelopment, [switch]$UnsignedPreview)
$ErrorActionPreference = 'Stop'
if ($UnsignedDevelopment -and $UnsignedPreview) { throw 'Choose either -UnsignedDevelopment or -UnsignedPreview, not both.' }
$projectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ReleaseSigning.ps1')
. (Join-Path $PSScriptRoot 'ReleaseSource.ps1')
$published = (Resolve-Path -LiteralPath $PublishedDirectory).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot ('artifacts\package-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count -gt 0) { throw 'Package output must be new or empty.' }
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'TrayBridge\TrayBridge.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$tag = 'v' + $version
$gitArgs = @("--git-dir=$projectRoot\.git", "--work-tree=$projectRoot")
$revision = & git @gitArgs rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source revision.' }
if (-not $UnsignedDevelopment) {
    $changes = & git @gitArgs status --porcelain --untracked-files=all
    if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Commit the complete release source and tests before creating a public package.' }
    $tagRevision = & git @gitArgs rev-parse "$tag^{commit}"
    if ($LASTEXITCODE -ne 0 -or $tagRevision -ne $revision) { throw "Tag $tag must identify this exact source revision." }
    $auditPath = Join-Path $projectRoot 'artifacts\dependency-review.json'
    if (-not (Test-Path -LiteralPath $auditPath)) { throw 'Run scripts/AuditDependencies.ps1 before packaging.' }
    $audit = Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
    if ($audit.nugetAudit -ne 'passed' -or [DateTime]::UtcNow - [DateTime]::Parse($audit.checkedUtc).ToUniversalTime() -gt [TimeSpan]::FromHours(24)) { throw 'A successful dependency review from the last 24 hours is required.' }
}
if (-not $UnsignedDevelopment -and -not $UnsignedPreview) {
    $tool = Resolve-ReleaseSignTool
    foreach ($name in @('TrayBridge.exe','TrayBridge.Native.dll','TrayBridge.Symbols.exe')) {
        $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $published $name)
        if (-not $signature.SignerCertificate) { throw "Unsigned release component: $name" }
        Assert-ReleaseSignature ([pscustomobject]@{ Tool=$tool; Thumbprint=$signature.SignerCertificate.Thumbprint }) (Join-Path $published $name)
    }
}
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = Join-Path $published 'TrayBridge.exe'; $start.Arguments = '--release-info'
$start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($start)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Published package metadata check timed out.' }
    if ($process.ExitCode -ne 0) { throw "Published package metadata check failed: $($stderr.Result)" }
    $info = $stdout.Result | ConvertFrom-Json
} finally { $process.Dispose() }
if ($info.version -ne $version) { throw "Published app version $($info.version) does not match source $version." }
if ($info.sourceHash -ne (Get-ReleaseSourceHash $projectRoot)) { throw 'The published app was built from different source. Rebuild before packaging.' }
if (-not $UnsignedDevelopment -and $info.sourceRevision -ne $revision) { throw 'Rebuild from the exact tagged commit before packaging.' }
foreach ($entry in $info.files | Where-Object { -not $_.Runtime }) {
    if ([IO.Path]::GetFileName($entry.Name) -ne $entry.Name) { throw 'Invalid embedded release filename.' }
    if ((Get-FileHash -LiteralPath (Join-Path $published $entry.Name) -Algorithm SHA256).Hash -ne $entry.Hash) { throw "Package differs from its compiled uninstall inventory: $($entry.Name)" }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$suffix = if ($UnsignedDevelopment) { '-development' } else { '' }
$binaryName = "TrayBridge-$version-win-x64$suffix.zip"
$sourceName = "TrayBridge-$version-source$suffix.zip"
function Write-Archive([string]$Path, [object[]]$Entries) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try { foreach ($entry in $Entries) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $entry.Path, $entry.Name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null } }
    finally { $archive.Dispose(); $stream.Dispose() }
}
$binaryEntries = @([pscustomobject]@{ Path=(Join-Path $published 'TrayBridge.exe'); Name='TrayBridge.exe' })
$binaryEntries += @($info.files | Where-Object { -not $_.Runtime } | ForEach-Object { [pscustomobject]@{ Path=(Join-Path $published $_.Name); Name=$_.Name } })
foreach ($required in @('TrayBridge.Native.dll','TrayBridge.Symbols.exe','LICENSE','THIRD-PARTY-NOTICES.md','README.md')) {
    if ($binaryEntries.Name -notcontains $required) { throw "Required package content missing: $required" }
}
Write-Archive (Join-Path $output $binaryName) $binaryEntries
if ($UnsignedDevelopment) {
    $sourceFiles = @(Get-ReleaseSourceFiles $projectRoot)
    $sourceEntries = @($sourceFiles | ForEach-Object { [pscustomobject]@{ Path=$_.FullName; Name=$_.FullName.Substring($projectRoot.Length + 1).Replace('\','/') } })
    Write-Archive (Join-Path $output $sourceName) $sourceEntries
} else {
    & git @gitArgs archive --format=zip "--output=$(Join-Path $output $sourceName)" $tag
    if ($LASTEXITCODE -ne 0) { throw 'Corresponding source archive failed.' }
}
$artifactNames = @($binaryName,$sourceName)
if ($UnsignedPreview) {
    foreach ($name in @('traybridge-demo.gif','traybridge-demo.png')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot "docs\assets\$name") -Destination (Join-Path $output $name)
        $artifactNames += $name
    }
}
$checksums = foreach ($name in $artifactNames) { ((Get-FileHash -LiteralPath (Join-Path $output $name) -Algorithm SHA256).Hash.ToLowerInvariant()) + '  ' + $name }
$checksums | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
[pscustomobject]@{ version=$version; sourceRevision=$revision; sourceHash=$info.sourceHash; sourceTag=$(if ($UnsignedDevelopment) { 'uncommitted working snapshot' } else { $tag }); development=[bool]$UnsignedDevelopment; prerelease=[bool]$UnsignedPreview; signing=$(if ($UnsignedDevelopment -or $UnsignedPreview) { 'unsigned' } else { 'authenticode' }); inventoryHash=$info.inventoryHash; artifacts=$artifactNames } |
    ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $output 'release.json') -Encoding UTF8
Write-Host "Package created: $output"
