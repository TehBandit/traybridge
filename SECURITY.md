# Security reports and maintenance

Report vulnerabilities privately through [GitHub private vulnerability reporting](https://github.com/TehBandit/traybridge/security/advisories/new). Do not post sensitive vulnerability details or diagnostic inventories in public issues.

Use [GitHub Issues](https://github.com/TehBandit/traybridge/issues) for ordinary bugs. Reports and inventories can contain local executable paths, tooltips, and application names; redact personal information first.

The repository maintainer reviews weekly dependency-check failures and incoming security reports. Only the current release on its explicitly supported Windows build is supported. Windows updates require compatibility review; the app refuses untested builds before injection. Each .NET runtime security update requires a new TrayBridge build because the runtime is bundled.

`scripts/AuditDependencies.ps1` checks Microsoft's latest supported .NET 10 runtime metadata, published advisories for MinHook/Windhawk/Windhawk mods, and NuGet audit data. Unavailable data or an outdated runtime fails the release check. Public advisory feeds do not cover every problem in this adapted native code; a passing review is not a security certification.

The scheduled verification workflow runs these checks weekly. A release must use a review completed within 24 hours of packaging. Review new upstream advisories for applicability to the revisions in `THIRD-PARTY-NOTICES.md` before changing the audit policy.
