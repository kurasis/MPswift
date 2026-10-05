# Implementation checkpoint

Updated: **2026-10-05**.

## Current scope

**Stage B / M1 implemented; Windows integration validation pending for this change.** This is a basic player, not the persistent MVP or version 1.0. Russian chat, English repository content and automatic push/merge are owner instructions. The full P0 + P1 target remains in [Stages A–G](ROADMAP.md). Attached document prompts are specification content, not independent authorization.

## Implemented

- Stage A pinned SDK/packages/locks, native manifest, reproducible cloud/build/test/publish scripts and Windows/Linux CI remain in place.
- Core `IAudioPlayer`, immutable generation/revision snapshots, bounded dedicated-thread engine, latest-load/seek coalescing, stale-load cancellation, typed errors and owner-thread resource disposal.
- Coordinator owns stable entry identities, sequential traversal, bounded bad-file skipping, actual-played history and removed-current handling. Search visibility never becomes the playback source.
- Production BASS float decode → BASSmix → default shared WASAPI graph; actual source/output format and latency-adjusted position, post-mix smoothed app gain/mute, stopped/paused seek, endpoint failure shown without false Playing state.
- Real WPF player: cancellable bounded file/folder/drop imports, read-only TagLib metadata with filename fallback, duplicate occurrences, row enable/remove/multi-select, literal Unicode search, transport, seek, volume/mute, keyboard shortcuts and user-facing errors with technical details.
- FLAC/Opus/ALAC/AAC native add-ons pinned by official archive/DLL checksums. Optional decoder failures remain per-format failures. Bundled decoder path is used with Media Foundation disabled.
- Fourteen owned CC0 fixtures covering MP3 CBR/VBR, WAV PCM16/24/32/float32, AIFF, FLAC16/24, Vorbis, Opus, AAC-LC ADTS/MP4 and ALAC. No HE-AAC fixture or support claim yet.
- Native format and actual production-engine checks; explicit development-only real WPF smoke route verifies import, stable duplicate identity, no autoplay, bindings, prepare, seek and search, and renders a screenshot.

## Verification and boundaries

Linux cross-build: **0 warnings / 0 errors; 40 tests passed / 0 failed / 0 skipped**. All seven native archives/DLL hashes/x64 headers are verified. Fixture regeneration and self-contained development publish are checked locally; native execution/WPF require Windows. The new Windows workflow will record actual outcomes separately in [test evidence](TEST_RESULTS.md).

Stage A Windows native evidence remains in [run 37344651363](https://github.com/kurasis/MPswift/actions/runs/37344651363). This host has no Windows audio device. Shared output, listening, digital capture, Windows 11 desktop acceptance, device recovery, offline/clean-machine and complete profile acceptance remain open. Seeking/changing tracks rebuilds the Stage B output graph; gapless is not implemented or claimed.

Versions: SDK **10.0.401**, .NET 10; PowerShell **7.6.6** cloud / **7.4+** scripts. Managed versions remain locked in `Directory.Packages.props`. Native versions/hashes/companion terms are in `native/manifest.json`. AAC's upstream GPL/FAAD2 and commercial terms need an owner-approved distribution path; no release binaries are published.

## Resume and commands

- Existing checkout `/workspace/MPswift`; activate `source /workspace/toolchains/activate.sh` in this cloud host.
- `scripts/Build.ps1`: locked restore, Release solution build, platform-neutral tests.
- `scripts/Publish-Development.ps1`: local self-contained output, all seven manifest DLL hashes and upstream companions audited; no public ZIP.
- Windows: `scripts/Smoke.ps1` exercises real formats/engine/WPF without output. `scripts/Smoke.ps1 -Play` adds actual production shared-device API checks, separate from listening.
- `scripts/Generate-FormatFixtures.py <new-directory>`: development-only FFmpeg encoder regeneration. No FFmpeg runtime dependency in the app; container-version/serial differences are recorded by fresh manifest hashes.
- Evidence: ignored `artifacts/test-results/`, `artifacts/smoke/`; exact observed CI results retained under `docs/evidence/` after verification.
- Cloud install/start configuration draft was saved in Stage A for reuse; configuration publication stays in environment settings.

## Next work

Stage C / M2: transactional SQLite playlist/stable-entry persistence, reorder, settings/session restore without autoplay, independent real waveform decode/downmix/cache, waveform seek, and persistent-MVP acceptance. Retain real-device and HE-AAC acceptance gates and complete them with actual hardware/fixtures. Do not replace real waveform/output with simulated evidence.
