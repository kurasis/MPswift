# Conservative code audit — 2026-10-07

Scope: implementation, boundaries, dependencies, build/CI, documentation and tracked files. Preserve production signatures, saved-data schemas, playlist/queue identities, audio policy and pinned dependencies. The checkout was clean at `215c650031790f7774e6c400c3e3372969365ac8`; no unfinished owner changes were overwritten.

## Baseline and workflow

The composition root is WPF `App`; `Player.Core` owns platform-independent contracts/rules and `Player.App` owns Windows/native/storage/UI services. Native source is also linked into the CLI harness, and storage source into managed tests. XAML handlers, generated MVVM commands, native imports and diagnostic CLI routes are runtime entry points, not deletion candidates based on simple reference counts.

Use the existing checkout, SDK 10.0.401 and activated PowerShell. `scripts/Build.ps1` performs locked restore, warnings-as-errors compilation (including SDK analyzers/type checking) and xUnit. The baseline passed with zero warnings/errors and **184/184 tests**. Read-only SDK analyzer verification and targeted IDE0005/IDE0051 verification also returned success. No separate lint/type-check configuration is prescribed. The latest baseline hosted run is [37475291031](https://github.com/kurasis/MPswift/actions/runs/37475291031).

Windows `Smoke.ps1` and `Package-Smoke.ps1` execute real BASS/WPF/filesystem/packaged checks. They cannot run on Linux; the existing main workflow requires them before publishing. Run the app on Windows using the README commands or the extracted self-contained executable. This audit does not substitute managed checks for native execution or manual hardware acceptance.

## Findings and changes

P1 = material runtime/performance issue; P2 = correctness/robustness; P3 = maintenance/documentation. No data-loss finding was established.

| Priority | File | Cause and impact | Disposition |
| --- | --- | --- | --- |
| P1 | `src/Player.Core/Playback/PlaybackOrder.cs` | `SetSource`, shuffled `Candidates` and `Restore` repeatedly search the whole source for every remaining ID: quadratic work on the UI coordination path. | Use local eligible-ID sets and a first-occurrence lookup. Preserve existing bag order, random calls, occurrence identity, queue/repeat/history behavior and duplicate-ID first-match semantics. Four targeted regressions cover eligibility, restore, repeated logical/occurrence identities and 10k entries. |
| P2 | `src/Player.App/Services/Library/ArtworkService.cs` | The cache key omits the chosen sibling path. `cover.png` and `folder.png` with equal size/mtime collide after the preferred file is removed, returning the wrong thumbnail. | Include the canonical sibling identity. Existing Windows smoke now switches between owned red/blue PNGs with exactly equal size/time, checks actual decoded pixels, then checks cache reuse when the preferred cover returns. Embedded/sibling priority and the 64-entry budget stay intact. |
| P2 | `src/Player.App/Services/Storage/SettingsFile.cs` (`Read`) | A path stat followed by an independently opened `ReadAllText` leaves a size-check/read window; the 64 KiB limit does not actually bound the read buffer if the file changes. | Open once, capture length once, allocate at most 64 KiB, read exactly and reject trailing growth. Preserve BOM detection/UTF-8/UTF-16/UTF-32 compatibility, schemas, clamping and read-only originals. Seven boundary/encoding cases cover the existing contract. |
| P2 | `src/Player.App/Services/Storage/SettingsFile.cs` (`LoadWithRecovery`) | `InvalidDataException` derives from `SystemException`, not `IOException`. A semantically invalid/oversized backup bypasses the aggregate recovery error, losing the primary failure context. | Include it in the existing backup rejection filter. The new regression first failed on the baseline and now retains both causes, both original files and the refusal to offer invalid recovery. |
| P3 | `src/Player.App/Services/Library/MediaImportService.cs` | Private `Bounded` helper remains after metadata handling moved to `MediaMetadataReader`; it has no call sites, delegate/XAML/config binding or reflection lookup. | Remove this one private method. The reader's active bounds handling remains. No public diagnostic type is removed. |
| P3 | `src/Player.Core/Playback/PlaybackCoordinator.cs` | Private `_removedCursorIndex` is assigned but never read. Its redundant source scans duplicate the actual removed-cursor bookkeeping in `PlaybackOrder`. It is not part of captured/serialized state or any generated/reflection entry point. | Remove the field and write-only calculation. Keep `PlaybackOrder`'s used cursor and all existing removed-source/navigation regressions. |
| P3 | `README.md`, `src/Player.App/Services/Storage/WaveformCache.cs` | README still reports 115 tests, restart-only language changes and an obsolete ZIP pattern. Cache comment describes peak-only data after RMS was added. | Update instructions/checkpoint wording and the comment. Historical source-specific evidence in other documents remains historical. |

The short plan was: establish the baseline; reproduce/measure the findings; apply isolated algorithm/cache/reader/error-handling fixes; remove only confirmed private dead code; add focused regressions; run full existing validation and Windows publication gates. Changes introduce no new dependency or public abstraction.

## Evidence and limits

The post-change locked Release build passes with zero warnings/errors and **196/196 managed tests**. Twelve added cases cover the corrected recovery path, byte/BOM compatibility and exact playlist-order behavior. SDK analyzer and targeted unused-code verification are repeated before integration. ZIP inventory/negative checks and Windows native/WPF/extracted-app checks are required by the unchanged workflow; exact run/source/version/download metadata accompanies each published prerelease.

An owned managed probe used 10,000 entries, a 64-entry warmup and three samples per operation. Median times in this Linux environment were `SetSource` **349.408 → 3.650 ms**, shuffled `Candidates` **61.856 → 2.879 ms**, and `Restore` **109.260 → 1.463 ms**. The complete ordered candidate-ID SHA-256 stayed identical. These are local algorithm observations, not Windows/reference-PC UI latency guarantees. [Raw measurements, source hash and dependency snapshot](evidence/code-audit-managed-2026-10-07.json).

All explicit packages have active roles (MVVM generation, BASS core/mixer/WASAPI, TagLib metadata/artwork, SQLite and test hosting/discovery). The read-only NuGet advisory query included transitive packages and reported no known vulnerabilities from its current feed snapshot. This is not proof that dependencies have no defects. No package/lock update is made.

Tracked files contain no confirmed disposable temp/archive remnants. Fixture media, product assets, native inventory/provisioning scripts and historical evidence are intentional. Ignored artifacts, local databases/portable data and native DLLs are not swept or deleted. Audit probes/logs are outside Git; the compact evidence JSON is intentional documentation.

## Remaining questions and retained code

| Priority | File | Confirmed issue or uncertainty | Why retained |
| --- | --- | --- | --- |
| P2 | `src/Player.Core/Playback/AudioProcessing.cs` | Every new `PcmProcessor` retains the previous processor, which itself can retain earlier processors. An owned managed probe created 1,000 processors without processing and confirmed 999 retained previous processors. Rendering clears the active previous link after the smoothing interval; inactive processors can still retain older links. | Bounding this chain needs an agreed ownership/smoothing design: `previous` is a public constructor input that other consumers can still use. Mutating an earlier processor or changing DSP transition policy would exceed the authorized conservative pass. Native/device resource impact is not measured by this probe. |
| P3 | `src/Player.App/Services/Library/MediaImportService.cs`, `MediaMetadataReader.cs` | Media extension literals are repeated, with document formats additionally supported by the importer. The reader exposes a public mutable set. | Reusing that mutable set changes importer coupling/behavior; replacing it with an immutable public surface changes an interface. Defer until that contract is agreed rather than adding another registry abstraction. |
| P3 | `src/Player.App/ViewModels/PlayerViewModel.cs` | One large class coordinates UI, import, source/session, saves and shutdown. The responsibilities and lifetimes are intertwined. | Keep tested behavior and generated/XAML interfaces; broader extraction needs focused ownership/lifecycle work with a concrete benefit. |
| P3 | `src/Player.App/ViewModels/DiagnosticsViewModel.cs`, `Services/Audio/NativeDiagnostics.cs` | Public historical diagnostic types are not instantiated by the current composition root. | Absence of direct production references does not establish absence of external consumers; preserve the public interfaces requested by the owner. |

Existing full Windows 11/physical Narrator/DPI, two-hour real-device output, listening/digital/full-profile and stable distribution acceptance remain open in the release documents. This audit does not claim to complete those gates.
