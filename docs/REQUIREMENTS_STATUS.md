# Requirement and acceptance status

The full specification remains the target. Unit tests or cross-builds do not satisfy an end-to-end Windows acceptance scenario. Observed native/WPF checks and pending real-device acceptance are recorded separately in [test results](TEST_RESULTS.md).

| ID | Priority | Scenario | Planned stage | Status | Evidence / next work |
| --- | --- | --- | --- | --- | --- |

| AC-001 | P0 | Clean portable launch | B/C | Self-contained extracted apphost smoke passed; clean Windows 11 baseline unrun | Unicode extraction/arbitrary CWD/invalid DOTNET_ROOT passed on Windows Server 2022; clean Windows 11 without SDK pending |
| AC-002 | P0 | Network disconnected before first run | B/C | Local-only app implemented; disconnected-first-run acceptance unrun | [Exact release workflow](RELEASE_ACCEPTANCE.md); no runtime downloads/accounts |
| AC-003 | P0 | Open MP3 and FLAC | B/C | Implemented; MP3/FLAC native integration passed; device acceptance open | MP3 CBR/VBR and FLAC16/24 fixtures; device playback acceptance open |
| AC-004 | P0 | Core format matrix | B/C | Native matrix passed for 21 representative profiles; full acceptance partial | 21 real fixtures; HE-AAC/profile/device gates open; [formats](FORMAT_SUPPORT.md) |
| AC-005 | P0 | Pause/resume/stop | B/C | Production controls implemented; core unit tested | Owner-thread pause/resume/stop tests; actual device API/manual acceptance open |
| AC-006 | P0 | Rapid seek/track changes | B/C | Core unit tested; production native prepare/seek/rapid changes passed | 300 stale/coalesced loads, cancellation, stop and seek; production native/WPF checks passed |
| AC-007 | P0 | Playlist persistence | B/C | SQLite/tab/entry persistence implemented and integration unit tested | Stable IDs, duplicate entries, enabled/manual order; actual Windows model/database reopen passed |
| AC-008 | P0 | Unicode paths and metadata | B/C | Unicode native path and real WPF metadata/bindings passed; output acceptance open | Generated Unicode path and metadata fixtures; actual WPF import/bindings/Unicode FLAC tags passed |
| AC-009 | P0 | Real waveform | B/C | Real independent waveform/cache/WPF control implemented | All-channel float extrema; actual native/WPF data/render/seek integration passed |
| AC-010 | P0 | Long file waveform | B/C | Real two-hour native analysis/cancellation/handle checks passed; performance acceptance partial | At most 300,000 buckets, fixed PCM chunks, no full-file PCM allocation |
| AC-011 | P0 | Corrupt/unsupported/missing file | B/C | Error paths implemented; core unit tested | Typed visible decoder/file/dependency/output errors; explicit bad-file no autoplay, bounded skip |
| AC-012 | P0 | Session restore | B/C | Session restore implemented and no-Play domain tested | Source/selected tab, detached active entry, position/gain/mute; actual WPF reopen passed without autoplay |
| AC-013 | P1 | Large collection | D–G | 100k production SQLite query + WPF rendering and nearly 10k scrolling measured on Windows | 100-row pages/≤18 containers; 9987 real rows/≤7 scroll containers; warm p95 ≤231.47 ms search and ≤15.40 ms scroll; reference Windows 11 acceptance open |
| AC-014 | P1 | Queue/repeat/shuffle | D–G | Implemented/domain/SQLite/WPF restore passed | Snapshot queue precedence, repeat/manual escape, bag/history restoration |
| AC-015 | P1 | Remove currently playing entry | D–G | Current-source retention implemented across tabs/deletion | Core removal test; other-tab editing retains coordinator source; actual WPF other-tab/source check passed |
| AC-016 | P1 | CUE single/multi-file | D–G | Parser/import/logical native bounds and WPF passed | Tokenizer, Unicode/explicit legacy, per-source boundaries and cache clipping |
| AC-017 | P1 | Gapless lossless fixture | D–G | Callback PCM split/CUE measured: maximum error 0 | [Actual callback PCM evidence](evidence/stage-de-windows-native-ui.json); endpoint capture remains open |
| AC-018 | P1 | Lossy gapless claims | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-019 | P1 | Crossfade | D–G | Scheduled equal-power overlap implemented; native overlap/seek check passed | Off default; short/CUE/repeat/seek policy |
| AC-020 | P1 | EQ and ReplayGain | D–G | PCM measured domain checks pass; native/device acceptance open | EQ/bypass/Nyquist/headroom/final saturation; tag-only gain |
| AC-021 | P1 | Device changes | D–G | Endpoint/default change policy implemented; hardware unrun | Stable IDs; preserve context and explicit resume; actual unplug/sleep pending |
| AC-022 | P1 | Exclusive mode unavailable | D–G | Explicit exclusive request/check/error implemented; hardware unrun | No silent shared fallback; unavailable/busy endpoint acceptance pending |
| AC-023 | P1 | Metadata/artwork failure | D–G | Read-only metadata/local bounded thumbnails/fallback implemented | Actual Windows corrupt/oversized/truncated cover, frozen bounded portrait/cache/cancellation/handles passed; huge-tag isolation acceptance open |
| AC-024 | P1 | Folder scan cancellation | D–G | Bounded cancellable incremental scanner implemented | 256 pending paths, one reader, 64-record writes; confirmed batches retained; Windows scan/reconcile workflow passed |
| AC-025 | P1 | File changes/watcher overflow | D–G | Fingerprint generations/watcher hints/overflow reconciliation implemented | Actual SQLite missing/reappearance state tested; native watcher overflow pending |
| AC-026 | P1 | Import/export | D–G | M3U8/PLS/legacy import and atomic M3U8 export implemented | Domain duplicate/order/local/recursive/encoding/CUE refusal tests pass; Windows scan/reconcile workflow passed |
| AC-027 | P1 | Search does not alter playback order | D–G | Unicode filtering keeps persisted source; manual filtered reorder rejected | Core tests and prior real WPF search proof; expanded tab/reopen check passed |
| AC-028 | P1 | Single instance | D–G | Real second-process concurrent forwarding/recovery passed | [Stage F Windows evidence](evidence/stage-f-windows-integration.json); bounded current-user pipe, no autoplay |
| AC-029 | P1 | Media keys/tray | D–G | Tray state/SMTC API and metadata passed; physical keys unrun | One media handler; actual published metadata matches coordinator; physical key/device-hidden playback pending |
| AC-030 | P1 | DPI/accessibility/localization | D–G | EN/RU resource/Automation/virtualization smoke passed; manual acceptance open | 205 keys, 6 realized containers for 9,987 rows; post-IPC culture and software-routed dropdown Escape passed; Narrator/physical DPI open |
| AC-031 | P1 | Data migration and full disk | D–G | Schema 1→2/complete ZIP backup+restore/settings recovery tests and actual WPF restore pass | Originals retained; real Windows ACL/lock errors and partial restore rollback passed; full disk/migration interruption remain open |
| AC-032 | P1 | Read-only portable location | D–G | Portable marker/fallback implemented; acceptance partial | Explicit writable Data/per-user choice; readonly-folder Windows acceptance open |
| AC-033 | P1 | Long playback/stress | D–G | Real 1000-cycle native preparation stress passed; output soak unrun | 50 warmup/load/prepare/seek/stop; handles/memory/p95 samples; two-hour device playback remains open |
| AC-034 | P1 | Offline traffic audit | D–G | Not implemented / not run | [Roadmap](ROADMAP.md) |
| AC-035 | P1 | Package audit | D–G | Linux/Windows 550-file audit, negative integrity and Windows apphost smoke passed | Fresh candidate/14 dependency declarations/13 native x64; license approval false; [gates](RELEASE_ACCEPTANCE.md) |
| AC-036 | P1 | Reference visual review | D–G | Actual screenshot reviewed; original vector/chrome/compact dark hierarchy | EN/RU screenshots retained in CI; review found/fixed ambient-language drift; physical scale review open |
| AC-037 | P1 | P1 format matrix | D–G | Six additional pinned P1 decoder paths implemented; coverage incomplete | 13 DLLs audited, WV/TTA/M4B fixtures added; native/profile/license release blockers remain |
| AC-038 | P1 | Source preservation | D–G | Codec/independent/long-wave source hash and handle checks passed | Read-only file/hash/handle checks include independent and long-wave paths in Windows CI |
| AC-039 | P1 | Crash/restart | D–G | Actual Windows WPF process termination/restart and rollback passed; full crash acceptance partial | Run 37415275489: native restart, no autoplay, IDs/queue/ratings/settings and live validation transaction rollback passed; migration/power-loss remain open |
| AC-040 | P1 | Diagnostic honesty | D–G | Reporting implemented; full release evidence pending | [Test results](TEST_RESULTS.md) and [checkpoint](IMPLEMENTATION_STATUS.md) |


## G4–G7 block: local results, Windows checks pending

Locked Release cross-build passes 115 tests, zero warnings/errors. Complete ZIP backup/restore has 13 real SQLite/file cases (live WAL, IDs/session/index/ratings/settings, retained originals, invalid entries/checksums/schema/foreign keys, no overwrite, ownership and occupied-path rejection). The expanded Windows harness exercises complete restore through WPF and invalid-restore recovery, real ACL/file-lock failures and artwork limits, 100k production query/page/render timings and nearly 10k playlist scroll timings. These Windows results are not yet observed. Four owned RF64/WMA/DSF/DFF fixtures extend the matrix to 21; actual new native results are pending. Full Windows 11/device/offline/full-disk/profile/licensing acceptance remains open.


G4–G7 observed status supersedes the pre-CI note above: [run 37418131090](https://github.com/kurasis/MPswift/actions/runs/37418131090) at `a00ee6c` passes 115 tests per OS, 21 profiles and EN/RU/extracted WPF backup/artwork/storage/performance checks. [exact reports](evidence/stage-g-formats-backup-performance-windows.json) retain raw results and boundaries. Whole AC acceptance is not inferred from the covered hosted workflows.
