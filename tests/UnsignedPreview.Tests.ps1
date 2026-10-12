param([Parameter(Mandatory=$true)][string]$PublishedDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$published = (Resolve-Path -LiteralPath $PublishedDirectory).Path
$fixture = Join-Path $projectRoot ('artifacts\preview-guards-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$fixture\scripts","$fixture\TrayBridge","$fixture\artifacts" -Force | Out-Null
Copy-Item -LiteralPath "$projectRoot\scripts\NewReleasePackage.ps1","$projectRoot\scripts\ReleaseSource.ps1","$projectRoot\scripts\ReleaseSigning.ps1" -Destination "$fixture\scripts"
Copy-Item -LiteralPath "$projectRoot\TrayBridge\TrayBridge.csproj" -Destination "$fixture\TrayBridge"
[IO.File]::WriteAllText("$fixture\.gitignore", "artifacts/`n")
& git init --quiet $fixture
if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize isolated preview fixture.' }
$gitArgs = @("--git-dir=$fixture\.git", "--work-tree=$fixture")
function Fixture-Git([string[]]$Arguments) {
    & git @script:gitArgs @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Fixture git command failed.' }
}
function Write-Audit([int]$HoursAgo = 0) {
    @{ nugetAudit='passed'; checkedUtc=[DateTime]::UtcNow.AddHours(-$HoursAgo).ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath "$fixture\artifacts\dependency-review.json" -Encoding UTF8
}
$passed = 0
function Refuses([string[]]$Flags, [string]$Expected, [string]$Name) {
    $ErrorActionPreference = 'Continue'
    try {
        $result = & powershell.exe -NoProfile -File "$fixture\scripts\NewReleasePackage.ps1" -PublishedDirectory $published -OutputDirectory "$fixture\artifacts\rejected" @Flags 2>&1
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = 'Stop' }
    if ($code -eq 0 -or ($result -join "`n") -notmatch $Expected) { throw "$Name failed: $result" }
    $script:passed++; Write-Host "PASS $Name"
}
Fixture-Git @('add','-A')
Fixture-Git @('-c','user.name=Preview fixture','-c','user.email=fixture@example.invalid','commit','--quiet','-m','Fixture source')
[xml]$project = Get-Content "$fixture\TrayBridge\TrayBridge.csproj" -Raw
$tag = 'v' + $project.Project.PropertyGroup.Version
Write-Audit
Refuses @('-UnsignedPreview','-UnsignedDevelopment') 'Choose either' 'preview and development modes are mutually exclusive'
Refuses @('-UnsignedPreview') 'must identify this exact source revision' 'unsigned preview requires an exact version tag'
Fixture-Git @('tag',$tag)
[IO.File]::WriteAllText("$fixture\unreviewed.txt", 'Uncommitted source')
Refuses @('-UnsignedPreview') 'Commit the complete release source' 'unsigned preview rejects uncommitted source'
Remove-Item -LiteralPath "$fixture\unreviewed.txt"
Write-Audit 25
Refuses @('-UnsignedPreview') 'last 24 hours' 'unsigned preview rejects an expired dependency review'
Write-Audit
Refuses @() 'Unsigned release component' 'signed mode still rejects unsigned binaries'
Refuses @('-UnsignedPreview') 'built from different source' 'unsigned preview rejects a mismatched source fingerprint'
[IO.File]::WriteAllText("$fixture\reviewed.txt", 'New reviewed commit')
Fixture-Git @('add','-A')
Fixture-Git @('-c','user.name=Preview fixture','-c','user.email=fixture@example.invalid','commit','--quiet','-m','New fixture source')
Refuses @('-UnsignedPreview') 'must identify this exact source revision' 'unsigned preview rejects a tag pointing at another commit'
Write-Host "Passed $passed unsigned-preview guard checks. Fixture: $fixture"
# Expected child-process failures must not become the successful CI step's exit code.
$global:LASTEXITCODE = 0
