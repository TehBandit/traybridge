function Get-ReleaseSourceFiles {
    param([string]$ProjectRoot)
    foreach ($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','SECURITY.md','.gitattributes','.gitignore','global.json','release-toolchain.json','build.ps1','setup.ps1')) {
        $path = Join-Path $ProjectRoot $name
        if (Test-Path -LiteralPath $path) { Get-Item -LiteralPath $path }
    }
    foreach ($directory in @('Native','TrayBridge','tests','scripts','docs','.github')) {
        $path = Join-Path $ProjectRoot $directory
        if (Test-Path -LiteralPath $path) { Get-ChildItem -LiteralPath $path -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -notin @('.pfx','.p12') } }
    }
}
function Get-ReleaseSourceHash {
    param([string]$ProjectRoot)
    $rows = @(Get-ReleaseSourceFiles $ProjectRoot | ForEach-Object {
        $_.FullName.Substring($ProjectRoot.Length + 1).Replace('\','/') + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    } | Sort-Object)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes(($rows -join "`n"))))).Replace('-','') }
    finally { $algorithm.Dispose() }
}
