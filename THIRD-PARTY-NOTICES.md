# Third-party notices and source provenance

TrayBridge is distributed under GPL-3.0-only. See `LICENSE`. This is a focused standalone derivative, with its own controller, UI, settings, symbol indexer and process-scoped compatibility host. It does not contain Windhawk's installer, service, marketplace or desktop application.

| Component | Upstream source | Revision | License |
| --- | --- | --- | --- |
| Hook-host adaptation | [Windhawk engine/mod.cpp](https://github.com/ramensoftware/windhawk/blob/aef1a9f1e77f30c8fd96c5dd29373c38181486bf/src/windhawk/engine/mod.cpp) | `aef1a9f1e77f30c8fd96c5dd29373c38181486bf` | GPLv3 |
| Taskbar cloning and native interaction | [taskbar-multi-tray](https://github.com/ramensoftware/windhawk-mods/blob/ddcd4c51c971b3e98eea12d066e8426cc38bf3df/mods/taskbar-multi-tray.wh.cpp) | v1.3.0, `ddcd4c51c971b3e98eea12d066e8426cc38bf3df` | MIT, EDM115; original author credit retained, license in `Native/vendor/TASKBAR-MULTI-TRAY-LICENSE.txt` |
| Overflow discovery and app-name lookup patterns | [system-tray-folders](https://github.com/ramensoftware/windhawk-mods/blob/ddcd4c51c971b3e98eea12d066e8426cc38bf3df/mods/system-tray-folders.wh.cpp) | v1.0.0, same mods revision | GPL-3.0-only, Ris Peng; credits to m417z retained in vendored source |
| MinHook fork | Windhawk's `src/windhawk/engine/libraries/MinHook` | same Windhawk revision | BSD-2-Clause, Tsuda Kageyu, with upstream modifications; source notices retained |
| HDE64 | Included in MinHook | same vendored tree | BSD-2-Clause, Vyacheslav Patkov |

The complete MinHook/HDE notices are in `Native/vendor/MinHook/LICENSE.txt`, also included with binary distributions as `MinHook-LICENSE.txt`. The original upstream tray sources are kept for attribution and comparison; `Native/Tray.cpp` is the actively compiled adaptation.

Changes include an independent source-built hook host, Microsoft PDB matching/indexing, checked WinRT accessors, monitor identity and routing policy, per-monitor overflow projections, persistent icon identity extraction, owned-property restoration, a controller lifetime lease, and a native Windows Forms settings interface.

The binary distribution includes the Microsoft .NET runtime. Its [license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) and [third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT) accompany the release. Windows DLLs and Microsoft PDB files are not redistributed; Windows supplies its DLLs, and the app obtains matching symbols directly from Microsoft's symbol server.

GPLv3 permits private use, modification and redistribution. Distributing modified binaries requires the corresponding source and license notices under the GPL; this repository and its tagged source are provided with releases. MIT and BSD components retain their notices. This project is not affiliated with or endorsed by Windhawk or Microsoft.
