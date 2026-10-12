# Safe uninstall plan

Status: implemented in source. Validation deletes only disposable fixture files; it does not uninstall the developer's running copy.

## Implementation and verification notes

The cleanup host uses built-in Windows PowerShell and compiles the app's embedded C# cleanup source in memory. This avoids shipping a mapped cleanup executable or extracting another .NET bundle. The source script and signed state live in one unique `%LOCALAPPDATA%\TrayBridge.Uninstall.<operation-id>` directory and remove themselves through the same verified-handle operations after success. Windows policies that prevent the host from starting stop uninstall before program-file removal.

The controller signs the immutable removal plan with a per-operation RSA key; the cleanup source contains only the public key. Deferred runs verify the original signature and file/root identities. The private key is discarded, so editable retry metadata cannot authorize new files. A matching current-user resume registration is verified before committing removal and is removed after success or a fatal cleanup error. The normal controller does not start while a cleanup registration is pending.

Managed writers and native inventory/status writers record file identities in bounded ownership records. Legacy files without records are preserved and listed. Safe deletion currently supports local NTFS storage; other filesystems and redirected paths are refused. Native uninstall resolves the acknowledgment export from the loaded image rather than using a possibly changed DLL on disk, and does not inject another helper.

Automated checks are in `tests/TrayBridge.Uninstall.Tests.csproj`. They cover deletion boundaries, changed contents/IDs, rename/write exclusion, junctions, hard links, read-only files, signed-plan tampering, empty-directory guards, loaded-helper exports, and the actual cleanup progress window. Worker lifecycle tests substitute disposable workspace roots, an isolated lifecycle mutex, and a unique test-only registry namespace. They exercise a locked-file pending result, a subsequent unlocked retry, script/state self-removal, preserved file and registry sentinels, and restart guidance.

Actual shell restoration and Windows sign-out/reboot completion still require disposable-VM validation. Fixture retries simulate release of a file lock; they do not force or test a real reboot.

Build a published copy first, then run `dotnet run --project tests/TrayBridge.Uninstall.Tests.csproj -c Release`. The tests use a unique test registry namespace, never the real Windows startup key. Run them outside a registry-restricted sandbox; the fixture removes its own values and key afterward. Native DLL loading in the export-resolution test uses `DONT_RESOLVE_DLL_REFERENCES` and does not enable taskbar management or inject into the shell.

## Intended behavior

Add **Settings > Uninstall TrayBridge...**. It removes the running published copy and the current user's verified TrayBridge data, restores Windows taskbars, and removes TrayBridge's matching sign-in registration. The confirmation shows resolved locations, what will be removed, and anything that must be preserved or cleaned up later. Cancel is the default button. Saved assignments, global configuration, and appearance preferences are explicitly included in the removal description.

Safety takes priority over completeness: an uncertain or replaced file is preserved and reported. Never present a partial removal as a completed uninstall. The operation does not search the device for other copies, release archives, user-created shortcuts, or other users' data.

## Findings from the current code

- This is a portable application, run from an extracted release folder. `setup.ps1` installs a project-local development SDK; it is not an end-user app installer.
- `build.ps1` publishes a self-contained .NET executable, a native helper, a symbol indexer, a PDB, and documentation/license files.
- `NativeHost.DataDirectory` is `%LOCALAPPDATA%\TrayBridge`. The app writes settings, inventories, reports, logs, Microsoft symbol caches, and fingerprinted native DLL copies there.
- `TrayApplication.SavePreferences()` writes one value named `TrayBridge` under the current user's Windows `Run` key. There is no service, driver, scheduled task, or installed shared runtime to remove in the inspected installation/startup code.
- `NativeHost.Invoke()` copies the helper into the data directory before loading it into Explorer/ShellHost. Stopping restores taskbars but intentionally leaves the DLL and hook trampolines resident until those shell processes exit (`Native/Bridge.cpp`).
- The existing disable method catches errors without returning a success result. Stop ignores ShellHost return codes; native stop returning zero means the worker stopped, not necessarily that restoration succeeded. Uninstall needs stronger acknowledgments.
- The normal entry point creates the data directory before dispatching commands. Cleanup must bypass normal startup so it cannot recreate the files it just removed.
- The current mutex is session-local, while the data directory is shared by that user's sessions. Concurrent controllers, tools, and copies must be accounted for.

## Deletion boundaries

| Location/item | Allowed scope | Ownership check |
| --- | --- | --- |
| Running release directory | Exact published files only: executable, native DLL, symbol indexer, optional PDB, and shipped documentation/licenses | Build-defined file list, trusted release content hashes, and the running executable's file identity |
| `%LOCALAPPDATA%\TrayBridge` | Verified app-created settings, inventories, statuses, reports, logs, caches, and their recorded temporary/backup files | Bounded ownership records and file identities; narrowly validated migration rules for existing data |
| .NET extraction cache | Only individually verified files from this app's exact bundle directories | Record actual loaded runtime locations and compare to this release's bundle metadata |
| Current-user `Run` registration | The value named `TrayBridge` only if its command targets this copy | Parse executable target and compare to the resolved executable identity |
| Temporary uninstall workspace | Exact worker/state files created for this uninstall | Unique operation ID, ownership records, file identities, and restricted current-user access |
| Deferred cleanup registration, if needed | The unique TrayBridge uninstall value created by this operation | Exact name and expected command; preserve a replaced value |

Unknown files remain untouched, even inside an app-named directory. Remove directories only with an empty-directory operation after verified contents have been removed. Never recursively delete a release folder, the data root, a parent directory, or a cache root.

Generic names such as `README.md` and `LICENSE` require a matching trusted release hash; the filename alone is insufficient. An edited or replaced copy is preserved. The running executable is identified from the process, not inferred from the working directory or `NativeHost.Distribution`'s development fallback.

The build-generated inventory must be authenticated or anchored in trusted compiled release metadata. A writable manifest may describe candidates but cannot authorize new paths. App-data ownership records may contain only fixed categories and bounded relative names; they cannot add arbitrary absolute targets. Record dynamic cache files when created. For legacy files without adequate ownership evidence, preserve and list them instead of assuming an entire folder belongs to the app.

Never touch source trees, `.git`, `.tools`, development SDKs, NuGet caches, Windows binaries, shared .NET installations, other applications, unrelated registry values, or other users' profiles. Development/debug launches must refuse program-file removal.

## Path and file safety

1. Derive roots independently from the running executable and Windows known-folder APIs. Reject empty, relative, drive-relative, network, device, alternate-stream, traversal, wildcard, and unsupported paths. Reject a release root equal to a drive root, profile root, shared known-folder root, or system directory.
2. Validate root and ancestor identities, and inspect every target without following reparse points. Refuse linked/junction targets or unsafe ancestors; do not traverse them. If legitimate redirected storage cannot be verified safely, explain the unsupported location and stop that part of removal.
3. Enforce containment by path components and resolved handles, never by a simple string prefix. The cleanup worker repeats validation; it does not trust the UI's serialized plan.
4. Hold verified directory/file handles through destructive operations, prevent rename/replacement where supported, and delete the verified object by handle. A check followed by an unrelated path-based delete is insufficient protection against a swap. Defer deletion if the filesystem cannot provide the required identity guarantees.
5. Preserve replaced files. Refuse multiply linked targets where ownership is ambiguous. Do not change permissions, take ownership, clear attributes, truncate content, or force-unlock files to make removal succeed.
6. Use structured process arguments and native file operations. Do not construct recursive shell deletion commands. Any finalizer must obey the same exact-file and identity rules.

## Uninstall sequence

1. **Preview without mutation.** Build an immutable removal plan and show the locations, data loss, preserved items, and likely locked helper files. Disable uninstall while enable, symbol preparation, or another operation is busy. Cancel and closing this dialog change nothing.
2. **Acquire exclusive ownership.** After confirmation, acquire a current-user uninstall gate shared by normal starts, diagnostic commands, symbol preparation, and cleanup. Stop timers and prevent UI callbacks from saving preferences or re-enabling the app. Detect other same-user controllers/sessions using the shared data. If they cannot cooperate, stop before destructive work; do not kill them.
3. **Prepare recovery and cleanup.** Create the unique restricted workspace and verify the independent cleanup worker. Persist the bounded operation state and handshake with the worker before closing the controller. Failures here leave the program files intact.
4. **Restore Windows.** Persist disabled native configuration, then stop every relevant already-loaded TrayBridge helper in this user's supported shell processes, including older recorded helper versions. The uninstall stop path must not inject a new helper solely to stop it. Obtain explicit restoration and worker-quiescence results for each live host. A disappeared host has no remaining active in-memory state; a live host with failed/unconfirmed restoration blocks file removal. Keep the UI open with a retryable error.
5. **Remove matching startup registration.** Remove only the verified value for this copy. A value targeting another copy is preserved and reported. Verify the result. An access error leaves the app disabled and reports failure before program-file deletion.
6. **Exit and clean.** Transfer the validated plan to the worker, close the controller, and wait on its actual process handle rather than a delay or a PID alone. Delete verified unlocked app files and caches individually, then remove verified empty directories. Keep operation state until completion is verified.
7. **Handle locked files.** Stop after bounded retries. Never unload callback code unsafely, terminate/restart Explorer or ShellHost, force sign-out, elevate privileges, or use system-wide reboot deletion. Clearly report pending cleanup and allow the user to sign out or restart normally. A disclosed, uniquely named current-user cleanup registration may resume the independent worker at next sign-in. It must revalidate every remaining target and must never restart TrayBridge. Cap retries and report persistent failures.
8. **Finalize and verify.** Remove the cleanup registration and operation state after verified success. The worker and any finalizer must remove their own exact artifacts after their handles close. Show complete, pending sign-out, or partial/failed results with exact remaining paths and reasons. Do not leave a permanent uninstall log on successful removal. Keep results visible in the cleanup UI long enough to review/copy them.

Deletion is not transactional: after program-file removal begins, do not promise rollback. Interrupted operations resume idempotently from bounded state. Missing targets are already removed; identity changes are preserved and reported. The worker must never broaden its scope to resolve an error.

## Windows constraints to verify in the prototype

Microsoft documents that deletion can fail for open or memory-mapped files. This agrees with TrayBridge's intentional resident-helper lifecycle: immediate removal of every file cannot be guaranteed while the shell retains those DLLs. See [DeleteFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-deletefilew).

Reparse points need explicit handling, and handle-based deletion should be evaluated rather than relying on recursive filesystem APIs. See [reparse points and file operations](https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-points-and-file-operations) and [SetFileInformationByHandle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle).

The current publish flags extract bundled native runtime files before managed startup, normally under `%TEMP%/.net` unless an extraction base is configured. Inventory the real locations; never delete the shared `.net` root or assume its app-named children are exclusively owned. Prefer a small statically linked native cleanup worker to avoid creating another .NET extraction cache during removal. See [.NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).

The deferred cleanup mechanism and worker self-removal must be proven under a standard user account before promising complete removal. Inspect [Run/RunOnce semantics](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys), test registration disappearance and interrupted execution, and ensure path replacement cannot turn a later retry into deletion of a different file. If the prototype cannot safely finish, ship an explicit partial-result path instead of claiming success.

## Implementation order and acceptance tests

Implement the file-plan validator and read-only preview first, then ownership tracking/release metadata, then restoration acknowledgments and concurrency gates, then the cleanup worker and deferred lifecycle, and finally the Settings UI. Likely integration points are `ManagerForm.cs`, `TrayApplication.cs`, `Program.cs`, `NativeHost.cs`, `Native/Bridge.cpp`, and `build.ps1`, with dedicated uninstall modules rather than deletion logic embedded in event handlers.

Use disposable directories and an isolated registry namespace for automated tests. Live uninstall validation belongs in a disposable Windows VM using a published copy, never this repository or the developer's real data.

- Cancel produces no changes; preview performs no writes.
- Unknown neighbors, edited license/readme files, and sentinel files inside app-data/cache folders survive byte-for-byte. Unrelated registry values retain their exact types and contents.
- Root/prefix confusion, malformed manifests, traversal, alternate streams, links, junctions, hard links, root replacement, and swaps between validation and deletion fail safely. Sentinels outside allowed roots remain unchanged.
- Tests cover spaces, Unicode, apostrophes, shell metacharacters, long paths, missing files, read-only files, access denial, and unsupported filesystems.
- Active, inactive, partly initialized, and previously loaded helper versions restore correctly. ShellHost failure, taskbar-thread hangs, native restoration errors, and worker-launch failure prevent premature deletion.
- Another session/copy, diagnostic command, startup race, PID reuse, interrupted controller/worker, and replacement files cannot escape the operation's scope.
- Locked DLLs produce a pending result; normal sign-out/restart completes cleanup without forced shell termination. Standard-user permissions suffice for supported locations.
- The worker, finalizer, extraction artifacts, deferred startup value, and operation state are absent after success; running a cleanup retry cannot recreate app data or launch the controller.
- No unverified item remains silently: completion is reported only when every owned planned artifact is gone. Preserved uncertain files and persistent failures are explicitly listed.

Release gate: all boundary, concurrency, failure, and full-lifecycle tests pass, and a separate review checks every destructive call against this plan.
