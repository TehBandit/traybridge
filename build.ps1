param(
    [switch]$Publish, [switch]$NativeOnly, [string]$DotnetPath, [string]$OutputDirectory,
    [switch]$Unsigned, [switch]$TestNativeRestoration,
    [string]$CertificateThumbprint = $env:TRAYBRIDGE_SIGN_CERT_THUMBPRINT,
    [ValidateSet('CurrentUser','LocalMachine')][string]$CertificateStore = 'CurrentUser',
    [string]$TimestampServer = $env:TRAYBRIDGE_TIMESTAMP_SERVER,
    [string]$SignToolPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
. (Join-Path $projectRoot 'scripts\ReleaseSource.ps1')
$sourceHash = Get-ReleaseSourceHash $projectRoot
$sourceRevision = ''
if (Test-Path -LiteralPath (Join-Path $projectRoot '.git')) {
    $sourceRevision = & git "--git-dir=$projectRoot\.git" "--work-tree=$projectRoot" rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine the build source revision.' }
}
$toolchain = Get-Content -LiteralPath (Join-Path $projectRoot 'release-toolchain.json') -Raw | ConvertFrom-Json
. (Join-Path $projectRoot 'scripts\ReleaseSigning.ps1')
if ($Unsigned -and -not $Publish) { throw '-Unsigned is only supported with -Publish.' }
if ($NativeOnly -and $Publish) { throw 'Choose either -NativeOnly or -Publish.' }
if ($Unsigned -and ($CertificateThumbprint -or $TimestampServer -or $SignToolPath)) { throw 'Do not combine -Unsigned with signing configuration; clear signing environment variables for unsigned builds.' }
$signingContext = $null
if ($Publish -and -not $Unsigned) {
    # Fail before compiling or modifying output if signing cannot be configured.
    $signingContext = New-ReleaseSigningContext -CertificateThumbprint $CertificateThumbprint -CertificateStore $CertificateStore -TimestampServer $TimestampServer -SignToolPath $SignToolPath
}
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) { throw 'Visual C++ build tools and Windows SDK are required.' }
if (-not (Test-Path -LiteralPath (Join-Path $vsPath "VC\Tools\MSVC\$($toolchain.vcToolsVersion)\bin\Hostx64\x64\cl.exe"))) { throw "Install the pinned Visual C++ toolset $($toolchain.vcToolsVersion)." }
$devCmd = Join-Path $vsPath 'Common7\Tools\VsDevCmd.bat'
$toolDir = Join-Path $projectRoot '.tools\native'
$distDir = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'dist' }
if ($signingContext -and (Test-Path -LiteralPath $distDir) -and @(Get-ChildItem -LiteralPath $distDir -Force).Count -gt 0) {
    throw 'Signed publishing requires a new or empty output folder. Pass -OutputDirectory to stage a fresh release.'
}
New-Item -ItemType Directory -Path $toolDir,$distDir -Force | Out-Null
$cmdFile = Join-Path $toolDir 'compile.cmd'
$nativePath = Join-Path $projectRoot 'Native'
$commandText = @"
@echo off
call "$devCmd" -arch=x64 -host_arch=x64 -vcvars_ver=$($toolchain.vcToolsVersion) -winsdk=$($toolchain.windowsSdkVersion) >nul
if errorlevel 1 exit /b 1
cd /d "$toolDir"
cl /nologo /std:c11 /O2 /MT /FI"$nativePath\preinclude.h" /D_WIN32_WINNT=0x0A00 /DUNICODE /D_UNICODE /DNOMINMAX /DWIN32_LEAN_AND_MEAN /c "$nativePath\vendor\MinHook\src\buffer.c" "$nativePath\vendor\MinHook\src\hook.c" "$nativePath\vendor\MinHook\src\trampoline.c" "$nativePath\vendor\MinHook\src\hde\hde64.c"
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /await /Zc:__cplusplus /Zc:preprocessor /EHsc /O2 /MT /bigobj /DUNICODE /D_UNICODE /DNOMINMAX /DWIN32_LEAN_AND_MEAN /I"$nativePath" /LD "$nativePath\Bridge.cpp" buffer.obj hook.obj trampoline.obj hde64.obj /link /OUT:"$distDir\TrayBridge.Native.dll" /SECTION:.shared,RWS user32.lib comctl32.lib ole32.lib oleaut32.lib runtimeobject.lib shlwapi.lib shell32.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /O2 /MT "$nativePath\Symbols.cpp" /link /OUT:"$distDir\TrayBridge.Symbols.exe" dbghelp.lib
exit /b %errorlevel%
"@
[IO.File]::WriteAllText($cmdFile, $commandText, [Text.Encoding]::ASCII)
& $env:ComSpec /d /c $cmdFile
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
if ($TestNativeRestoration) {
    $testCommand = "@echo off`r`ncall `"$devCmd`" -arch=x64 -host_arch=x64 -vcvars_ver=$($toolchain.vcToolsVersion) -winsdk=$($toolchain.windowsSdkVersion) >nul`r`nif errorlevel 1 exit /b 1`r`ncd /d `"$toolDir`"`r`ncl /nologo /std:c++17 /EHsc /O2 /MT /I`"$nativePath`" `"$projectRoot\tests\NativeRestoration.cpp`" /Fe:`"$toolDir\NativeRestoration.Tests.exe`"`r`nif errorlevel 1 exit /b 1`r`n`"$toolDir\NativeRestoration.Tests.exe`"`r`nexit /b %errorlevel%`r`n"
    $testCommandPath = Join-Path $toolDir 'restoration-tests.cmd'
    [IO.File]::WriteAllText($testCommandPath, $testCommand, [Text.Encoding]::ASCII)
    & $env:ComSpec /d /c $testCommandPath
    if ($LASTEXITCODE -ne 0) { throw 'Native restoration regression checks failed.' }
}
if ($NativeOnly) { return }
$sdk = if ($DotnetPath) { (Resolve-Path -LiteralPath $DotnetPath).Path } elseif (Test-Path "$projectRoot\.tools\dotnet\dotnet.exe") { "$projectRoot\.tools\dotnet\dotnet.exe" } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = "$projectRoot\.tools\dotnet-home"
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = "$projectRoot\.tools\packages" }
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if ($Publish) {
    if ($signingContext) { & (Join-Path $projectRoot 'scripts\AuditDependencies.ps1') -DotnetPath $sdk }
    $inventoryPath = Join-Path $projectRoot 'TrayBridge\obj\uninstall-release.xml'
    Complete-ReleasePublish -SigningContext $signingContext -OutputDirectory $distDir -InventoryPath $inventoryPath -PrepareInventory {
        & $sdk restore "$projectRoot\TrayBridge\TrayBridge.csproj" -r win-x64 '-p:SelfContained=true'
        if ($LASTEXITCODE -ne 0) { throw 'Managed restore failed.' }
        Copy-Item -LiteralPath "$projectRoot\LICENSE","$projectRoot\THIRD-PARTY-NOTICES.md","$projectRoot\README.md" -Destination $distDir -Force
        Copy-Item -LiteralPath "$projectRoot\SECURITY.md" -Destination $distDir -Force
        Copy-Item -LiteralPath "$projectRoot\docs\releases\0.1.5.md" -Destination (Join-Path $distDir 'ReleaseNotes.md') -Force
        Copy-Item -LiteralPath "$projectRoot\Native\vendor\MinHook\LICENSE.txt" -Destination "$distDir\MinHook-LICENSE.txt" -Force
        Copy-Item -LiteralPath "$projectRoot\Native\vendor\TASKBAR-MULTI-TRAY-LICENSE.txt" -Destination "$distDir\Taskbar-Multi-Tray-LICENSE.txt" -Force
        $assets = Get-Content -LiteralPath "$projectRoot\TrayBridge\obj\project.assets.json" -Raw | ConvertFrom-Json
        $downloads = $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies
        $runtimeVersion = (($downloads | Where-Object name -eq 'Microsoft.NETCore.App.Runtime.win-x64' | Select-Object -First 1).version.Trim('[',']').Split(',')[0]).Trim()
        $runtimePack = Join-Path $env:NUGET_PACKAGES "microsoft.netcore.app.runtime.win-x64\$runtimeVersion"
        $desktopPack = Join-Path $env:NUGET_PACKAGES "microsoft.windowsdesktop.app.runtime.win-x64\$runtimeVersion"
        Copy-Item -LiteralPath "$runtimePack\LICENSE.TXT" -Destination "$distDir\Dotnet-Runtime-LICENSE.txt" -Force
        Copy-Item -LiteralPath "$runtimePack\THIRD-PARTY-NOTICES.TXT" -Destination "$distDir\Dotnet-Runtime-THIRD-PARTY-NOTICES.txt" -Force
        Copy-Item -LiteralPath "$desktopPack\LICENSE" -Destination "$distDir\Dotnet-Desktop-LICENSE.txt" -Force
        # Compile ownership hashes into the controller; a writable sidecar cannot expand uninstall scope.
        $xmlSettings = New-Object System.Xml.XmlWriterSettings
        $xmlSettings.Indent = $true
        $inventory = [System.Xml.XmlWriter]::Create($inventoryPath, $xmlSettings)
        try {
            $inventory.WriteStartDocument()
            $inventory.WriteStartElement('ReleaseInventory')
        if ((Get-ReleaseSourceHash $projectRoot) -ne $sourceHash) { throw 'Source changed during compilation; restart the build.' }
        $inventory.WriteElementString('SourceHash', $sourceHash)
        $inventory.WriteElementString('SourceRevision', $sourceRevision)
            $inventory.WriteStartElement('Files')
        foreach ($releaseName in @('TrayBridge.Native.dll','TrayBridge.Symbols.exe','LICENSE','THIRD-PARTY-NOTICES.md','README.md','SECURITY.md','ReleaseNotes.md','MinHook-LICENSE.txt','Taskbar-Multi-Tray-LICENSE.txt','Dotnet-Runtime-LICENSE.txt','Dotnet-Runtime-THIRD-PARTY-NOTICES.txt','Dotnet-Desktop-LICENSE.txt')) {
                $inventory.WriteStartElement('ReleaseFile')
                $inventory.WriteElementString('Name', $releaseName)
                $inventory.WriteElementString('Hash', (Get-FileHash -LiteralPath (Join-Path $distDir $releaseName) -Algorithm SHA256).Hash)
                $inventory.WriteElementString('Runtime', 'false')
                $inventory.WriteEndElement()
            }
            foreach ($pack in @($runtimePack, $desktopPack)) {
                foreach ($runtimeFile in Get-ChildItem -LiteralPath (Join-Path $pack 'runtimes\win-x64\native') -Filter '*.dll' -File) {
                    $inventory.WriteStartElement('ReleaseFile')
                    $inventory.WriteElementString('Name', $runtimeFile.Name)
                    $inventory.WriteElementString('Hash', (Get-FileHash -LiteralPath $runtimeFile.FullName -Algorithm SHA256).Hash)
                    $inventory.WriteElementString('Runtime', 'true')
                    $inventory.WriteEndElement()
                }
            }
            $inventory.WriteEndElement()
            $inventory.WriteEndElement()
            $inventory.WriteEndDocument()
        }
        finally { $inventory.Dispose() }
    } -PublishController {
        $inventoryHash = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash
        & $sdk publish "$projectRoot\TrayBridge\TrayBridge.csproj" --no-restore -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:DebugType=embedded' '-p:TrayBridgePreparedRelease=true' "-p:TrayBridgeReleaseInventoryPath=$inventoryPath" "-p:TrayBridgeReleaseInventoryHash=$inventoryHash" -o $distDir
        if ($LASTEXITCODE -ne 0) { throw 'Managed publish failed; the release is incomplete.' }
        if ((Get-ReleaseSourceHash $projectRoot) -ne $sourceHash) { throw 'Source changed during publication; this staging output is incomplete.' }
    }
}
else {
    & $sdk build "$projectRoot\TrayBridge\TrayBridge.csproj" -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Managed build failed.' }
}
