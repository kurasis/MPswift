# Requirement and acceptance status

The full specification remains the target. Unit tests or cross-builds do not satisfy an end-to-end Windows acceptance scenario. Stage A native load/decode/output checks are recorded separately in [test results](TEST_RESULTS.md).

| ID | Priority | Scenario | Planned stage | Status | Evidence / next work |
| --- | --- | --- | --- | --- | --- |
| AC-3 / DTS / other audio codecs | P2 | Individually approved add-ons | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-001 | P0 | Clean portable launch | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-002 | P0 | Network disconnected before first run | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-003 | P0 | Open MP3 and FLAC | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-004 | P0 | Core format matrix | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-005 | P0 | Pause/resume/stop | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-006 | P0 | Rapid seek/track changes | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-007 | P0 | Playlist persistence | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-008 | P0 | Unicode paths and metadata | B/C | Partial domain unit tests; native/UI acceptance not run | [Path tests](../tests/Player.Core.Tests/LocalMediaPathTests.cs), [segment tests](../tests/Player.Core.Tests/TrackSegmentTests.cs) |
| AC-009 | P0 | Real waveform | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-010 | P0 | Long file waveform | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-011 | P0 | Corrupt/unsupported/missing file | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-012 | P0 | Session restore | B/C | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-013 | P1 | Large collection | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-014 | P1 | Queue/repeat/shuffle | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-015 | P1 | Remove currently playing entry | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-016 | P1 | CUE single/multi-file | D–G | Partial domain unit tests; native/UI acceptance not run | [Path tests](../tests/Player.Core.Tests/LocalMediaPathTests.cs), [segment tests](../tests/Player.Core.Tests/TrackSegmentTests.cs) |
| AC-017 | P1 | Gapless lossless fixture | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-018 | P1 | Lossy gapless claims | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-019 | P1 | Crossfade | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-020 | P1 | EQ and ReplayGain | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-021 | P1 | Device changes | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-022 | P1 | Exclusive mode unavailable | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-023 | P1 | Metadata/artwork failure | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-024 | P1 | Folder scan cancellation | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-025 | P1 | File changes/watcher overflow | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-026 | P1 | Import/export | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-027 | P1 | Search does not alter playback order | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
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
| AC-038 | P1 | Source preservation | D–G | Read-only generated WAV smoke/source-preservation passed in Windows CI; full workflow acceptance pending | [Smoke source](../tools/Player.AudioSmoke/Program.cs), [Windows CI](https://github.com/kurasis/MPswift/actions/runs/37344651363) |
| AC-039 | P1 | Crash/restart | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-040 | P1 | Diagnostic honesty | D–G | Reporting implemented; full release evidence pending | [Test results](TEST_RESULTS.md) and [checkpoint](IMPLEMENTATION_STATUS.md) |
