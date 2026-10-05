# Requirement and acceptance status

The full specification remains the target. Unit tests or cross-builds do not satisfy an end-to-end Windows acceptance scenario. Observed native/WPF checks and pending real-device acceptance are recorded separately in [test results](TEST_RESULTS.md).

| ID | Priority | Scenario | Planned stage | Status | Evidence / next work |
| --- | --- | --- | --- | --- | --- |
| AC-3 / DTS / other audio codecs | P2 | Individually approved add-ons | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-001 | P0 | Clean portable launch | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-002 | P0 | Network disconnected before first run | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-003 | P0 | Open MP3 and FLAC | B/C | Implemented; MP3/FLAC native integration passed; device acceptance open | MP3 CBR/VBR and FLAC16/24 fixtures; device playback acceptance open |
| AC-004 | P0 | Core format matrix | B/C | Native matrix passed for 14 core profiles; full acceptance partial | 14 real fixtures; HE-AAC/profile/device gates open; [formats](FORMAT_SUPPORT.md) |
| AC-005 | P0 | Pause/resume/stop | B/C | Production controls implemented; core unit tested | Owner-thread pause/resume/stop tests; actual device API/manual acceptance open |
| AC-006 | P0 | Rapid seek/track changes | B/C | Core unit tested; production native prepare/seek/rapid changes passed | 300 stale/coalesced loads, cancellation, stop and seek; production native/WPF checks passed |
| AC-007 | P0 | Playlist persistence | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-008 | P0 | Unicode paths and metadata | B/C | Unicode native path and real WPF metadata/bindings passed; output acceptance open | Generated Unicode path and metadata fixtures; actual WPF import/bindings/Unicode FLAC tags passed |
| AC-009 | P0 | Real waveform | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-010 | P0 | Long file waveform | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-011 | P0 | Corrupt/unsupported/missing file | B/C | Error paths implemented; core unit tested | Typed visible decoder/file/dependency/output errors; explicit bad-file no autoplay, bounded skip |
| AC-012 | P0 | Session restore | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-013 | P1 | Large collection | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-014 | P1 | Queue/repeat/shuffle | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-015 | P1 | Remove currently playing entry | D–G | Basic coordinator behavior unit tested | Removing playing entry retains source and advances remaining entries; full queue acceptance Stage D |
| AC-016 | P1 | CUE single/multi-file | D–G | Partial domain unit tests; native/UI acceptance not run | [Path tests](../tests/Player.Core.Tests/LocalMediaPathTests.cs), [segment tests](../tests/Player.Core.Tests/TrackSegmentTests.cs) |
| AC-017 | P1 | Gapless lossless fixture | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-018 | P1 | Lossy gapless claims | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-019 | P1 | Crossfade | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-020 | P1 | EQ and ReplayGain | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-021 | P1 | Device changes | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-022 | P1 | Exclusive mode unavailable | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-023 | P1 | Metadata/artwork failure | D–G | Read-only metadata/fallback implemented | Filename fallback and bounded technical errors; artwork deferred Stage E |
| AC-024 | P1 | Folder scan cancellation | D–G | Manual folder import cancellation implemented | Bounded worker/batches; already imported rows retained; full library cancellation Stage E |
| AC-025 | P1 | File changes/watcher overflow | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-026 | P1 | Import/export | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-027 | P1 | Search does not alter playback order | D–G | Search/source separation implemented and unit tested | Literal NFC Unicode filtering leaves full coordinator order; actual WPF filter/source check passed |
| AC-028 | P1 | Single instance | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-029 | P1 | Media keys/tray | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-030 | P1 | DPI/accessibility/localization | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-031 | P1 | Data migration and full disk | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-032 | P1 | Read-only portable location | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-033 | P1 | Long playback/stress | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-034 | P1 | Offline traffic audit | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-035 | P1 | Package audit | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-036 | P1 | Reference visual review | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-037 | P1 | P1 format matrix | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-038 | P1 | Source preservation | D–G | Read-only hash/handle checks passed for 14 fixtures and production engine | Stage B 14-fixture hashes and exclusive reopen assertions plus production disposal |
| AC-039 | P1 | Crash/restart | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-040 | P1 | Diagnostic honesty | D–G | Reporting implemented; full release evidence pending | [Test results](TEST_RESULTS.md) and [checkpoint](IMPLEMENTATION_STATUS.md) |
