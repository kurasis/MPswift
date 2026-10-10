# MPswift — local user help

MPswift is a Windows x64 audio player. Version 1.0 is available as a per-user setup EXE and portable ZIP. Windows 11 is the release target; clean Windows 11/offline/hardware acceptance is not yet complete. No internet service, account, .NET installation or SDK is required by the self-contained app. Extract the whole folder; do not move only the executable. Start `MPswift.exe`. Setup installs into `%LOCALAPPDATA%/Programs/MPswift` without requesting elevation, with a Start menu shortcut and optional desktop shortcut; uninstall preserves data. See the packaged release notes and dependency review before redistributing.

## Desktop controls when minimized

Minimize the player (or hide it in the tray) to show a compact desktop strip with the current track, previous/stop/play-pause/next, shuffle, seek/time, mute and volume. It stays behind ordinary application windows and does not take focus when you use its controls. Drag the cover, title or empty background to move it; its position is saved and fitted into an available monitor. Click the arrow or double-click the title/cover to restore MPswift. The strip disappears when the main player returns and closes when MPswift exits. Settings → Behavior → Show desktop controls when minimized disables it. Music, playlists and queue use the same player as the main window.

## Files and playlists

Add local files/folders with the bottom buttons or Ctrl+O / Ctrl+Shift+O. Drop files or folders onto the track list to append them to the selected playlist. Drop folders onto the playlist tab strip to create one new playlist per folder, named after that folder, including music in subfolders. Multiple folders create separate tabs; loose files in a mixed drop append to the originally selected playlist. Nothing plays merely because it was imported. Use playlist actions for tabs, sorting, copying a path, Explorer, properties, local library, import/export and backups. Search filters the visible list without changing the playback sequence. Clear search before reorder/sort. Enter plays a selected row; Delete removes selected entries, preserving the audio files. Ctrl+A selects visible rows. Ctrl+N creates a tab; F2 renames with tab focus. Ctrl+Shift+Up/Down or drag changes manual order.

The library window manages explicitly chosen local roots, cancellable scans and paged literal Unicode search. Ratings and counted history are stored in the database. Artwork comes from local tags or adjacent cover/folder images. Remote/UNC/mapped-network sources and unavailable cloud placeholders are rejected. Scans/imports can keep confirmed results after cancellation; inspect technical details for partial failures.

M3U8 export preserves ordinary local paths, order and duplicates. PLS/M3U8/CUE imports are bounded and do not recursively import documents. Use the explicit legacy-encoding dialog for non-Unicode documents. CUE logical segments cannot be faithfully exported as ordinary M3U8; that export is refused. Relink independently validates the replacement and preserves stable IDs; it does not move or rewrite music.

## Track context menu and file information

Right-click a selected track to retain the multi-selection, or an unselected track to target it alone. The menu includes playback, add, Favorites, queue, file information/location, previous playback playlist, file clipboard, star rating, place after the current track, send/copy, entry removal and enable/disable. Favorites (Избранное in Russian) is a saved playlist; adding the same logical song again does not duplicate it. Sending tracks to another playlist creates new entries and keeps the originals. Place-after-current requires the active playback playlist and an unfiltered view. Previous playlist returns to the last playlist used as a playback source, not the last tab viewed.

F4 opens read-only information: General, Lyrics, ID3v1 and ID3v2 tabs, technical properties, ReplayGain, local cover and history. The path toolbar copies the path, opens Explorer (Alt+O) and navigates files. No tags are edited. Copy files to clipboard supplies actual file references for Explorer paste. Copy to folder creates new files only; existing filenames are retained and reported as errors. Selected M3U8 export similarly requires a new filename and refuses CUE segments.

Del removes playlist entries without touching audio. Ctrl+Del opens a physical-file list with Cancel selected by default, followed by Windows recycle confirmations. Deleting a CUE source affects all songs sharing that file. The loaded source must be changed before recycling; linked recycle paths are refused. Windows may additionally ask about permanent deletion when recycling is unavailable: inspect that system prompt before proceeding. Unavailable entries stay visible until you remove or relink them.

## Playback and sound

Space plays/pauses outside text/checkbox/slider controls. Alt+Left/Right selects Previous/Next. Ctrl+Left/Right seeks five seconds outside text controls; a focused waveform also accepts unmodified arrows/Home/End. Ctrl+Up/Down changes app gain by five percentage points. The seek slider and waveform expose a keyboard/Automation range. App mute/gain never changes Windows system volume or stored waveform.

Queued snapshots play before the playlist and survive source edits. Play next preserves the selected batch order; clear queue leaves the current song running. Previous uses actual-start history and restarts after three seconds. Manual Next escapes repeat-one. Shuffle uses a remaining-item bag; filtered view does not alter its source.

Audio settings select Windows default or an explicit endpoint and shared/exclusive mode. Changing output pauses playback. Unavailable exclusive mode reports an error; it never silently switches to shared mode. After an endpoint/default change, select the correct output and press Play explicitly. Actual device/hotplug/sleep acceptance remains open.

EQ, preamp, tag-only ReplayGain and equal-power crossfade use a float processing graph. Missing ReplayGain tags mean 0 dB. Positive EQ adds headroom; final overrange values saturate. Crossfade defaults off, clamps short tracks and bypasses contiguous CUE/repeat-one. This is not an advertised bit-perfect mode. Required lossy gapless combinations, high-rate/multichannel/specialist profiles and hardware continuity need their own recorded evidence; see FORMAT_SUPPORT.md.

## Windows and storage

A second launch activates the existing per-user window. Local paths append without autoplay; `--play` explicitly starts the first newly added row. `--` ends option parsing. Unknown options and arbitrary IPC commands are rejected. Standard media commands use Windows SMTC; OS availability is reported separately from actual physical key testing.

Tray actions show/hide, play/pause, next/previous and exit. Hiding keeps playback active. Closing exits by default; enable close-to-tray explicitly in appearance/behavior preferences. Language is English or Russian and changes immediately after Apply, including menus and accessible names. High contrast follows Windows. F1 opens local help; no browser is launched.

A packaged `portable.marker` selects `Data/` beside the app; remove the marker before starting to select `%LOCALAPPDATA%/MPswift/LocalAudioPlayer/`. An unwritable portable location requires an explicit per-user fallback choice. Keep the entire Data folder when moving a portable installation. Music is not copied into it, so moved absolute media paths may need relink. Closing persists state; restoring never starts audio.

The database/settings backup action is separate from the disposable `Cache/Waveforms` directory. Never delete the database to clear cache. Corrupt/newer storage is preserved and rejected; choose a valid backup for recovery. Local diagnostic logs rotate at five 2 MiB files, with bounded buffering and personal path prefix redaction. Inspect the text before sharing; the app never uploads logs. A disk error remains visible even if logging itself cannot write.

Package hashes are in SHA256SUMS.txt; `scripts/Verify-Candidate.ps1` in the source checkout audits the complete manifest. License metadata/notices are included under notices/, native/ and docs/. Their presence does not settle corresponding-source or commercial-use obligations. This candidate is unsigned; development ZIP prereleases are available, while stable 1.0 acceptance remains open. See RELEASE_ACCEPTANCE.md for the exact remaining acceptance list.

If settings are damaged or missing and a valid previous settings copy exists, startup offers explicit recovery. Choosing No exits without changing either copy. Recovery retains damaged settings under a separate `settings.json.preserved-*` name and restores the backup. Unsupported settings versions require a compatible application and are never silently downgraded. Database recovery remains a separate operation.


Use Backup in the action menu to save a complete local .zip containing playlists, queue, library, ratings, history and settings. Music, cache and logs are excluded. Restore backup validates the archive before replacing saved files, retains current database/settings separately, stops playback and restores without autoplay. Failed validation preserves current data. Choose a new backup filename; existing files are never overwritten. Legacy .db backups restore the database only. Restart after restoring a backup with another language.

## Settings and album headings

Use the gear in the title bar to open Settings. Choose English or Russian (applies immediately after Apply), show/hide album and folder headings, or choose close-to-tray behavior. Apply saves the choices; Cancel leaves them unchanged. The Sound section opens output device, EQ, ReplayGain and crossfade controls.

Dropping a folder onto the playlist tabs creates a playlist including its subfolders. Album headings use tags when present, otherwise folder names. Folder hints distinguish discs/subfolders, and counts reflect visible tracks. Search and reordering update headings without changing the playlist's playback order. The ellipsis menu groups playlist, selected-track, queue and file/backup actions.

## Whole-album FLAC and CUE

When a FLAC contains an entire album, keep its companion .cue in the same folder. Add the folder or the FLAC: a single valid CUE referencing that FLAC is expanded into named songs, without creating new audio files. A folder containing both the image and its CUE does not add the whole album twice.

For an existing whole FLAC row, select it (multiple images are supported), then use **⋯ → Selected tracks → Expand FLAC into songs from CUE**. If FILE names a missing WAV/APE or a renamed image, the importer first matches the audio/CUE filename stem, then accepts the sole FLAC in that same folder. Existing referenced files are never redirected; multiple unresolved candidates are refused. Missing, ambiguous, malformed or out-of-range automatic associations keep the original row. Settings → Legacy CUE / playlist encoding controls fallback after strict Unicode decoding (Windows-1251 by default; Windows-1252, DOS-866 or Unicode-only are available). The separate legacy import dialog can override this for one import. The final song duration comes from the real decoded FLAC length. Multi-file CUE sheets can still be imported explicitly. Files without timing metadata remain whole; silence is not guessed as song boundaries.

## Development build versions

The title bar and F1 help show the compiled version. Development builds use `1.0.<workflow run number>-dev.<attempt>`; version 1.0 uses `1.0.0+build.<run number>.<attempt>` in package metadata and 1.0.0 in the title; restarting a workflow increments its attempt. The ZIP, GitHub release, package manifest and app/core metadata identify the same source/build; stable UI shows 1.0.0 while file metadata records each workflow build. Local unnumbered builds use `1.0.0-dev.0`. Keep the existing Data folder when replacing a portable build.

## Playlist tabs and waveform

Right-click a playlist tab (or focus it and use Shift+F10) to rename, duplicate, delete, move, sort or export that playlist. Drag a tab before/after another tab; the amber marker shows where it will land. Hold near either edge to scroll overflowing tabs. Tab order is saved, and moving tabs keeps the playing source and queue. Dropping a music folder still creates a playlist.

The waveform now shows measured average PCM energy as its solid envelope and quieter peak context. Short loud transients do not fill every column. Stereo channels are analyzed independently before combining energy; opposite-phase material stays visible. It follows source audio, independent of app volume/EQ. Old cached waveforms regenerate automatically; refresh remains available in the action menu.

## Cache, startup and diagnostics

Settings now include four accent colors, energy/peak waveform display, startup repeat/shuffle defaults and active-session/position restoration. Restored sessions keep their repeat/shuffle state; defaults apply to a new or disabled session restore. Playlists and ratings always remain saved; no startup option autoplays. File/folder dialogs remember their last local directories.

Storage shows actual cache usage and a live 16–2048 MiB limit. Clear waveform cache removes only disposable peaks, including protection against earlier analysis refilling it. It preserves music, database, ratings and the waveform already on screen; another analysis regenerates peaks on demand. Local data/log locations are shown.

Use Preview and copy diagnostics in Settings or the action menu. The report shows compiled/native/deployed-decoder, requested/actual output, DSP/gain/mute and logging details, with configured personal prefixes hidden. Inspect the preview before copying; nothing is sent automatically. Redaction is bounded and cannot recognize every personal string in an arbitrary exception.

Remove duplicate entries is available from the playlist tab context menu and Playlist actions. The preview operates on the full playlist and keeps the first canonical-path/exact-CUE-bound occurrence; different songs within an album FLAC remain distinct. Cancel removes nothing. Confirmed removal affects playlist entries only, preserving source files/current playback/queued snapshots; a changed order requires a new preview. Power-state notifications pause/save the active session; playback resumes only after an explicit Play. Physical driver/sleep acceptance remains separate.
