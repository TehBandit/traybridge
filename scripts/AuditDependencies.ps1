param([string]$DotnetPath, [string]$ReportPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$toolchain = Get-Content -LiteralPath (Join-Path $projectRoot 'release-toolchain.json') -Raw | ConvertFrom-Json
$sdk = if ($DotnetPath) { (Resolve-Path -LiteralPath $DotnetPath).Path } elseif (Test-Path -LiteralPath (Join-Path $projectRoot '.tools\dotnet\dotnet.exe')) { Join-Path $projectRoot '.tools\dotnet\dotnet.exe' } else { (Get-Command dotnet -ErrorAction Stop).Source }
if (-not $ReportPath) { $ReportPath = Join-Path $projectRoot 'artifacts\dependency-review.json' }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$metadataUrl = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$metadata = Invoke-RestMethod -Uri $metadataUrl -TimeoutSec 30
if ($metadata.'support-phase' -notin @('active','maintenance')) { throw 'The pinned .NET channel is no longer supported.' }
if ($metadata.'latest-runtime' -ne $toolchain.runtimeVersion) { throw "The bundled runtime $($toolchain.runtimeVersion) is behind .NET $($metadata.'latest-runtime'). Review/update the toolchain and rebuild before releasing." }
$reviewedRepos = @('TsudaKageyu/minhook','ramensoftware/windhawk','ramensoftware/windhawk-mods')
$reviews = foreach ($repo in $reviewedRepos) {
    $url = "https://api.github.com/repos/$repo/security-advisories?per_page=100"
    $response = Invoke-RestMethod -Uri $url -Headers @{ Accept='application/vnd.github+json'; 'User-Agent'='TrayBridge-dependency-review'; 'X-GitHub-Api-Version'='2022-11-28' } -TimeoutSec 30
    $advisories = @($response | Where-Object { $null -ne $_ })
    if ($advisories.Count -gt 0) { throw "Published advisories for $repo need an applicability review: $($advisories.html_url -join ', ')" }
    [pscustomobject]@{ repository=$repo; source=$url; publishedAdvisories=$advisories.Count }
}
# Treat unavailable vulnerability data as a failed release check, never a clean result.
& $sdk restore (Join-Path $projectRoot 'TrayBridge\TrayBridge.csproj') -r win-x64 --force '-p:SelfContained=true' '-p:NuGetAudit=true' '-p:NuGetAuditMode=all' '-p:WarningsAsErrors=NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904%3BNU1905'
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore/audit failed; no successful review was recorded.' }
$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'TrayBridge\obj\project.assets.json') -Raw | ConvertFrom-Json
$downloads = $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies
foreach ($packName in @('Microsoft.NETCore.App.Runtime.win-x64','Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
    $packVersion = (($downloads | Where-Object name -eq $packName | Select-Object -First 1).version.Trim('[',']').Split(',')[0]).Trim()
    if ($packVersion -ne $toolchain.runtimeVersion) { throw "Unexpected restored runtime pack $packName $packVersion." }
}
$report = [pscustomobject]@{
    checkedUtc=[DateTime]::UtcNow.ToString('o'); runtime=$toolchain.runtimeVersion; latestRuntime=$metadata.'latest-runtime'; runtimeSource=$metadataUrl
    upstream=$reviews; nugetAudit='passed'; sourceProvenance='THIRD-PARTY-NOTICES.md'
    scope='Public upstream advisories and NuGet audit; does not certify the adapted native code or prove absence of vulnerabilities.'
}
New-Item -ItemType Directory -Path (Split-Path $ReportPath -Parent) -Force | Out-Null
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
Write-Host "Dependency review passed; report: $ReportPath"
