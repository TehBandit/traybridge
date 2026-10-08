$ErrorActionPreference = 'Stop'
$toolsPath = Join-Path $PSScriptRoot '.tools'
New-Item -ItemType Directory -Path $toolsPath -Force | Out-Null
$installerPath = Join-Path $toolsPath 'dotnet-install.ps1'
Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installerPath
& $installerPath -Channel '10.0' -InstallDir (Join-Path $toolsPath 'dotnet') -NoPath
if ($LASTEXITCODE -ne 0) { throw 'The Microsoft .NET SDK installation failed.' }
Write-Host 'The project-local .NET 10 SDK is ready. Visual C++ build tools and Windows SDK are also required.'
