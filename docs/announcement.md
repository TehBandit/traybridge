# TrayBridge 0.1.5 preview announcement

Ready-to-use text for a release discussion or a Windows customization/multi-monitor community post. Check the community's promotion rules before posting.

## Suggested title

TrayBridge: native Windows 11 tray icons on every monitor, with per-monitor selection

## Suggested post

I built TrayBridge to put Windows' native notification area on secondary taskbars and choose which application icons and Windows controls appear on each monitor. You can show the same icons everywhere or keep different selections on different screens. Icons retain their normal click and menu behavior.

The 0.1.5 preview is a standalone download: extract the ZIP, run `TrayBridge.exe`, enable it, choose a display, select icons, and click Apply. No installer, .NET installation, Node.js, or Windhawk installation is needed.

This preview currently supports **Windows 11 25H2 x64 build 26200.9457 only**. It is unsigned, so Windows/browser warnings may appear and some device policies can block it. It downloads matching symbols from Microsoft on first enable. Startup is optional and off by default; there is no analytics. Built-in uninstall restores Windows' taskbars and removes verified app files/settings; a restart may be needed.

- [Demo GIF](https://raw.githubusercontent.com/TehBandit/traybridge/v0.1.5/docs/assets/traybridge-demo.gif): actual settings UI, sample data, illustrated taskbars.
- [Download and release notes](https://github.com/TehBandit/traybridge/releases/tag/v0.1.5)
- [Source, usage, compatibility, and uninstall](https://github.com/TehBandit/traybridge)
- [Feedback and bug reports](https://github.com/TehBandit/traybridge/issues)

Source, checksums, and exact commit/tag provenance accompany the download. Feedback from people using multiple monitors on the supported Windows build is welcome.

## Sharing assets

Use `docs/assets/traybridge-demo.gif` for the walkthrough and `docs/assets/traybridge-demo.png` for the still image. The README and release notes both include the GIF. Link the version-specific release rather than GitHub's automatic source archive or a latest-release URL, which can omit prereleases.
