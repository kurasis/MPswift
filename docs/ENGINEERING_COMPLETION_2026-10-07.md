# Bounded snapshots and resource shutdown follow-up (2026-10-07)

Baseline `c216afe2ae7a`, build 0.2.88-dev.1, main run 37646682395: all four jobs successful, 286 tests on each OS, 216 bounded parser cases, actual EN/RU/rebuilt-library/extracted WPF and independently downloaded 578-file/13-native ZIP verification. This follow-up preserves public interfaces, schemas and pinned dependency versions. The owner explicitly approved the new five-minute snapshot policy.

## Findings and concrete changes

| Priority / classification | File and cause | Minimal correction |
| --- | --- | --- |
| P2, snapshot availability boundary | `SqlitePlayerStore.BackupAsync`, `DatabaseRecovery.Restore`, `SqliteLibraryIndex.AddIndexSchema`: all-pages native BackupDatabase copy is outside the SQL VM budget | One internal native helper copies 128 pages per step and checks the approved five-minute soft deadline between steps. Incomplete copies roll back when their native handle closes; BUSY/LOCKED/native errors keep immediate provider behavior, without hidden retries |
| P2, reproduced lifecycle defect | `Player.Core/Playback/PlaybackCoordinator.cs`, DisposeAsync: stop/advance error skipped player disposal | Attempt stop, drain and player disposal; preserve all failures. Four new controls fail on a separately compiled unmodified coordinator and pass on the corrected source |
| P2, confirmed resource-chain defect | `Player.App/ViewModels/PlayerViewModel.cs`, final CloseAsync resource release: a waveform/coordinator error skipped later services/database and tokens | Attempt every post-save release, aggregate failures, dispose completed scan/art/import/wave/save tokens. Preserve the existing save-failure recovery behavior before this phase |
| P2, confirmed stream-ownership defect | `MediaSessionService.UpdateArtwork`: PNG/store/updater error escaped ownership of the new WinRT stream; output view was detached without an explicit owner | Finally dispose untransferred streams; transfer ownership only after successful updater publication. Explicitly close output/writer views, retain the parent artwork stream |

Nine actual-SQLite controls cover a multi-step 16 MiB payload, timeout and cancellation after a real page step, source/destination contention, SQLITE_FULL, read-only destination, closed/pre-cancelled inputs and a committed WAL write between steps. They verify retained source/previous destination and successful subsequent copies. Existing store/complete-backup/schema-migration tests exercise all three production call sites.

The new guarded `ResourceShutdownValidation` uses actual owned WAV/native preparation, waveform decoding, SQLite save/reopen and post-release injected errors. It requires exclusive music/database reopening and directory movement after one/two errors. Actual WinRT PNG writing must close output views while leaving the parent readable. It runs in EN/RU WPF and the extracted package; Linux compilation does not establish those Windows outcomes. No decoder/device success is simulated.

## Verification and audit depth

- Baseline: locked build and 286 tests passed before edits. Corrected local normal build has zero warnings/errors and **299 tests**, zero failures/skips. Four unmodified-coordinator controls fail as expected in fresh owned output copies; ordinary outputs remain intact.
- [Official refreshed NuGet vulnerability observation](evidence/security-advisory-refresh-2026-10-07.json): 28 exact app/test/framework/source-build identities, normal TLS and pinned SDK NuGet.Versioning range semantics, no matching advertised ranges. Feed updated 2026-10-07. This does not establish closed native-addon/OS/zero-day coverage.
- [All-local-object secret observation](evidence/security-all-objects-2026-10-07.json): binary and unreachable/unpushed local Git blobs and current nonignored working files, six high-confidence families, no hits. `Check-Secrets.py` is read-only and never prints matched values; tests verify six unreachable binary cross-boundary canaries, current files, no output overwrite and no value disclosure. CI runs controls and a local checkout scan; absent remote-only objects/custom secrets remain outside coverage.
- [Full analyzer inventory and reviewed dispositions](evidence/engineering-completion-analyzers-2026-10-07.json): strict latest-all remains failing; normal configuration is unchanged. Serializer-created/public types, dispatcher continuations and native cleanup failures are not deleted/suppressed to force a green result.

## Concrete limits

The snapshot deadline is soft: a single native/filesystem I/O call cannot be preempted; DONE commits the final step. No new database-size limit or retry loop is added. Raw failed backup/migration destinations may retain newly created diagnostic files; previously existing backups/source data are untouched, and complete ZIP/recovery publication happens only after successful validation. No UI cancellation button or parser-process redesign is introduced.

Windows/native/package observations require this source's CI. Clean offline Windows 11, real audio endpoints/power loss/two-hour listening, signing and owner rights/source-distribution decisions remain open. Exact NuGet PE reproduction is not established by replacement compatibility. Official modern Monkey's Audio developer/download pages still return HTTP 406; no unverified encoder or fabricated >4 GiB APE fixture is used. These observations do not establish complete application safety or stable distribution acceptance.
