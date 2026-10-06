# Local Audio Player — local user help

This is a development candidate for Windows x64. Windows 11 is the release target; clean Windows 11/offline/hardware acceptance is not yet complete. No internet service, account, .NET installation or SDK is required by the self-contained app. Extract the whole folder; do not move only the executable. Start `Player.App.exe`. See `DEVELOPMENT-ONLY.txt` and the dependency review before distributing a copy.

## Files and playlists

Add local files/folders with the bottom buttons or Ctrl+O / Ctrl+Shift+O. Drop files onto the window. Nothing plays merely because it was imported. Use playlist actions for tabs, sorting, copying a path, Explorer, properties, local library, import/export and backups. Search filters the visible list without changing the playback sequence. Clear search before reorder/sort. Enter plays a selected row; Delete removes selected entries, preserving the audio files. Ctrl+A selects visible rows. Ctrl+N creates a tab; F2 renames with tab focus. Ctrl+Shift+Up/Down or drag changes manual order.

The library window manages explicitly chosen local roots, cancellable scans and paged literal Unicode search. Ratings and counted history are stored in the database. Artwork comes from local tags or adjacent cover/folder images. Remote/UNC/mapped-network sources and unavailable cloud placeholders are rejected. Scans/imports can keep confirmed results after cancellation; inspect technical details for partial failures.

M3U8 export preserves ordinary local paths, order and duplicates. PLS/M3U8/CUE imports are bounded and do not recursively import documents. Use the explicit legacy-encoding dialog for non-Unicode documents. CUE logical segments cannot be faithfully exported as ordinary M3U8; that export is refused. Relink independently validates the replacement and preserves stable IDs; it does not move or rewrite music.

## Playback and sound

Space plays/pauses outside text/checkbox/slider controls. Alt+Left/Right selects Previous/Next. Ctrl+Left/Right seeks five seconds outside text controls; a focused waveform also accepts unmodified arrows/Home/End. Ctrl+Up/Down changes app gain by five percentage points. The seek slider and waveform expose a keyboard/Automation range. App mute/gain never changes Windows system volume or stored waveform.

Queued snapshots play before the playlist and survive source edits. Play next preserves the selected batch order; clear queue leaves the current song running. Previous uses actual-start history and restarts after three seconds. Manual Next escapes repeat-one. Shuffle uses a remaining-item bag; filtered view does not alter its source.

Audio settings select Windows default or an explicit endpoint and shared/exclusive mode. Changing output pauses playback. Unavailable exclusive mode reports an error; it never silently switches to shared mode. After an endpoint/default change, select the correct output and press Play explicitly. Actual device/hotplug/sleep acceptance remains open.

EQ, preamp, tag-only ReplayGain and equal-power crossfade use a float processing graph. Missing ReplayGain tags mean 0 dB. Positive EQ adds headroom; final overrange values saturate. Crossfade defaults off, clamps short tracks and bypasses contiguous CUE/repeat-one. This is not an advertised bit-perfect mode. Required lossy gapless combinations, high-rate/multichannel/specialist profiles and hardware continuity need their own recorded evidence; see FORMAT_SUPPORT.md.

## Windows and storage

A second launch activates the existing per-user window. Local paths append without autoplay; `--play` explicitly starts the first newly added row. `--` ends option parsing. Unknown options and arbitrary IPC commands are rejected. Standard media commands use Windows SMTC; OS availability is reported separately from actual physical key testing.

Tray actions show/hide, play/pause, next/previous and exit. Hiding keeps playback active. Closing exits by default; enable close-to-tray explicitly in appearance/behavior preferences. Language is English or Russian and applies after restart. High contrast follows Windows. F1 opens local help; no browser is launched.

A packaged `portable.marker` selects `Data/` beside the app; remove the marker before starting to select `%LOCALAPPDATA%/MPswift/LocalAudioPlayer/`. An unwritable portable location requires an explicit per-user fallback choice. Keep the entire Data folder when moving a portable installation. Music is not copied into it, so moved absolute media paths may need relink. Closing persists state; restoring never starts audio.

The database/settings backup action is separate from the disposable `Cache/Waveforms` directory. Never delete the database to clear cache. Corrupt/newer storage is preserved and rejected; choose a valid backup for recovery. Local diagnostic logs rotate at five 2 MiB files, with bounded buffering and personal path prefix redaction. Inspect the text before sharing; the app never uploads logs. A disk error remains visible even if logging itself cannot write.

Package hashes are in SHA256SUMS.txt; `scripts/Verify-Candidate.ps1` in the source checkout audits the complete manifest. License metadata/notices are included under notices/, native/ and docs/. Their presence does not settle corresponding-source or commercial-use obligations. This candidate is unsigned; no public release was made. See RELEASE_ACCEPTANCE.md for the exact remaining acceptance list.

If settings are damaged or missing and a valid previous settings copy exists, startup offers explicit recovery. Choosing No exits without changing either copy. Recovery retains damaged settings under a separate `settings.json.preserved-*` name and restores the backup. Unsupported settings versions require a compatible application and are never silently downgraded. Database recovery remains a separate operation.
