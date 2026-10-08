param([switch]$Publish, [switch]$NativeOnly, [string]$DotnetPath)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$vsPath = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) { throw 'Visual C++ build tools and Windows SDK are required.' }
$devCmd = Join-Path $vsPath 'Common7\Tools\VsDevCmd.bat'
$toolDir = Join-Path $projectRoot '.tools\native'
$distDir = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $toolDir,$distDir -Force | Out-Null
$cmdFile = Join-Path $toolDir 'compile.cmd'
$nativePath = Join-Path $projectRoot 'Native'
$commandText = @"
@echo off
call "$devCmd" -arch=x64 -host_arch=x64 >nul
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
if ($NativeOnly) { return }
$sdk = if ($DotnetPath) { (Resolve-Path -LiteralPath $DotnetPath).Path } elseif (Test-Path "$projectRoot\.tools\dotnet\dotnet.exe") { "$projectRoot\.tools\dotnet\dotnet.exe" } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = "$projectRoot\.tools\dotnet-home"
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = "$projectRoot\.tools\packages" }
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if ($Publish) { & $sdk publish "$projectRoot\TrayBridge\TrayBridge.csproj" -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' -o $distDir }
else { & $sdk build "$projectRoot\TrayBridge\TrayBridge.csproj" -c Release }
if ($LASTEXITCODE -ne 0) { throw 'Managed build failed.' }
if ($Publish) {
    Copy-Item -LiteralPath "$projectRoot\LICENSE","$projectRoot\THIRD-PARTY-NOTICES.md","$projectRoot\README.md" -Destination $distDir -Force
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
}
