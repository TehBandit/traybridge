# Release procedure

Version 0.1.5 is an unsigned GitHub preview, distributed as a self-contained ZIP with no installer. Supported target: Windows 11 25H2 x64 build 26200.9457, native taskbars, writable local NTFS storage. Other builds/architectures and restricted PowerShell policies are unsupported. The owner chose to assume the full Windows lifecycle works without VM testing; do not describe it as VM-verified.

## Local checks

```powershell
.\setup.ps1
.\scripts\AuditDependencies.ps1
.\build.ps1 -Publish -Unsigned -TestNativeRestoration -OutputDirectory .\artifacts\development
.\.tools\dotnet\dotnet.exe run --project tests\TrayBridge.Uninstall.Tests.csproj -c Release
.\tests\ReleaseBuild.Tests.ps1
.\tests\ReleaseSigning.Tests.ps1
.\tests\UnsignedPreview.Tests.ps1 -PublishedDirectory .\artifacts\development
.\scripts\NewReleasePackage.ps1 -PublishedDirectory .\artifacts\development -OutputDirectory .\artifacts\development-package -UnsignedDevelopment
.\tests\ReleasePackage.Tests.ps1 -PackageDirectory .\artifacts\development-package -PublishedDirectory .\artifacts\development
```

The uninstall export fixture uses `artifacts/uninstall-release/TrayBridge.Native.dll`, falling back to `dist/TrayBridge.Native.dll`. On a fresh checkout, publish to `dist` or copy the newly built helper to that fixture location before running the suite. Fixtures use disposable project-local paths and a dedicated test registry namespace. They do not inject into the shell or uninstall the real app. An unavailable dependency feed fails review.

## Public unsigned preview

1. Enable GitHub private vulnerability reporting. Review usage, compatibility, uninstall instructions, and release notes.
2. Regenerate/inspect the README GIF if the UI changed; see [demo/README.md](demo/README.md). Its sample data and illustrated taskbars must be labeled.
3. Commit all reviewed source, tests, docs, and media. Create the exact version tag (`v0.1.5` for this release). Existing older tags cannot describe these changes. Push the source and tag without force.
4. Run the dependency audit and rebuild from the unchanged tagged checkout into new folders:

```powershell
.\scripts\AuditDependencies.ps1
.\build.ps1 -Publish -Unsigned -TestNativeRestoration -OutputDirectory .\artifacts\preview-v0.1.5
.\scripts\NewReleasePackage.ps1 -PublishedDirectory .\artifacts\preview-v0.1.5 -OutputDirectory .\artifacts\preview-package -UnsignedPreview
.\tests\ReleasePackage.Tests.ps1 -PackageDirectory .\artifacts\preview-package -PublishedDirectory .\artifacts\preview-v0.1.5
```

`-UnsignedPreview` keeps the clean exact-tag, source revision/fingerprint, embedded file hashes, project version, and recent dependency checks. It skips certificate verification and explicitly records `development: false`, `prerelease: true`, and `signing: unsigned`. `-UnsignedDevelopment` creates development snapshots and cannot be combined with preview mode. An unsigned preview cannot pass the separate signed-public-package requirements.

5. Create a GitHub **prerelease** attached to that existing tag. Attach both ZIPs, `SHA256SUMS.txt`, `release.json`, and the demo GIF; use `docs/releases/0.1.5.md` as release notes. Stage as a draft until all assets are uploaded, then publish. Do not mark this preview as latest/stable.
6. Verify the download URL, source/tag/manifest match, checksum, README image, and private reporting form. Keep license notices and corresponding source. Link the version-specific application ZIP, not GitHub's automatic source ZIP. Unsigned ZIPs can still trigger Windows/browser warnings or device-policy blocks.

The manual **Publish unsigned preview** workflow performs audit, build, tests, exact-tag packaging, and GitHub prerelease publication on `windows-2022`; it needs no signing certificate or self-hosted runner. Run it with an existing version tag. The development verification workflow checks pushes, PRs, and weekly dependency reviews without publishing.

Use [announcement.md](announcement.md) and the GIF/still image for promotion. Community posting is separate; use the owner's account and follow the chosen community's rules.

## Optional signed public package

Obtain a publicly trusted code-signing certificate/provider, configure private-key access in the Windows `My` store, and identify the provider's RFC 3161 timestamp endpoint. Keep keys/passwords outside the repository. Microsoft Azure Artifact Signing uses a different cloud integration and needs an additional project signing backend; it cannot be passed as a Windows-store thumbprint.

After committing/tagging the final signed release source, build into a new folder:

```powershell
.\build.ps1 -Publish -TestNativeRestoration -OutputDirectory .\artifacts\signed-vNEXT `
  -CertificateThumbprint '<provider-backed code-signing thumbprint>' `
  -TimestampServer '<provider RFC 3161 endpoint>'
.\scripts\NewReleasePackage.ps1 -PublishedDirectory .\artifacts\signed-vNEXT -OutputDirectory .\artifacts\signed-package
```

The signed build audits dependencies, signs/verifies native binaries before hashing, embeds final hashes and the source fingerprint in the controller, then signs/verifies the controller. Source changes during compilation/publication stop the build. Do not distribute failed staging folders or edit/re-sign helpers afterward.

The **Stage signed release** workflow stages on a trusted self-hosted Windows runner labeled `traybridge-signing`. Configure the `traybridge-release` environment's `TRAYBRIDGE_SIGN_CERT_THUMBPRINT` and `TRAYBRIDGE_TIMESTAMP_SERVER` variables and install the pinned toolchain/provider there. Publication is separate. Verify actual certificate-backed output and browser behavior; mock tests do not establish those results.

## Maintenance

The repository maintainer owns release/security maintenance. Review weekly dependency failures before releasing, update runtime/toolchain pins for security fixes, and rebuild self-contained binaries. Validate Windows versions before expanding support. See `SECURITY.md` for reporting.
