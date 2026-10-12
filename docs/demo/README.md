# README demonstration

The GIF renders the current `ManagerForm`, `ModernControls`, and settings model with two sample monitors and sample icons. Taskbar strips are labeled illustrations; this is a UI walkthrough, not a recording of a live shell session. The harness never injects helpers, changes startup registration, or reads personal app settings.

From the repository root, regenerate the GIF and still image on Windows:

```powershell
$env:DOTNET_CLI_HOME = "$pwd/.tools/dotnet-home"
$env:NUGET_PACKAGES = "$pwd/.tools/packages"
.\.tools\dotnet\dotnet.exe run --project docs/demo/Demo.csproj -c Release -- docs/assets/traybridge-demo.gif artifacts/demo-data
```

The application uses real icon identities and native taskbar controls. Colored circles in the illustration stand in for those icons.
