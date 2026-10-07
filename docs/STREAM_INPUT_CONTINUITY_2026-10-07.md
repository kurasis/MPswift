# Decoder input lifetime and WavPack correction continuity

## Scope and finding

P2, conditional local data-integrity/availability hardening: `BassMixerGraph.OpenSource` previously released its canonical `LocalReadLease` after decoder creation while the native decoder continued reading. `BassWaveformService.Decode` retained only the primary input; BASSWV could discover a correction sidecar by pathname, and the waveform cache fingerprint omitted that sidecar. A writer with access to the media directory could replace/change later inputs or produce a stale correction-dependent waveform. Deterministic exploitation of native decoder implementation races is not claimed; this is neither privilege escalation nor an OS boundary against same-user malware.

## Minimal correction

`SourceReadPins` retains canonical main and optional correction read leases. Production preparation transfers ownership into `NativeStreamPins`; stream freeing/last-engine close releases it without changing the existing public integer-handle interfaces. Waveform decoding retains its own input owner throughout actual PCM reads. Native callbacks and their owners are rooted until native cleanup completes, including forced collection controls. Windows sharing blocks ordinary writes/replacement through any hardlink alias while the input is held. Existing local music/directory links remain supported.

WavPack uses the already verified/pinned BASSWV module's `BASS_WV_StreamCreateFileUserEx` export with callbacks over the exact held streams, rather than allowing implicit reopening of a `.wvc` pathname. The pinned archive's `c/basswv.h` declares one callback table and separate main/correction userdata (five arguments); header SHA-256 is `dc986e47edb407df954f7003dea89f895498117ae08b3af9c6fdaa3649bced6c`. Callback exceptions are contained, read buffers are bounded to 64 KiB and length/seek/read operations use their owned streams. WavPack content detection also covers an input renamed to another supported extension. An absent sidecar remains absent for that open decoder; a subsequent open discovers newly added correction normally. No plugin/DLL/dependency version is changed.

Waveform cache fingerprints include correction presence, canonical path, length and modification time; corrected/uncorrected data no longer share a key. Presence/modification are checked again before decode and cache publication. Existing cache schema and non-WavPack keys remain unchanged.

`LocalFileAccess` now resolves file links one hop at a time and checks directory ancestors before children. A remote link target is rejected by local-path policy before metadata queries through that target. Local link compatibility controls remain required. This is not a complete race-free guarantee against arbitrary concurrent directory tampering or proof of zero network activity under all OS behavior.

## Required observations

Cross-target locked Release compilation succeeds with zero warnings/errors. Linux passes 284 managed tests; it cannot execute BASS/Windows sharing. Disposable Windows CI now requires actual production WAV main-write/rename refusal, WavPack main/correction write/rename refusal, forced collection before PCM reads, frozen missing-sidecar selection, exact corrected reference PCM (including renamed WavPack), callback/handle release, and actual correction appearance/modification/removal waveform cache checks. Restricted file controls run synchronously under the existing administrator-disabled impersonated token; asynchronous waveform checks are ordinary disposable CI, not restricted-token evidence. Source fixture hashes and existing 216 malformed-input/lifecycle cases remain required. Results are pending for this exact source, not inferred from compilation.

The native decoders still execute in the player process. No publisher authentication, parser process sandbox, hardware output, clean Windows 11 or licensing acceptance is inferred. See [remaining gates](REMAINING_WORK.md).
