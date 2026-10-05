# Architecture — Stage A

Two production projects are established. `Player.Core` contains pure C# rules, with no WPF, native, database or dispatcher references. `Player.App` owns WPF presentation and platform services. The current domain slice validates offline local Windows file paths and logical seek/CUE time bounds. Actual drive type/cloud state checks stay in Windows code; lexical validation alone does not prove a file is local or hydrated.

The WPF composition root injects `INativeDiagnostics` into its toolkit view model. Views contain layout only. Native validation runs off the UI dispatcher and displays actionable dependency results. The future `IAudioPlayer`/coordinator/storage/waveform interfaces will be introduced with their first functional slices in B–D, rather than declaring empty service APIs in A.

`Player.AudioSmoke` is a CLI development harness that links the same native loading/session source as the app. It is not a third production layer, and `BassSmokeSession` is not the production playback engine. Native mutations are synchronous and owned by the calling command thread. Its persistent decode mixer renders float PCM to shared WASAPI in the negotiated endpoint mix format. Source gain is 0.1 for smoke output; generated tone amplitude is 0.05. EQ/gain policy for the finished app remains Stage D work.

The output callback has a strongly retained delegate, performs native decode and bounded atomic counters, and records failures for the command thread. It performs no logging, SQL or UI work and does not propagate managed exceptions. Output is freed/quiesced before mixer/source/BASS teardown. A teardown failure is surfaced rather than freeing a potentially live graph. Loaded libraries live for the process lifetime and are not unloaded while P/Invoke stubs can still reference them.

`NativeLibraryBootstrap` validates exact approved files against the manifest before loading. A resolver maps ManagedBass imports to already-loaded absolute paths under the application directory. It does not search working directories, media folders or PATH. Packages and official archives are separately pinned. Runtime capability claims require Windows execution, not merely hashes or compilation.

The fixture generator emits a small PCM16 WAV, known-duration/peak/channel manifest and checksum. Decode checks exercise end-of-file and midpoint seek; output checks exercise source consumption, pause/resume and stop reset. They do not measure the audible timeline or gapless boundaries. Source SHA-256 and an exclusive read-only reopen check preservation and handle release.

Planned production threading: short dispatcher updates, serialized native engine commands, bounded realtime callbacks, bounded metadata/waveform/database workers and generation IDs to reject stale results. Database, cache, settings, playback history and portable storage are not yet implemented. See specification Sections 6–15 for the remaining contracts.

## Stage B production slice

`SerializedAudioPlayer` owns a bounded command queue and dedicated native-context thread. Factory/open/play/pause/seek/dispose run on that owner; no native mutation occurs in WPF handlers. Immutable snapshots carry load generations and publication revisions. Coalesced pending loads/seeks replace obsolete work; stopped/disposed generations cannot autoplay after an older decode completes. A 100 ms engine poll runs only during playback; there is no permanent UI polling timer.

`PlaybackCoordinator` owns full entry order independently of the filtered WPF collection and remembers actually started entries. Track identities may be shared by duplicate occurrences; entry identities never are. Removing a playing row retains active metadata/playback and the next source position. Stage D adds queue/repeat/shuffle and persistent output scheduling.

`BassAudioBackend` owns decoder/plugins/source/mixer/shared WASAPI, strongly retains its callback, and quiesces output before freeing resources. The callback only reads float mixer data, applies smoothed linear app gain/mute and signals native errors through atomic fields. Source position uses mixer history adjusted for the WASAPI buffer. No callback allocations, I/O, logging, UI calls or blocking locks. Stage B closes/rebuilds output for track/seek boundaries, so it makes no gapless claim.

`MediaImportService` validates local paths before reads, skips reparse/offline folder recursion and emits bounded batches from one background metadata worker. TagLib failures use real filenames and bounded details; no fake artwork/tags/waveform. `PlayerViewModel` marshals snapshots through the dispatcher with revision rejection; views handle input/layout only. Import does not autoplay. Resource-owned shutdown awaits import and audio disposal.
