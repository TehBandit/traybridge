# Release readiness

Updated 2026-10-12 for the unsigned 0.1.5 preview. The original review identified restoration, recovery, compatibility, signing integrity, and distribution gaps.

The release path is an unsigned, installer-free GitHub prerelease. Public previews require exact committed/tagged source, a matching binary inventory/source fingerprint, and a recent dependency review. Signing is optional for previews; the separate signed-release path retains signature/timestamp checks. The owner declined VM testing and asked to assume full Windows lifecycle behavior works. That assumption is recorded; no real reboot/sign-out verification is claimed.

## Findings addressed

| Original finding | Current behavior |
| --- | --- |
| Swallowed restoration failures | Native restoration distinguishes no work, change, and failure. Any failure keeps the acknowledgment false. Failed property/clock records and needed caches are retained. Disable/uninstall use already-loaded helpers; failed restoration blocks removal and re-enabling until the shell exits. |
| Prefix-only cleanup entries blocked startup | DPAPI-protected recovery receipts identify the signed plan and exact script/workspace identities. Startup offers inspect/retry/stop/open. Unknown/replaced entries are preserved and do not block opening. Stop rechecks the current receipt and command. |
| Missing/blocked cleanup had no recovery | Compilation and deferred command length are checked before taskbar/startup changes. Initial launch and manual retry verify the actual child process's readiness and show host errors. Retry waits for the controller to exit before taking the lifecycle gate. |
| Unenforced compatibility | Windows 11 25H2 x64 build 26200.9457 is checked before symbol preparation and remote invocation. Stop uses already-loaded helpers and remains available after updates. Other builds and ARM64 are refused. |
| Signing invalidated uninstall hashes | Native binaries are signed/verified before hashing; the controller is signed afterward. Final signatures, timestamps, and hashes are checked. Direct publication rejects missing/unprepared/stale inventory. |
| Incomplete source/package identity | Version is 0.1.5. The build records the source fingerprint/revision and rejects source changes during compilation. Public packaging verifies inventory/source/version/tag and creates binary/source ZIPs, checksums, and a manifest. Unsigned previews retain these checks and record unsigned/prerelease status; signed releases also require signatures. |
| Incomplete dependency review | Online NuGet audit passed. Runtime 10.0.12 matched current Microsoft metadata. Public advisory APIs reported no published advisories for MinHook, Windhawk, or Windhawk mods. Weekly checks and a release audit are prepared. |

## Verification and limits

- Managed compilation passes.
- 74 isolated uninstall/recovery checks cover file/registry replacement, signed approval tampering, valid/stopped plans, missing/edited scripts, DPAPI tampering, long commands, actual PowerShell compilation/readiness, and worker progress/deferred-cleanup fixtures.
- 27 native restoration outcome sequences pass, including failures followed by changes and optional no-ops.
- 27 signing checks pass using simulated signatures. No release certificate was installed.
- Four publication-metadata guard checks pass.
- Development publication and archive verification are included in the release procedure. Final package evidence is recorded under `artifacts`.
- Dependency evidence: `artifacts/dependency-review.json`. Public advisories and NuGet audit do not certify the adapted native code or prove absence of vulnerabilities.

No real uninstall of the active app, shell injection, forced shell restart, sign-out, or reboot was performed for this release preparation. Native tests verify outcome aggregation; they do not inject failures into Windows XAML. Full lifecycle behavior is assumed at the owner's request. The GIF renders the actual management UI using sample data and illustrated taskbars; it is not a live shell recording. Actual signed-package/browser checks remain pending a certificate. GitHub workflow results are available in the repository's Actions tab once publication runs them.

## Release procedure

- Commit the reviewed changes, create exact tag `v0.1.5`, audit dependencies, rebuild from it, and package with `-UnsignedPreview`.
- Enable GitHub private vulnerability reporting, as selected by the owner.
- Publish the unsigned prerelease notes, binary/source ZIPs, checksums, manifest, and GIF with the narrow compatibility statement.
- Certificate-backed signing remains optional future work; it is not a blocker for this unsigned preview.

See [releasing.md](releasing.md), [0.1.5 notes](releases/0.1.5.md), and [SECURITY.md](../SECURITY.md).

Dependency-review sources: [Microsoft .NET release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json), [MinHook advisories](https://github.com/TsudaKageyu/minhook/security/advisories), [Windhawk advisories](https://github.com/ramensoftware/windhawk/security/advisories), [Windhawk mods advisories](https://github.com/ramensoftware/windhawk-mods/security/advisories).
