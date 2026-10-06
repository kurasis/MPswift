# Implementation roadmap

The owner's request is to study the specification, divide it into clear stages when useful, and begin Stage A. The full product remains the P0 + P1 target. No scope reduction is approved.

| Stage | Specification milestone | Deliverable and exit evidence |
| --- | --- | --- |
| A — Foundation and dependency proof | M0 | Pinned SDK/packages, solution, native manifest, reproducible builds, independent core tests, native WAV smoke harness. Windows/device execution is recorded separately; missing Windows hardware is explicit. |
| B — Usable basic player | M1 | Serialized production audio service and coordinator, functional WPF transport/file/folder UI, duration/seek/volume/errors, core format fixtures. AC-003–006 and relevant AC-008/011. |
| C — Persistent MVP | M2 | SQLite playlists/stable entries, duplicate/reorder/multi-select/search, safe settings/session restore, real independent waveform analysis/cache/seek. All P0, clean-machine/offline checks. |
| D — Audio coordination | M3 | Explicit queue/repeat/shuffle/history, persistent mixer, measured gapless, CUE parser/segments, devices/exclusive/recovery, EQ/ReplayGain/crossfade/clipping. AC-014–022. |
| E — Library and file workflows | M4 | Bounded incremental index, Unicode search, artwork, P1 decoders, playlist import/export, ratings/history, relink and storage recovery. AC-013/023–027/031/037/038. |
| F — Windows integration and polish | M5 | Single-instance IPC, media session/keys/tray, English/Russian resources, keyboard/accessibility/DPI and original reference-style visuals. AC-028–030/036. |
| G — Release verification | M6 | Full automated/manual/digital/audio/stress/performance/offline acceptance, license inventory, clean self-contained portable ZIP/checksums/local help. All P0/P1 gates. |

Implement vertical slices; do not advance an untested feature to verified status. Work on independent code can continue while specific Windows/device checks remain unavailable. Stage B replaces the diagnostic window with working controls; waveform and advanced controls appear only when their implementations are available.


## Current Stage G development block

| Slice | Delivered | Observed result at `a00ee6c` |
| --- | --- | --- |
| G4 — Format coverage | Owned RF64/WMA v2/DSF/DSDIFF fixtures and deterministic DSD generator | 21 real native profiles passed; required remaining full profiles remain open |
| G5 — Complete backup | Validated no-overwrite ZIP database/settings backup, retained-original rollback restore and WPF action | 13 new real file/SQLite cases and EN/RU/extracted actual-model restore passed |
| G6 — Storage/artwork failures | Real ACL/file locks/partial restore rollback; corrupt/large cover and bounded portrait thumbnails | Owned Windows failures/recovery/source preservation passed; full disk/huge-tag isolation remain open |
| G7 — Performance evidence | Actual 100k production query/WPF pages and 9987-row production scrolling, raw CPU/resources | All hosted warm samples met 250 ms search/100 ms scroll comparisons; reference/cold/steady/device acceptance remains open |

[run 37418131090](https://github.com/kurasis/MPswift/actions/runs/37418131090) and [exact reports](evidence/stage-g-formats-backup-performance-windows.json) retain exact provenance. Continue remaining RELEASE_ACCEPTANCE.md workflows; these slices do not declare version 1.0 complete.


G8/G9 are implemented and the observed Windows cases passed at `0ce6a9b` in [run 37423428788](https://github.com/kurasis/MPswift/actions/runs/37423428788); [exact evidence](evidence/stage-g8-g9-windows.json). G8 covers owned HE-AAC/APE/SV8/hybrid/rates, actual WMA lossless/Pro and >4 GiB RF64. G9 covers actual full disk/read-only fallback/migration interruption/watcher overflow/huge tags. Remaining G10–G13 workflows concern clean Windows 11/offline/accessibility/DPI, actual endpoints/digital/listening, sustained playback/reference resources and licensing/version 1.0. Actual Windows N/power-loss and untested codec combinations remain explicit acceptance prerequisites.


| Next slice | Implementation/workflow | Acceptance boundary |
| --- | --- | --- |
| G10 — Desktop/offline | Actual accessibility/focus/layout/DPI policy and owned self-contained first-run/restart + positive-controlled PID/descendant ETW runner | Windows 11/no-runtime/standard-user/disconnected prerequisites recorded; physical Narrator/DPI and full desktop baseline remain separate |
| G11 — Output/digital verification | Shared/exclusive actual WASAPI lifecycle/track changes and bounded loopback boundary capture, with source hashes and error evidence | Probe/no-endpoint is not playback; endpoint/digital/listening/hotplug/sleep require actual devices |
| G12 — Sustained playback/resources | Two-hour output and 1000 real transitions with reference hardware/resource/CPU observations | Decoder preparation stress is not output soak |
| G13 — Distribution acceptance | Licensing/source/notices review and approved version 1.0 | Development prereleases do not authorize licensing purchases/stable acceptance |

G10/G11 workflows are implemented. Hosted Windows G10 EN/RU first/restart and controlled ETW plus G11 native Probe pass at `dec7664` in [run 37435560367](https://github.com/kurasis/MPswift/actions/runs/37435560367); [exact evidence](evidence/stage-g10-g11-windows.json). Zero enabled hosted outputs leaves actual shared/exclusive/capture/listening gates open. G12 and G13 are the next implementation/workflow slices; clean Windows 11, hardware and manual acceptance remain mandatory.
