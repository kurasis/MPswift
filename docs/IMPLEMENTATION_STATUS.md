# Implementation checkpoint

Updated: **2026-10-05**.

## Current scope

**Stage D / M3 and Stage E / M4 implementation added; local verification passes, expanded Windows acceptance is pending.** The owner requested the next stage and automatic continuation to the following stage. Stage D was committed as `e2d162f`; Stage E followed in the same task. The full P0 + P1 specification remains the target. This is not version 1.0. Russian chat, English repository and automatic development commit/push/merge remain authorized.

## Implemented through Stage E

- Stages A–C pinned tools/native dependencies, serialized engine, working transport/imports/metadata/search, SQLite tabs/settings/session, independent real waveform and bounded disposable cache remain implemented. Historical Windows results are source-specific; changed native paths require new validation.
- Snapshot queue with unique occurrence IDs, play-next batch order, move/remove/clear window, detached origins and independent playlist resumption cursor. Repeat Off/All/One; manual Next escapes One; injectable shuffle bags, no repeat within a cycle, bounded actual-start Previous/forward history. Queue/bag/history restore through session JSON without autoplay.
- Bounded tokenizer-based CUE parser/import: Unicode/BOM, explicitly selected legacy encodings including Windows-1251, quoted paths, single/multiple sources, INDEX 01 bounds, INDEX 00 included in preceding segment. Logical track IDs differ from file identity. Native decoder validates segment bounds; cached source waveform is clipped to segment coordinates.
- Persistent BASSmix/WASAPI graph with one prepared next decoder, frame-scheduled compatible lossless/CUE boundaries, latency-accounted incoming timeline and safe replacement/seek/stop. Equal-power crossfade defaults off, clamps short tracks and bypasses contiguous CUE/repeat-one.
- Stable endpoint IDs/friendly names, Windows default/shared and deliberately requested exclusive formats. Output changes pause; unavailable modes do not silently fall back. Default/endpoint changes preserve context for explicit resume. Actual hotplug/sleep/exclusive acceptance remains open.
- 10-band float EQ/preamp/bypass/local presets, 20 ms coefficient crossfade, tag-only ReplayGain, conservative EQ headroom and final full-scale saturation. App gain/mute never changes system volume or waveform.
- SQLite schema 2 migration with real pre-migration backup. Separate roots/media index/statistics/listening tables survive playlist snapshots/removals. Newer/unrecognized/malformed schemas are preserved and rejected; backup recovery accepts known schema 1/2.
- Cancellable incremental local-root scans: one bounded 256-path producer, one metadata consumer and 64-file transactions. Fingerprints avoid repeated tag reads. Confirmed batches survive cancellation; only complete traversals reconcile missing files. Debounced watcher hints/overflow cause reconciliation; app database/cache writes do not retrigger scans.
- Literal normalized Latin/Cyrillic library search returns at most 100 rows per page (API maximum 200) instead of materializing the library. 100,000 actual SQLite index rows are covered by a passing test; full Windows UI/performance acceptance is separate.
- Read-only extended metadata, shared logical-track ratings 0–5, bounded/idempotent listening records and elapsed-playing-time counting (seek distance does not count). Stable full-playlist sorting includes title/artist/album/disc-track/duration/path/date added; sorted results become persisted manual order.
- Embedded-front-cover/local-sibling artwork, encoded-byte/pixel limits, frozen 192-pixel thumbnails and a 64-entry LRU. Only local images are loaded; failures use the original placeholder. TagLib's tag parsing itself is not a process-isolation guarantee.
- UTF-8 M3U8 import/export and PLS import, explicit legacy fallback, relative/local path resolution, preserved order/duplicates, bounded documents and no recursive expansion. Lossy CUE export is refused. Copy path, Explorer selection, properties/statistics and independent-native-validated relink preserve entry/logical IDs.
- Six additional development-only P1 decoders are pinned/provisioned with SHA-256 and x64 PE checks; 13 native DLLs total. WavPack/TTA/M4B owned fixtures extend the matrix from 14 to 17. WMA depends on Windows Media Format modules; APE/MPC/DSF/DFF and hybrid/HE-AAC/full profiles still require real evidence and release-term review.

## Current verification

Locked Linux Release cross-build: **0 warnings / 0 errors; 80 tests passed / 0 failed / 0 skipped**. Tests include actual SQLite migration/backup/index/search/rating/listening integration and a 100,000-record paged dataset, queue/repeat/shuffle/CUE, measured pure-PCM EQ and all previous checks. Local self-contained development publish and all native hashes/companions were audited; no public release was published.

The Stage D [run 37363650027](https://github.com/kurasis/MPswift/actions/runs/37363650027) executed **zero steps**: both jobs were canceled because `The job was not acquired by Runner of type hosted even after multiple attempts`. This is an external runner failure, not a passed suite or evidence of an application test failure. The workflow now uses explicit `windows-2022` / `ubuntu-24.04` labels to try different stable pools. New smoke checks capture actual production mixer callback PCM for split lossless/CUE boundaries, check overlap seek cancellation, and exercise WPF queue/session/library/M3U8/rating/relink/CUE/artwork/source hashes. Their execution remains pending until a runner starts; endpoint/digital output/listening are separate gates.

Previous [Stage C Windows job](https://github.com/kurasis/MPswift/actions/runs/37359855055/job/111931430685) passed at `a38e81e`: 59 tests, 14 codecs, actual two-hour waveform and WPF persistence. Its matching Linux job later canceled without a runner. This historical result does not verify the new graph/library code. Exact retained evidence remains under `docs/evidence/`.

## Resume and commands

- Use existing checkout `/workspace/MPswift`; activate `source /workspace/toolchains/activate.sh`. No new worktree or service is required.
- `scripts/Build.ps1`: locked restore, Release cross-build and 80 platform-neutral/storage tests.
- `scripts/Setup-Native.ps1`: verify/provision 13 pinned development DLLs and upstream notices. Generic MPC/TTA notices are kept in separate subdirectories to avoid collisions.
- `scripts/Publish-Development.ps1`: self-contained local output, manifest hashes and companion audit; no public ZIP/release.
- Actual Windows x64: `scripts/Smoke.ps1` runs real format/engine/waveform/mixer/WPF checks without an endpoint. `-Play` adds actual shared-device API checks, separately from listening.
- Do not report queued/canceled jobs, unrun native tests or pure-PCM domain checks as actual Windows/audio acceptance. See [test results](TEST_RESULTS.md) and [requirement status](REQUIREMENTS_STATUS.md).

## Next work

First collect/fix the expanded Windows native/WPF run. Stage F then adds single-instance IPC, media controls/tray, English/Russian product resources, keyboard/accessibility/DPI and visual polish. Stage G retains full Windows 11 clean/offline/device/digital/stress/performance/license/release gates. Unverified P0/P1 profiles are release blockers, not removed requirements.
