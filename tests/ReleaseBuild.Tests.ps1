$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet).Source }
$fixture = Join-Path $projectRoot ('.tools\release-build-' + [Guid]::NewGuid().ToString('N') + '.xml')
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
$passed = 0
function Check-Build([string[]]$Properties, [bool]$ExpectedSuccess, [string]$Name) {
    $result = & $script:sdk msbuild (Join-Path $script:projectRoot 'TrayBridge\TrayBridge.csproj') -nologo -t:ValidateTrayBridgeRelease '-p:PublishSingleFile=true' @Properties 2>&1
    if (($LASTEXITCODE -eq 0) -ne $ExpectedSuccess) { throw "$Name failed: $result" }
    $script:passed++; Write-Host "PASS $Name"
}
try {
    Check-Build @() $false 'direct publication refuses unprepared metadata'
    Check-Build @('-p:TrayBridgePreparedRelease=true',"-p:TrayBridgeReleaseInventoryPath=$fixture",'-p:TrayBridgeReleaseInventoryHash=missing') $false 'prepared publication refuses missing inventory'
    [IO.File]::WriteAllText($fixture, '<ReleaseInventory><Files /></ReleaseInventory>')
    $hash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    Check-Build @('-p:TrayBridgePreparedRelease=true',"-p:TrayBridgeReleaseInventoryPath=$fixture",'-p:TrayBridgeReleaseInventoryHash=stale') $false 'prepared publication refuses stale inventory hash'
    Check-Build @('-p:TrayBridgePreparedRelease=true',"-p:TrayBridgeReleaseInventoryPath=$fixture","-p:TrayBridgeReleaseInventoryHash=$hash") $true 'exact prepared inventory passes its hash guard'
} finally { if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture } }
Write-Host "Passed $passed release-build checks."
