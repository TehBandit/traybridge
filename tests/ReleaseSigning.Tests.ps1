$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $projectRoot 'scripts\ReleaseSigning.ps1')
$passed = 0
function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL $Name" }
    $script:passed++; Write-Host "PASS $Name"
}
function Refuses([scriptblock]$Action, [string]$Message, [string]$Name) {
    try { & $Action } catch { Check ($_.Exception.Message -like "*$Message*") $Name; return }
    throw "FAIL $Name (did not refuse)"
}

# No test certificate is installed and no production binary is signed by these tests.
# Use the existing Microsoft tool's public certificate to test EKU field handling.
$tool = Resolve-ReleaseSignTool
$realCertificate = (Get-AuthenticodeSignature -LiteralPath $tool).SignerCertificate
Check ([bool]($realCertificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })) 'read the real certificate Code Signing EKU'
$thumb = 'A' * 40
$context = [pscustomobject]@{ Thumbprint=$thumb; Store='CurrentUser'; TimestampServer='https://timestamp.example.test'; Tool=$tool }
Refuses { New-ReleaseSigningContext } 'CertificateThumbprint' 'missing certificate configuration fails before building'
Refuses { New-ReleaseSigningContext -CertificateThumbprint $thumb -TimestampServer 'file:///tmp/time' } 'RFC 3161' 'invalid timestamp endpoint is rejected'
Refuses { New-ReleaseSigningContext -CertificateThumbprint $thumb -TimestampServer 'https://user:secret@example.test/time' } 'RFC 3161' 'credentials in timestamp URLs are rejected'
Refuses { New-ReleaseSigningContext -CertificateThumbprint $thumb -TimestampServer $context.TimestampServer -SignToolPath $tool } 'No accessible private key' 'absent certificate cannot produce a signed release'
$warningTool = [pscustomobject]@{ Tool={ $global:LASTEXITCODE=2 } }
Refuses { Invoke-ReleaseSignTool $warningTool @('sign') } 'warning (exit 2)' 'a SignTool warning stops release success'
$errorTool = [pscustomobject]@{ Tool={ $global:LASTEXITCODE=1 } }
Refuses { Invoke-ReleaseSignTool $errorTool @('verify') } 'exit 1' 'a SignTool verification error stops release success'

$certificateFixture = [pscustomobject]@{
    HasPrivateKey=$true; NotBefore=(Get-Date).AddDays(-1); NotAfter=(Get-Date).AddDays(1)
    EnhancedKeyUsageList=@([pscustomobject]@{ ObjectId='1.3.6.1.5.5.7.3.3' })
    Subject='CN=Fixture Publisher'; Issuer='CN=Fixture CA'
}
function Get-Item { param($LiteralPath,$ErrorAction) return $script:certificateFixture }
$configured = New-ReleaseSigningContext -CertificateThumbprint $thumb.ToLower() -TimestampServer $context.TimestampServer
Check ($configured.Thumbprint -eq $thumb -and $configured.Store -eq 'CurrentUser') 'explicit store certificate selection is normalized'
$certificateFixture.NotAfter = (Get-Date).AddDays(-1)
Refuses { New-ReleaseSigningContext -CertificateThumbprint $thumb -TimestampServer $context.TimestampServer -SignToolPath $tool } 'not currently valid' 'expired certificate is rejected'
$certificateFixture.NotAfter = (Get-Date).AddDays(1)
$certificateFixture.EnhancedKeyUsageList = @([pscustomobject]@{ ObjectId='1.3.6.1.5.5.7.3.1' })
Refuses { New-ReleaseSigningContext -CertificateThumbprint $thumb -TimestampServer $context.TimestampServer -SignToolPath $tool } 'enhanced key usage' 'server certificate cannot sign the release'
$certificateFixture.EnhancedKeyUsageList = @([pscustomobject]@{ ObjectId='1.3.6.1.5.5.7.3.3' })
$certificateFixture.Issuer = $certificateFixture.Subject
Refuses { New-ReleaseSigningContext -CertificateThumbprint $thumb -TimestampServer $context.TimestampServer -SignToolPath $tool } 'self-signed' 'self-signed public-release certificate is rejected'
Remove-Item Function:\Get-Item

$fixtureRoot = Join-Path $projectRoot ('.tools\signing-tests-' + [Guid]::NewGuid().ToString('N'))
$fixtureBoundary = [IO.Path]::GetFullPath((Join-Path $projectRoot '.tools')) + '\'
if (-not ([IO.Path]::GetFullPath($fixtureRoot).StartsWith($fixtureBoundary, [StringComparison]::OrdinalIgnoreCase))) { throw 'Unsafe fixture root.' }
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$fixtureFiles = @('TrayBridge.Native.dll','TrayBridge.Symbols.exe','TrayBridge.exe','inventory.xml')
$events = New-Object 'Collections.Generic.List[string]'
$calls = New-Object 'Collections.Generic.List[object]'
$signatures = @{}
$failName = ''; $missingTimestamp = $false; $wrongSigner = $false; $invalidSignature = $false
$inventoryPath = Join-Path $fixtureRoot 'inventory.xml'
function Invoke-ReleaseSignTool {
    param($Context,[string[]]$Arguments)
    $name = [IO.Path]::GetFileName($Arguments[-1])
    $script:calls.Add($Arguments)
    $script:events.Add($Arguments[0] + ':' + $name)
    if ($Arguments[0] -eq 'sign') {
        if ($name -eq $script:failName) { throw 'Simulated signing/timestamp failure' }
        [IO.File]::AppendAllText($Arguments[-1], '|signed-and-timestamped')
        $script:signatures[$Arguments[-1]] = $true
    }
}
function Get-AuthenticodeSignature {
    param($LiteralPath,$ErrorAction)
    [pscustomobject]@{
        Status=$(if ($script:invalidSignature -or -not $script:signatures[$LiteralPath]) { 'NotSigned' } else { 'Valid' })
        SignatureType='Authenticode'
        SignerCertificate=[pscustomobject]@{ Thumbprint=$(if ($script:wrongSigner) { 'B' * 40 } else { $script:thumb }) }
        TimeStamperCertificate=$(if ($script:missingTimestamp) { $null } else { [pscustomobject]@{ Subject='Fixture TSA' } })
    }
}
function Reset-Fixture {
    foreach ($name in $script:fixtureFiles) { [IO.File]::WriteAllText((Join-Path $script:fixtureRoot $name),'fixture') }
    $script:events.Clear(); $script:calls.Clear(); $script:signatures.Clear()
    $script:failName=''; $script:missingTimestamp=$false; $script:wrongSigner=$false; $script:invalidSignature=$false
}
$prepare = {
    $script:events.Add('inventory')
    $xml = '<ReleaseInventory><Files>'
    foreach ($name in @('TrayBridge.Native.dll','TrayBridge.Symbols.exe')) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $script:fixtureRoot $name) -Algorithm SHA256).Hash
        $xml += "<ReleaseFile><Name>$name</Name><Hash>$hash</Hash><Runtime>false</Runtime></ReleaseFile>"
    }
    [IO.File]::WriteAllText($script:inventoryPath, $xml + '</Files></ReleaseInventory>')
}
$publish = { $script:events.Add('publish'); [IO.File]::WriteAllText((Join-Path $script:fixtureRoot 'TrayBridge.exe'),'published fixture') }
try {
    Reset-Fixture
    Complete-ReleasePublish $context $fixtureRoot $inventoryPath $prepare $publish
    $order = $events.ToArray()
    Check ([Array]::IndexOf($order,'sign:TrayBridge.Native.dll') -lt [Array]::IndexOf($order,'inventory') -and [Array]::IndexOf($order,'sign:TrayBridge.Symbols.exe') -lt [Array]::IndexOf($order,'inventory')) 'both native binaries are signed before uninstall inventory generation'
    Check ([Array]::IndexOf($order,'verify:TrayBridge.Native.dll') -lt [Array]::IndexOf($order,'inventory') -and [Array]::IndexOf($order,'verify:TrayBridge.Symbols.exe') -lt [Array]::IndexOf($order,'inventory')) 'both native signatures are verified before inventory generation'
    Check ([Array]::IndexOf($order,'publish') -lt [Array]::IndexOf($order,'sign:TrayBridge.exe')) 'controller signing happens after publication'
    Check ($signatures.Count -eq 3) 'all three release components are signed'
    Check (@($calls | Where-Object { $_[0] -eq 'sign' -and $_ -contains 'SHA256' -and $_ -contains '/tr' -and $_ -contains '/td' -and $_ -contains '/sha1' }).Count -eq 3) 'signing uses explicit signer, SHA256 and RFC 3161 timestamps'
    Check (@($calls | Where-Object { $_[0] -eq 'verify' -and $_ -contains '/pa' -and $_ -contains '/tw' -and $_ -contains '/all' }).Count -eq 6) 'native and controller signatures are verified before and after publication'
    Assert-ReleaseInventory $inventoryPath $fixtureRoot
    Check $true 'inventory matches the final signed native files'

    Reset-Fixture; $failName='TrayBridge.Symbols.exe'
    Refuses { Complete-ReleasePublish $context $fixtureRoot $inventoryPath $prepare $publish } 'Simulated' 'native signing failure aborts the release'
    Check (-not $events.Contains('inventory') -and -not $events.Contains('publish')) 'native failure prevents inventory generation and publication'
    Reset-Fixture; $failName='TrayBridge.exe'
    Refuses { Complete-ReleasePublish $context $fixtureRoot $inventoryPath $prepare $publish } 'Simulated' 'controller signing failure aborts the release'
    Reset-Fixture; $missingTimestamp=$true
    Refuses { Complete-ReleasePublish $context $fixtureRoot $inventoryPath $prepare $publish } 'timestamp' 'a missing timestamp prevents release success'
    Reset-Fixture; $wrongSigner=$true
    Refuses { Complete-ReleasePublish $context $fixtureRoot $inventoryPath $prepare $publish } 'selected signer' 'the wrong signing certificate is rejected'
    Reset-Fixture; $invalidSignature=$true
    Refuses { Complete-ReleasePublish $context $fixtureRoot $inventoryPath $prepare $publish } 'valid, embedded' 'an invalid signature is rejected'
    Reset-Fixture
    $changedPublish = { & $script:publish; [IO.File]::AppendAllText((Join-Path $script:fixtureRoot 'TrayBridge.Native.dll'),'|changed-after-hashing') }
    Refuses { Complete-ReleasePublish $context $fixtureRoot $inventoryPath $prepare $changedPublish } 'changed after' 'post-inventory file changes prevent release success'
    Reset-Fixture
    Complete-ReleasePublish $null $fixtureRoot $inventoryPath $prepare $publish -WarningAction SilentlyContinue
    Check ($signatures.Count -eq 0 -and $events.Contains('publish')) 'explicit unsigned development publication skips signing'
    $context.Store='LocalMachine'; $calls.Clear()
    Add-ReleaseSignature $context (Join-Path $fixtureRoot 'TrayBridge.exe')
    Check ($calls[0] -contains '/sm') 'machine-store signing selects the machine store explicitly'
}
finally {
    # Exact test-owned paths only; no recursive deletion or certificate-store changes.
    foreach ($name in $fixtureFiles) { Remove-Item -LiteralPath (Join-Path $fixtureRoot $name) -ErrorAction SilentlyContinue }
    [IO.Directory]::Delete($fixtureRoot, $false)
}
Write-Host "Passed $passed release-signing checks (mock signing; no release certificate installed)."
