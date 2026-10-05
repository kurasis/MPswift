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

Implement vertical slices; do not advance an untested feature to verified status. Work on independent code can continue while specific Windows/device checks remain unavailable. Stage A's diagnostics window is intentionally labeled as a development tool, rather than a mock player.
