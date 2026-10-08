# TrayBridge

A standalone Windows 11 app that puts native notification areas on multiple taskbars and lets you choose which icons appear on each monitor. **Windhawk is not required or installed.** Selected upstream source is built into TrayBridge's own native helper.

Current target: Windows 11 25H2 x64, build **26200.9457**. This is an early, current-build implementation; other Windows builds and ARM64 have not been validated.

## Use

Run `TrayBridge.exe` from the extracted release folder. Its primary-tray icon opens **Manage trays**; its right-click menu also provides **Show all icons**, **Restore saved assignments**, **Disable**, and **Exit**.

Windows must show a taskbar on each target monitor. Enable **Show my taskbar on all displays** in Windows' Taskbar settings if a connected display has no taskbar.

1. Click **Enable**. First use downloads matching debug symbols directly from Microsoft; no Windhawk program, service or mod package is downloaded. Allow the preparation step to finish.
2. Check one or more connected monitors. Select a monitor to load its current rule into the editor.
3. Application icons are always clickable. Leave **All applications** checked to include every current and newly appearing icon. Uncheck any individual icon to switch to a custom selection while keeping the others selected. Checking **All applications** again selects everything and includes new icons automatically. The **Windows controls & indicators** tab works the same way for individual system icons; the group switches control Quick Settings, clock/notifications, and input/privacy surfaces.
4. Click **Apply settings** to copy the displayed rule to the checked monitors. Select **Edit global defaults** to change the rule inherited by monitors without an override. **Use global defaults** removes selected overrides.
5. Open **Show Hidden Icons** once after enabling to discover the current hidden icon catalog. Hidden icons remain in the native overflow popup, filtered for the monitor that opened it. Their assignment does not change Windows' promoted/hidden preference.

The same icon can appear on several monitors or only one. TrayBridge's management icon is exempt from filtering on the primary monitor. Unassigned/new application icons remain recoverable on primary. **Show all icons** temporarily reveals everything without deleting saved assignments; **Restore saved assignments** returns to them.

Rules are keyed by physical monitor paths rather than display numbers. Explicit icon assignments belonging to a disconnected monitor temporarily fall back to primary and return when it reconnects. Sign-in startup is optional and off by default. Closing the settings window leaves the controller running; use **Exit** to quit and restore Windows' native taskbars.

## How it works

The managed controller owns the UI, monitor detection, settings and lifetime. The native helper runs inside this user's Explorer and ShellHost processes. It shares Windows' live tray view models into secondary taskbar XAML islands, filters each view independently, and preserves the native application callbacks and upstream flyout/menu handling. It never edits the application's icon or removes that application's registration.

Application identity uses the registered icon GUID when available, otherwise the executable path plus icon UID on the validated current build. Private interfaces and instruction patterns are checked against matching module symbols. If a particular icon implementation is unsupported, its opaque Windows ID is retained; that icon may need reassignment if Windows recreates it. Concurrent instances of the same executable using the same UID currently share an assignment.

Settings and local inventories live in `%LOCALAPPDATA%\TrayBridge`. They contain local executable paths and tooltips and stay on the device. The app has no analytics. Internet access is used for Microsoft symbol preparation; cached operation is local.

Disabling restores the native properties/bindings changed by this app. A process lease also restores them if the controller exits unexpectedly. Hook trampolines and the helper remain dormant in the shell until the shell exits, avoiding unsafe unloading while a callback may be returning. Windows updates can change private taskbar interfaces: mandatory symbol/host validation failures leave cloning disabled.

## Validation

Verified on the current machine with two 1920×1080 taskbars:

- Five native application icons cloned onto both taskbars.
- Stoplight's running icon exclusively on secondary, with its unread/error icons on primary.
- Volume and network glyphs assigned independently to different monitors.
- Secondary overflow filtered to one assigned test icon, with a native click reaching its owning process.
- Disable/restore and a second enable/disable cycle without restarting Explorer.
- Global inheritance, independent bulk rules, disconnect/reconnect routing, disabled-rule handling and override reset checked with policy tests.

Battery, language/IME, and camera/microphone indicators use the shared system-stack path and become selectable when Windows exposes their glyphs. Hardware/session conditions for those indicators and physical monitor unplug/replug have not been exercised on this desktop. Flyout placement and some system-menu behavior remain sensitive to Windows changes; this release is a prerelease.

## Build from source

Requirements: Windows x64, .NET 10 SDK, Visual C++ x64 build tools and Windows SDK with C++/WinRT headers. The current native code also builds with VS 2019 Build Tools / SDK 10.0.19041.

```powershell
cd D:\traybridge
.\setup.ps1                 # optional project-local Microsoft .NET SDK
.\build.ps1 -Publish
.\dist\TrayBridge.exe
```

Use `-DotnetPath <path-to-dotnet.exe>` to select another installed SDK. `-NativeOnly` builds just the native DLL and symbol indexer. The published app is self-contained; the person running it does not need to install .NET or developer tools. Keep the native helper and symbol indexer alongside the app.

```powershell
.\dist\TrayBridge.exe --self-test
.\dist\TrayBridge.exe --prepare
.\dist\TrayBridge.exe --probe       # inventory without enabling cloning
.\dist\TrayBridge.exe --test-route  # temporary live routing test; restores afterward
```

Tests write reports under the local data directory. Run live tests with the normal controller exited. The fixture command `--fixture` adds a temporary icon for interaction checks and exits automatically after one minute.

See [LICENSE](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.md) for GPLv3 terms, upstream source revisions and retained MIT/BSD credits.
