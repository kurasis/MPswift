# SQLite snapshot-copy investigation and proposed plan (2026-10-07)

The owner approved a five-minute soft deadline. The [implementation and targeted controls](ENGINEERING_COMPLETION_2026-10-07.md) now cover backup, recovery and the additional pre-schema-migration copy through DatabaseSnapshotCopy. Copies use 128-page steps; unfinished native copies roll back. Source-specific Windows/native/package checks now pass in run 37654713897. Existing immediate native contention errors remain. The planned UI cancellation/retry changes are not introduced.

## Verified source behavior

`src/Player.App/Services/Storage/SqlitePlayerStore.cs` (`BackupAsync`) and `DatabaseRecovery.cs` (`Restore`) call `SqliteConnection.BackupDatabase`. The [official provider at the exact pinned 10.0.12 commit](https://github.com/dotnet/dotnet/blob/95017c711e6afc1085133d440e42b4bd78155701/src/efcore/src/Microsoft.Data.Sqlite.Core/SqliteConnection.cs) initializes a native backup, calls `sqlite3_backup_step(backup, -1)` once and propagates the native result. There is no managed retry loop in that method. Its native all-pages copy is outside the existing SQL virtual-machine instruction/time budget.

The [official SQLite API](https://www.sqlite.org/c3ref/backup_finish.html) documents positive page counts, OK/DONE/BUSY/LOCKED results, and rollback of an incomplete destination transaction when backup finishes. This confirms an availability boundary for large/contended copies; no corruption exploit is demonstrated. A soft deadline cannot preempt stalled operating-system I/O inside one native step.

## Concrete implementation proposal

1. Use one internal helper over the already pinned SQLitePCLRaw provider. Retain source/destination connections, held data-file identities, worker serialization and native error codes. Copy at most 128 pages per native step; finish/release in all outcomes.
2. Check cancellation/deadline between steps. Do not impose an arbitrary database-size ceiling. Make cancellation rollback an unfinished copy and leave the source and previous user backup unchanged.
3. Preserve current successful valid backup/restore content, schema and public APIs. BUSY/LOCKED handling must have an explicit bounded retry policy; new cancellation/refusal UI must clearly expose the cause.
4. Verify actual large owned databases, concurrent writers/held locks, cancellation between steps, native failure/finish rollback and post-failure reopening. Cover both backup and frozen restore snapshots, keeping current mutable-file/atomic-install controls.

The page loop can improve responsiveness but does not establish a hard wall-clock guarantee. A hard deadline against stalled native I/O would require a separate process and a larger design. Maximum duration was explicitly approved as five minutes and is implemented between steps. New retry or UI cancellation behavior would still require agreement. One stalled native call is outside a hard wall-clock guarantee.
