# Distribution preparation — G13

This review records the actual pinned development candidate. It does not select an application license, purchase rights, or mark AC-035 accepted. Owner intent (personal, free public, commercial) remains unanswered. Development publication authorization is not proof of third-party rights.

## Completed independently

- Restored NuGet declarations are retained for all 14 exact application/runtime/projection packages; inventory additionally records license declaration type, copyright, repository and any package-provided commit.
- Microsoft.Data.Sqlite/Core 10.0.12 MIT text is retained byte-for-byte from `src/efcore/LICENSE.txt` at the exact NuGet-declared dotnet/dotnet commit `95017c711e6afc1085133d440e42b4bd78155701`.
- Four SQLitePCLRaw 2.1.12 packages retain the Apache-2.0 LICENSE and complete upstream NOTICE at release `v2.1.12`, commit `ca835d21508bff43121c65081035840ac5006c4c`. The complete NOTICE includes upstream component notices; its presence does not claim SQLCipher/OpenSSL binaries are deployed. The actual candidate uses e_sqlite3.
- TagLibSharp 2.3.0 retains COPYING (LGPL-2.1) and AUTHORS at release `TaglibSharp-2.3.0.0`, commit `b5ae84f2e84087bf160bb0471420200dd2b5d809`. Upstream Directory.Build.props declares version 2.3.0.0. These are release provenance, not a demonstrated reproducible mapping of the NuGet binary to complete corresponding source.
- [Pinned supplemental text manifest](licenses/manifest.json) identifies exact package applicability, upstream HTTPS commit URLs, byte lengths and SHA-256. Checked-in bytes enable offline packaging without fetching floating upstream content. Inventory retains the same provenance. Verification rejects missing/modified/extra texts and path traversal; extracted package verification checks each applicable copied text.
- All 15 native companion texts have separate SHA-256 pins in native/manifest.json, derived from archives matching the existing official archive hashes. Setup now checks original companion bytes as well as DLL bytes, and re-extracts a changed/missing companion from the verified archive. Candidate verification also checks these pins. Decoder versions, hashes, format support and APIs are unchanged.
- Existing Microsoft.NETCore runtime LICENSE/THIRD-PARTY-NOTICES, WindowsDesktop LICENSE, CommunityToolkit texts and ManagedBass MIT texts remain retained from their actual packages.

## Open decisions and evidence

| Dependency | Actual finding | Remaining work before declaring distribution cleared |
| --- | --- | --- |
| Application | Public source repository has no owner-selected application license | Owner chooses intended use and source/distribution license; no license is applied automatically |
| BASS and dependent addons | Pinned bass.txt says free qualifying non-commercial use; commercial usage needs applicable license. ManagedBass MIT does not replace this | Confirm entity/use and applicable grant; any purchase needs separate authorization |
| BASS AAC/MP4 2.4.7.2 | Pinned readme expressly conditions free distribution on GPL and refers commercial use to Nero. Archive contains DLLs, import libraries and C/Delphi/VB headers, no decoder implementation source | Resolve GPL/application/BASS compatibility and exact complete corresponding source, or owner-approved licensed replacement. Generic FAAD2 source or a header is insufficient |
| BASS_TTA 2.4.0.2 | Pinned readme invokes LGPL; archive contains LGPL text/DLL/import libraries/headers, no implementation source | Obtain complete source for the actual addon and establish user replacement/relinking path. Headers are insufficient |
| TagLibSharp 2.3.0 | Separate unsingle-file TagLibSharp.dll is deployed; exact release source is identifiable | Preserve complete matching source/build material, verify binary correspondence and replacement/relinking, establish distribution mechanism and required permissions. Signed library/release tag alone does not prove this |
| Windows SDK .NET projection/WinRT.Runtime | Targeting pack 10.0.19041.57 declares an SDK license URL, contains no retained license text, and supplies deployed projection assemblies | Review terms/REDIST list and actual CsWinRT/component provenance; runtime notices are not a substitute |
| Additional codec notices | Upstream native companion texts contain embedded codec attribution/conditions | Retained unchanged; complete per-component rights/source review remains required |

Pinned TagLib release source reference: https://github.com/mono/taglib-sharp/tree/b5ae84f2e84087bf160bb0471420200dd2b5d809 . This link is identification, not a written corresponding-source offer or a bundled complete source distribution. No upstream messages, legal acceptance, license purchase, source-license choice or decoder replacement is performed by this preparation.

## Reproducible checks

```powershell
./scripts/Test-LicenseTexts.ps1
./scripts/Build.ps1
./scripts/Package-Candidate.ps1 -SkipBuild
./scripts/Test-CandidateIntegrity.ps1
```

The two supplemental LICENSE/NOTICE copies for each SQLitePCLRaw package deliberately retain the whole upstream NOTICE. Per-package inventory points to source URLs/hashes instead of inventing missing NuGet files. Package manifest/checksums cover all retained texts. These checks prove provenance and completeness of the selected text set; they do not infer full legal clearance from a successful build.


## Source/build preparation follow-up (2026-10-07)

[Exact TagLib source preparation](TAGLIB_SOURCE_PREPARATION_2026-10-07.md) now retains the complete pinned upstream archive in an owned review directory, inventories it, rebuilds the reviewed library with zero warnings/errors and passes 284 Core tests in a separate copy. Core does not load TagLibSharp, so that old result does not prove library replacement. A new guarded Windows job rebuilds the library with a locked restore and verifies the actually loaded DLL during real EN/RU WPF metadata workflows. [Its actual Windows EN/RU observation now passes](TAGLIB_SOURCE_PREPARATION_2026-10-07.md#observed-windows-replacement-2026-10-07); exact NuGet build correspondence and a source distribution mechanism remain open. The shipped dependency is unchanged.


## Windows SDK/component terms preparation (2026-10-07)

The actual SDK targeting package is `Microsoft.Windows.SDK.NET.Ref/10.0.19041.57`. Its `WinRT.Runtime.dll` PE identifies `2.2.0.48161+8649ee3eeb2445ca2a36d80d878ef60b96a6c65d`; the exact Microsoft CsWinRT commit's MIT LICENSE and the package-declared Microsoft SDK RTF terms are retained unchanged in [license review material](license-review/manifest.json), with source URLs/byte hashes. This identifies a component's declared provenance, not reproducible whole-package source equivalence.

The official [SDK REDIST list](https://learn.microsoft.com/en-us/legal/windows-sdk/redist), reached from the SDK terms, explicitly lists the `Microsoft.Windows.SDK.NET.Ref` package and its deployed `lib/net8.0/Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` files for unmodified use enabling WinRT APIs. Thus the formerly unlocated REDIST material and runtime-component license text are now identified and retained. This is subject to the SDK's distribution conditions (including notices/copyright, functionality and applicable end-user/distributor terms); owner/distribution acceptance remains required. CsWinRT's MIT declaration does not replace the package's SDK terms. The exact CsWinRT MIT component notice now ships alongside the SDK package declaration through the existing checksum-verified supplemental notice mechanism; it is labeled as WinRT.Runtime.dll component material. The original SDK RTF terms remain review material. No application license is selected.
