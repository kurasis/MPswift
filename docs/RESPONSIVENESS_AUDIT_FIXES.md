# Responsiveness audit fixes (2026-10-23)

The owner asked for every confirmed finding of the static responsiveness/defect/security audit (source `9b3cf7e`) to be fixed. The audit found UI-thread work that scaled with playlist size, two defects and one ordering weakness. This change keeps public behavior, schema, pinned dependencies and source audio bytes; it changes how much work each operation does and on which thread.

## Saving (P2, P5)

- `SqlitePlayerStore.SaveAsync` and `SaveSessionAsync` validate and serialize on the database owner thread. The dispatcher only captures immutable arrays.
- After the first full library save of a store instance, later saves compare with the last committed rows. Only playlists whose entry rows changed are rewritten. Only tracks whose metadata changed are upserted. Tab names/order are updated in place, removed playlists and orphaned tracks are deleted. Any failed transaction or out-of-band track write (relink) clears the cache, so the next save is a complete rewrite.
- A decoded duration within 0.5 s of the tag duration no longer schedules a library save at track change.
- The ten-second position save no longer flips the status line to "Saving…".

## Playlist view (P1, P3, P7)

- An empty search sets `Filter = null`. Collection changes are not followed by `Refresh()` unless a search filter is active.
- Imports add rows as batches arrive but coalesce source sync, album headings and save scheduling to at most four times per second, with a final update when the import ends.
- `ReorderableCollection<T>` applies sort, album moves, drag-drop and keyboard moves as at most 128 incremental moves (the cheaper of front-to-back and back-to-front plans), otherwise one `Reset`. Removing more than 128 rows is one `Reset`.
- Metadata reconciliation, rating refresh, selection lookups and statistics queries use dictionaries/sets or batched `IN (…)` reads instead of nested scans. Unchanged metadata no longer raises six property notifications per row.
- Album headings and search compare spans instead of allocating per row. `PlaylistRowViewModel.Entry` no longer allocates on every read.
- Rating stars, icons and the waveform reuse frozen pens and offsets.

Not changed: the row template still contains the collapsed album-heading button. Replacing it with a selector needs Windows visual and UI Automation verification, and acceptance checks address that element.

## Disk I/O off the dispatcher (P4, P6)

- Copy files to clipboard validates paths on a worker. Export flushes on a worker. Dropped-folder drive/directory/link probes run on a worker.
- CUE waveform slicing runs on a worker. The waveform control validates each data instance once, stretches the cached envelope while the window is resized and rebuilds it 120 ms after the size settles.
- Startup still reads settings synchronously before the main window exists; it delays nothing on screen.

## Other findings

- P8: smooth scrolling advances once per composed frame (`RenderingEventArgs.RenderingTime`) instead of being capped near 66 fps. Appended rows no longer cancel a scroll animation.
- P9: library search returns the page and the match count from one query (`COUNT(*) OVER()`); a page past the end uses a separate count. An FTS index would need a schema migration and is not part of this change.
- P10/O1: the import cache is a bounded FIFO with O(1) eviction. Cached tags are reused only while the file size and write time are unchanged; otherwise the file is re-read under the same logical track ID.
- O2: every write to the statistics chain happens under `_snapshotGate`, and closing reads the final chain under the same lock.
- B1: `LocalFileAccess.ValidateFileLocation` (path syntax, drive type and existing ancestors/links) runs before any existence probe of a playlist, CUE or imported path.

## Verification

Local Linux (Ubuntu 24.04, distribution .NET SDK 10.0.113 with `global.json` temporarily pointed at it; not committed): locked restore and Release build of the full solution with zero warnings/errors. Player.Core.Tests 414 pass (406 before, plus incremental-save, rollback, database-thread validation, reorder/removal and heading-equivalence controls). TagRepair CLI 78 pass / 15 Windows-only skips. Secret-pattern controls and `git diff --check` pass.

Windows WPF, native, packaged, installed and installer acceptance run in CI for the pull request and are recorded only once observed. No timing was measured locally; the expected effect is reduced UI-thread work, not a measured millisecond figure.
