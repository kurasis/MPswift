# Implementation checkpoint

Updated: **2026-10-05**.

## Current scope

**Stage C / M2 implementation in progress; Linux validation passed and Windows integration pending.** Persistent playlists/settings/session and actual independent waveform analysis are implemented. The full P0 + P1 specification remains the target; real-device, Windows 11 clean-machine/offline and HE-AAC acceptance are still open and this is not version 1.0. Owner instructions remain Russian chat, English repository content and automatic development push/merge.

## Implemented through Stage C

- Stages A/B pinned tools/packages/native libraries, production serialized engine, functional imports/transport/errors, 14 real native codec fixtures and Windows/Linux CI remain intact.
- Version-1 SQLite relational playlists/tracks/entry IDs with independent duplicate occurrences, enabled state/order, session snapshots, foreign keys, WAL/busy timeout, transactions and a bounded dedicated database thread.
- Persistent tabs: create/rename/duplicate/delete/move; final tab recreates Default. Multi-row keyboard and drag reorder preserve IDs and reject filtered reorder. Editing a different tab retains the active source sequence.
- Debounced atomic versioned JSON settings with prior-file backup; app volume/mute/window size/cache budget. Session position persists approximately every 10 seconds during playback, on transport transitions/seek and on clean exit; reopen prepares/seeks without autoplay.
- Local per-user data and explicit `portable.marker` / `Data` mode. Unwritable portable storage offers a per-user choice; database errors preserve originals, newer schemas are rejected, and a data-directory ownership lock prevents two writers.
- Real SQLite backup API includes current WAL data, with separate settings export. Explicit backup recovery validates schema/integrity/references first and retains previous main/WAL/SHM files.
- Real on-demand BASS float waveform decode on one independent worker; all-channel extrema preserve opposite-phase peaks. Approximately 100 buckets/s coarsens to at most 300,000 buckets, with fixed PCM buffers. New tracks cancel obsolete jobs; generation checks prevent stale display.
- Versioned bounded binary waveform cache keyed by canonical source/size/time/algorithm/native manifest, with checksum validation, atomic completion writes and configured LRU budget. Cache errors retain a seekable timeline; sources are read-only.
- Lightweight WPF waveform geometry, accent played clip/cursor, hover/click/drag seeking and accessible slider/keyboard fallback. Shared reference-counted BASS context keeps playback and analysis handles independent.

## Current verification

Linux Release cross-build succeeds with **0 warnings / 0 errors; 59 tests passed / 0 failed / 0 skipped**. New actual SQLite/settings/cache/domain tests cover persisted duplicate identity/order, SQL parameters, transactional rollback, session-only writes, live-WAL backup, explicit recovery preserving corrupt originals, incompatible/corrupt database preservation, second-writer rejection, no-autoplay restore and bounded opposite-phase waveform accumulation.

New Windows checks are pending: native independent waveform/cache/cancellation, actual two-hour PCM decode, and real WPF tabs/edit/seek/save/reopen without autoplay. Previous Stage B Windows evidence remains in [test results](TEST_RESULTS.md). Native output/listening/capture, HE-AAC, gapless/device recovery and clean Windows 11/offline acceptance are not inferred from these tests.

## Resume and commands

- Existing checkout `/workspace/MPswift`; activate `source /workspace/toolchains/activate.sh` in this cloud host.
- `scripts/Build.ps1`: locked restore, Release solution build, platform-neutral tests.
- `scripts/Publish-Development.ps1`: local self-contained output, all seven manifest DLL hashes and upstream companions audited; no public ZIP.
- Windows: `scripts/Smoke.ps1` exercises real formats/engine/WPF without output. `scripts/Smoke.ps1 -Play` adds actual production shared-device API checks, separate from listening.
- `scripts/Generate-FormatFixtures.py <new-directory>`: development-only FFmpeg encoder regeneration. No FFmpeg runtime dependency in the app; container-version/serial differences are recorded by fresh manifest hashes.
- Evidence: ignored `artifacts/test-results/`, `artifacts/smoke/`; exact observed CI results retained under `docs/evidence/` after verification.
- The cloud startup draft will be refreshed for Stage C tooling and Stage D continuation after verification. The existing complete `install_script` is retained; publishing reusable configuration stays in environment settings.

## Next work

Complete Stage C Windows integration evidence, retaining hardware/release acceptance as open gates. Then Stage D / M3 adds explicit queue/repeat/shuffle/history, persistent mixer transitions, measured gapless, CUE segments, devices/exclusive/recovery, EQ/ReplayGain/crossfade/clipping. Keep source preservation and evidence boundaries truthful.
