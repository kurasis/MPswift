# Ownership and analyzer follow-up (2026-10-07)

Baseline: `9205d3f96a38`, main run 37635304638, 284 tests per OS. Normal locked Release checks passed before edits. No public API, persisted schema, dependency version or playback policy changes are made.

## Confirmed defect and scoped hardening

| Priority / classification | File and cause | Minimal correction and verification |
| --- | --- | --- |
| P2, confirmed shutdown defect | `src/Player.App/Services/Storage/SqlitePlayerStore.cs`, `Run` final cleanup: a held-file release exception skipped remaining resources and completion of `_exit`; the worker could terminate without completing asynchronous disposal | Attempt every resource release, preserve/aggregate failures, always complete disposal. Two actual-store fault controls cover one/two failing releases, subsequent pins/directory release, repeat disposal and reopening committed data |
| P3, explicit loader policy | System-DLL declarations across Windows/audio/storage services and isolated validation tools lacked explicit search scope | Add System32 scope to 34 known Windows-system declarations. This is hardening, not a demonstrated DLL-hijack exploit. Keep addon imports under the existing hash-verified native loader |
| P2, validation-evidence gap | Earlier TagLib source preparation ran Core tests after copying a rebuilt DLL, but Core has no TagLib dependency and did not load it | Add a separate guarded Windows CI job: locked source rebuild, real EN/RU WPF metadata workflow in copied app outputs, identity/hash of the actually loaded DLL, unchanged original files. Core regression results are reported separately and are not called TagLib compatibility proof |

The shutdown fault controls inject throwing streams into the real held-file registry without introducing production test hooks or public interfaces. Normal disposal, original exception visibility and committed database content are retained. No user files are used.

## Analyzer coverage and limits

The earlier 558-diagnostic exploration had partial project coverage. A forced full solution rebuild now collects all projects, deduplicating linked files and WPF temporary/repeated compilations by source location/rule. The inventory moves from 1,281 to 1,247 unique locations; 34 system-import observations are closed. [Exact counts and log digests](evidence/analyzer-followup-2026-10-07.json) distinguish before/after observations. Counts are diagnostics, not confirmed bugs.

For discovery only, `TreatWarningsAsErrors=false` allows the full inventory to complete. The repository retains its ordinary warning-as-error policy; strict `AnalysisLevel=latest-all` remains failing. Normal configured compilation and the expanded 286-test suite pass locally. Windows/native/source-replacement results require this source's CI.

Ownership-transfer findings cannot be deleted mechanically: worker-finally cleanup, held native pins and deferred disposal account for many CA2000/CA2213 observations. UI continuations require dispatcher affinity. SQL findings use fixed commands/whitelisted identifiers; existing injection controls remain. Shuffle/test Random is not a security primitive. Serializer-instantiated models and publicly visible historical types are retained. Four BASSWMA validation imports still use the verified resident addon; forcing System32 would change addon resolution. Broader individual review remains open.
