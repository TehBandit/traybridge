# TrayBridge

TrayBridge puts Windows' native notification area on your other monitors and lets you choose which application icons and Windows controls appear on each taskbar. Keep chat on one screen, music on another, or show the same icons everywhere.

Open TrayBridge, enable it, choose a display, select its icons, and click **Apply**. Application icons keep their normal click and menu behavior. Windhawk is not required or installed.

[**Download the Windows x64 ZIP — 0.1.5 preview**](https://github.com/TehBandit/traybridge/releases/download/v0.1.5/TrayBridge-0.1.5-win-x64.zip) · [Release notes and checksums](https://github.com/TehBandit/traybridge/releases/tag/v0.1.5) · [Report a bug](https://github.com/TehBandit/traybridge/issues)

**Supported: Windows 11 25H2 x64, OS build 26200.9457 only.** This is an unsigned preview. Other Windows builds and ARM64 are refused before taskbar changes. Run `winver` to check your OS build; a Windows update may require a newer compatible TrayBridge release.

![TrayBridge walkthrough: enable, choose Display 2, select application icons, apply, and save the configuration](https://raw.githubusercontent.com/TehBandit/traybridge/v0.1.5/docs/assets/traybridge-demo.gif)

*The walkthrough shows the actual settings UI with sample monitors and icons. The taskbars beneath it are illustrations.*

## Use TrayBridge

1. In Windows **Settings → Personalization → Taskbar → Taskbar behaviors**, enable **Show my taskbar on all displays**.
2. Run `TrayBridge.exe` and click **Enable**. First use downloads matching Windows debug symbols directly from Microsoft; allow preparation to finish.
3. Click a display's name to view its settings. Mark the checkbox beside each display that should receive **Apply**. To customize one monitor, mark only that monitor.
4. In **Applications**, leave **All applications** checked to show current and future icons, or uncheck individual icons to customize the selection. Use **Windows controls** to choose system icons, Quick Settings, the clock, and input/privacy indicators.
5. Click **Apply**. Repeat for other monitors to give each its own selection.
6. Optional: click **Set global config** to save the complete applied setup. **Revert to global config** restores it later.

Click TrayBridge's icon on the primary taskbar to reopen **Manage trays**. Closing the management window leaves the app running. Use the tray menu's **Exit** to quit and restore Windows' taskbars. **Settings → Start at sign-in** is optional and off by default.

**Show all icons** temporarily reveals icons without deleting saved assignments. **Restore saved assignments** returns to your rules. **Reset to Windows** disables tray cloning and clears assignments while keeping your saved global configuration and preferences. Open **Show Hidden Icons** once after enabling to discover overflow icons.

See [detailed usage](https://github.com/TehBandit/traybridge/blob/v0.1.5/docs/usage.md) for bulk selection, searches, themes, disconnected monitors, and saved configurations.

## Download and install

1. [Download `TrayBridge-0.1.5-win-x64.zip`](https://github.com/TehBandit/traybridge/releases/download/v0.1.5/TrayBridge-0.1.5-win-x64.zip). The release page's **Source code** downloads are for building the app, not running it.
2. Right-click the ZIP and select **Extract All**. Keep the complete release together in a dedicated, writable folder on a local NTFS drive, such as `%LOCALAPPDATA%\Programs\TrayBridge`.
3. Run `TrayBridge.exe` from that folder. Keep `TrayBridge.Native.dll`, `TrayBridge.Symbols.exe`, and the included notices alongside it.

There is no installer wizard. The published app includes .NET; users do not need Node.js, `npx`, .NET, developer tools, or Windhawk. Windows PowerShell is used by the app's uninstall and recovery process. Restricted PowerShell policies and alternative taskbars are unsupported.

**Unsigned preview:** Windows or your browser may warn that the download is uncommon or the publisher is unknown. Download only from this repository's release page and inspect the source/checksums if needed. Some device policies and Smart App Control configurations can block execution. This release does not ask you to disable security protections. See [Microsoft's explanation of download reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

The release includes `SHA256SUMS.txt`, matching source, and `release.json` identifying the exact tag, source commit, and unsigned status. Checksums detect file changes; they are not a publisher certificate. To check the downloaded ZIP in PowerShell:

```powershell
Get-FileHash .\TrayBridge-0.1.5-win-x64.zip -Algorithm SHA256
```

Compare the result with its line in `SHA256SUMS.txt` on the release page.

## Upgrade or roll back

Exit the old controller and keep its folder as a rollback copy. Extract the new release into a separate folder. If an older helper remains resident or restoration cannot be confirmed, sign out or restart before starting the new version. Avoid running different versions together.

Settings are shared between copies under `%LOCALAPPDATA%\TrayBridge`. Back them up before upgrading; older versions may not understand newer settings. To roll back, exit the new controller, sign out/restart if necessary, and run the preserved older version on its supported Windows build.

## Uninstall

Open **Manage trays → Settings → Uninstall TrayBridge…**. Review the files/settings listed and confirm removal. The app restores Windows' taskbars and removes the running published copy and its individually verified local data, including assignments and saved global configuration.

Unknown files, edited documentation, other copies, and older files without ownership records are preserved. Taskbar restoration failures stop removal and explain the next step. **A restart may be required to finish uninstallation.** If files remain in use, cleanup retries at the next sign-in up to three times; the recovery screen lets you inspect, retry, or stop a verified cleanup registration.

To stop using TrayBridge while keeping your setup, choose **Disable** or **Exit** from its tray menu. Merely deleting the extracted folder does not remove saved settings or an optional sign-in registration; use the built-in uninstall flow for full removal.

## Compatibility, privacy, and support

- Supported target: **Windows 11 25H2 x64 build 26200.9457**, native Windows taskbars, writable local NTFS storage. Other patch levels and ARM64 are unsupported.
- The native helper runs inside this user's Explorer and ShellHost processes. Windows updates can change the private interfaces it uses. Symbol/host validation failures leave cloning disabled.
- Settings, icon inventories, and diagnostics stay under `%LOCALAPPDATA%\TrayBridge`; the app has no analytics. Internet access is used for Microsoft symbol preparation. Cached operation is local.
- Report ordinary bugs through [GitHub Issues](https://github.com/TehBandit/traybridge/issues). Include your Windows build and TrayBridge version, and redact local paths, tooltips, and application names from logs.
- Report vulnerabilities through [GitHub private vulnerability reporting](https://github.com/TehBandit/traybridge/security/advisories/new). See [SECURITY.md](https://github.com/TehBandit/traybridge/blob/v0.1.5/SECURITY.md).

This is a preview. Isolated checks cover restoration outcomes, uninstall/recovery, release metadata, and packaging. A complete real sign-out/reboot/uninstall lifecycle was not tested for this release. See [release notes](https://github.com/TehBandit/traybridge/blob/v0.1.5/docs/releases/0.1.5.md) and [verification details](https://github.com/TehBandit/traybridge/blob/v0.1.5/docs/production-readiness.md).

## Build from source

The toolchain is pinned in `global.json` and `release-toolchain.json`: .NET SDK 10.0.401, runtime 10.0.12, Visual C++ 14.29.30133, and Windows SDK 10.0.19041.0 with C++/WinRT headers. Install the native build tools, then run from the repository root:

```powershell
.\setup.ps1
.\build.ps1 -Publish -Unsigned -TestNativeRestoration
.\dist\TrayBridge.exe
```

`setup.ps1` installs the project-local .NET SDK. `-DotnetPath` selects another installed SDK; `-NativeOnly` builds just the native helper and symbol indexer; `-OutputDirectory` stages another folder. Use `build.ps1` to publish so verified uninstall hashes and cleanup code are embedded in the controller.

Public unsigned previews use `scripts/NewReleasePackage.ps1 -UnsignedPreview`: it requires a clean exact version tag, matching source/build/inventory, and a dependency review from the last 24 hours. `-UnsignedDevelopment` produces explicitly labeled development snapshots. Signed releases remain supported separately. See the [release procedure](https://github.com/TehBandit/traybridge/blob/v0.1.5/docs/releasing.md).

## License and credits

TrayBridge is distributed under GPLv3. Selected upstream source is built into its native helper; see [LICENSE](https://github.com/TehBandit/traybridge/blob/v0.1.5/LICENSE) and [third-party notices](https://github.com/TehBandit/traybridge/blob/v0.1.5/THIRD-PARTY-NOTICES.md) for source revisions and retained credits. Matching source and notices accompany each release.
