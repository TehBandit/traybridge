# Release signing helpers. Private keys stay in the Windows certificate store/provider.
function Resolve-ReleaseSignTool {
    param([string]$Path)
    if (-not $Path) {
        $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($command) { $Path = $command.Source }
        else {
            $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
            $candidates = Get-ChildItem -LiteralPath $sdkBin -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
                Sort-Object { [Version]$_.Name } -Descending
            foreach ($candidate in $candidates) {
                $tool = Join-Path $candidate.FullName 'x64\signtool.exe'
                if (Test-Path -LiteralPath $tool -PathType Leaf) { $Path = $tool; break }
            }
        }
    }
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'SignTool was not found. Install the Windows SDK or pass -SignToolPath.'
    }
    (Resolve-Path -LiteralPath $Path).Path
}

function New-ReleaseSigningContext {
    param(
        [string]$CertificateThumbprint,
        [ValidateSet('CurrentUser','LocalMachine')][string]$CertificateStore = 'CurrentUser',
        [string]$TimestampServer,
        [string]$SignToolPath
    )
    $thumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
    if ($thumbprint -notmatch '^[A-F0-9]{40}$') {
        throw 'Signed publishing requires -CertificateThumbprint (a code-signing certificate in the My store). Use -Unsigned only for development builds.'
    }
    $timestamp = $null
    if (-not [Uri]::TryCreate($TimestampServer, [UriKind]::Absolute, [ref]$timestamp) -or
        $timestamp.Scheme -notin @('http','https') -or -not $timestamp.Host -or $timestamp.UserInfo -or $timestamp.Fragment) {
        throw 'Signed publishing requires -TimestampServer with your provider''s HTTP(S) RFC 3161 endpoint.'
    }
    $certificate = Get-Item -LiteralPath "Cert:\$CertificateStore\My\$thumbprint" -ErrorAction SilentlyContinue
    if (-not $certificate -or -not $certificate.HasPrivateKey) { throw "No accessible private key for code-signing certificate $thumbprint in $CertificateStore\My." }
    $now = Get-Date
    if ($certificate.NotBefore -gt $now -or $certificate.NotAfter -le $now) { throw 'The code-signing certificate is not currently valid.' }
    if (-not ($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })) {
        throw 'The selected certificate does not have the Code Signing enhanced key usage.'
    }
    if ($certificate.Subject -eq $certificate.Issuer) { throw 'A self-signed certificate cannot be used for a public release.' }
    [pscustomobject]@{
        Thumbprint = $thumbprint
        Store = $CertificateStore
        TimestampServer = $timestamp.AbsoluteUri
        Tool = Resolve-ReleaseSignTool $SignToolPath
    }
}

function Invoke-ReleaseSignTool {
    param($Context, [string[]]$Arguments)
    & $Context.Tool @Arguments
    # SignTool returns 2 on warnings (including failed timestamping); fail closed.
    if ($LASTEXITCODE -ne 0) { throw "SignTool $($Arguments[0]) failed or reported a warning (exit $LASTEXITCODE). The release is incomplete." }
}

function Assert-ReleaseSignature {
    param($Context, [string]$Path)
    Invoke-ReleaseSignTool $Context @('verify','/q','/pa','/all','/tw','/sha1',$Context.Thumbprint,$Path)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path -ErrorAction Stop
    if ($signature.Status -ne 'Valid' -or $signature.SignatureType -ne 'Authenticode' -or
        -not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $Context.Thumbprint -or
        -not $signature.TimeStamperCertificate) {
        throw "A valid, embedded signature with the selected signer and a timestamp is required: $Path"
    }
}

function Add-ReleaseSignature {
    param($Context, [string]$Path)
    $arguments = @('sign','/q','/fd','SHA256','/tr',$Context.TimestampServer,'/td','SHA256','/sha1',$Context.Thumbprint,'/s','My','/d','TrayBridge')
    if ($Context.Store -eq 'LocalMachine') { $arguments += '/sm' }
    $arguments += $Path
    Invoke-ReleaseSignTool $Context $arguments
    Assert-ReleaseSignature $Context $Path
}

function Assert-ReleaseInventory {
    param([string]$InventoryPath, [string]$OutputDirectory)
    # This metadata was generated locally by the build, never supplied by the app/user.
    [xml]$inventory = Get-Content -LiteralPath $InventoryPath -Raw -ErrorAction Stop
    $files = @($inventory.ReleaseInventory.Files.ReleaseFile | Where-Object { $_.Runtime -eq 'false' })
    foreach ($required in @('TrayBridge.Native.dll','TrayBridge.Symbols.exe')) {
        if (@($files | Where-Object { $_.Name -eq $required }).Count -ne 1) { throw "Missing or duplicate release inventory entry: $required" }
    }
    foreach ($file in $files) {
        if ([IO.Path]::GetFileName($file.Name) -ne $file.Name -or $file.Name -in @('.','..')) { throw 'Invalid release inventory filename.' }
        $actual = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $file.Name) -Algorithm SHA256 -ErrorAction Stop).Hash
        if ($actual -ne $file.Hash) { throw "Release file changed after its uninstall hash was embedded: $($file.Name)" }
    }
}

function Complete-ReleasePublish {
    param($SigningContext, [string]$OutputDirectory, [string]$InventoryPath,
        [scriptblock]$PrepareInventory, [scriptblock]$PublishController)
    $nativeFiles = @('TrayBridge.Native.dll','TrayBridge.Symbols.exe')
    if ($SigningContext) {
        foreach ($name in $nativeFiles) { Add-ReleaseSignature $SigningContext (Join-Path $OutputDirectory $name) }
    }
    & $PrepareInventory
    Assert-ReleaseInventory $InventoryPath $OutputDirectory
    & $PublishController
    if ($SigningContext) {
        Add-ReleaseSignature $SigningContext (Join-Path $OutputDirectory 'TrayBridge.exe')
    }
    Assert-ReleaseInventory $InventoryPath $OutputDirectory
    if ($SigningContext) {
        foreach ($name in @('TrayBridge.exe') + $nativeFiles) { Assert-ReleaseSignature $SigningContext (Join-Path $OutputDirectory $name) }
        Write-Host 'Signed release verified: all three binaries have valid signatures and timestamps; uninstall hashes match.'
    }
    else { Write-Warning 'UNSIGNED BUILD. Package public previews with -UnsignedPreview; this output has no publisher signature.' }
}
