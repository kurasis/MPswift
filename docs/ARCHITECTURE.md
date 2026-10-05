# Architecture — Stage A

Two production projects are established. `Player.Core` contains pure C# rules, with no WPF, native, database or dispatcher references. `Player.App` owns WPF presentation and platform services. The current domain slice validates offline local Windows file paths and logical seek/CUE time bounds. Actual drive type/cloud state checks stay in Windows code; lexical validation alone does not prove a file is local or hydrated.

The WPF composition root injects `INativeDiagnostics` into its toolkit view model. Views contain layout only. Native validation runs off the UI dispatcher and displays actionable dependency results. The future `IAudioPlayer`/coordinator/storage/waveform interfaces will be introduced with their first functional slices in B–D, rather than declaring empty service APIs in A.

`Player.AudioSmoke` is a CLI development harness that links the same native loading/session source as the app. It is not a third production layer, and `BassSmokeSession` is not the production playback engine. Native mutations are synchronous and owned by the calling command thread. Its persistent decode mixer renders float PCM to shared WASAPI in the negotiated endpoint mix format. Source gain is 0.1 for smoke output; generated tone amplitude is 0.05. EQ/gain policy for the finished app remains Stage D work.

The output callback has a strongly retained delegate, performs native decode and bounded atomic counters, and records failures for the command thread. It performs no logging, SQL or UI work and does not propagate managed exceptions. Output is freed/quiesced before mixer/source/BASS teardown. A teardown failure is surfaced rather than freeing a potentially live graph. Loaded libraries live for the process lifetime and are not unloaded while P/Invoke stubs can still reference them.

`NativeLibraryBootstrap` validates exact approved files against the manifest before loading. A resolver maps ManagedBass imports to already-loaded absolute paths under the application directory. It does not search working directories, media folders or PATH. Packages and official archives are separately pinned. Runtime capability claims require Windows execution, not merely hashes or compilation.

The fixture generator emits a small PCM16 WAV, known-duration/peak/channel manifest and checksum. Decode checks exercise end-of-file and midpoint seek; output checks exercise source consumption, pause/resume and stop reset. They do not measure the audible timeline or gapless boundaries. Source SHA-256 and an exclusive read-only reopen check preservation and handle release.

Planned production threading: short dispatcher updates, serialized native engine commands, bounded realtime callbacks, bounded metadata/waveform/database workers and generation IDs to reject stale results. Database, cache, settings, playback history and portable storage are not yet implemented. See specification Sections 6–15 for the remaining contracts.
