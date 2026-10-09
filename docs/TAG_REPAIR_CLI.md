# MPswift Tag Repair

A separate offline Windows x64 command-line utility for physically repairing legacy music metadata. It does not require the player or a separately installed .NET runtime. Its manifest requests `asInvoker`, not administrator elevation.

Extract the entire separate ZIP. From cmd, first preview:

```cmd
MPswift.TagRepair.exe "C:\Music"
```

After reviewing the proposed changes, apply:

```cmd
MPswift.TagRepair.exe "C:\Music" --apply
```

The selected folder is scanned recursively. Add `--top-only` to inspect only that folder. Supported extensions: `.mp3`, `.flac`, `.cue`, case insensitive. Other files are left alone. Legacy CUE decoding defaults to Windows-1251; use `--cue-codepage 1252` or `--cue-codepage 866` only when that is the actual old encoding. UTF-8 and BOM-marked UTF-16/UTF-32 are detected before the fallback. Every readable CUE must contain FILE, TRACK AUDIO and INDEX 01 structure.

## What changes

- MP3 standard text tags are recovered and saved as ID3v2.4/UTF-8. An ID3v1-only file's standard metadata is copied before the lossy ID3v1 copy is removed. This can change compatibility with old devices that only understand ID3v1/ID3v2.3.
- FLAC standard text tags use UTF-8. Correct Unicode remains; saving already broken UTF-8 strings does not repair them, so recovery happens first.
- CUE text is saved as strict UTF-8 without BOM. TITLE/PERFORMER/SONGWRITER and REM GENRE/COMMENT values can be recovered; FILE references and INDEX/structural lines remain as decoded. The utility does not rename audio files or follow CUE references.

Recovery supports losslessly reversible Windows-1251 interpreted as Latin-1/Windows-1252 and common UTF-8 mojibake, including UTF-8 interpreted as Windows-1251. Longer recovered words confirm short Belarusian prepositions. Ordinary Latin names, intact Unicode and opaque identifiers are preserved. No heuristic can identify every ambiguous word or reconstruct bytes already replaced by `?`/U+FFFD; review the preview on mixed-language albums. Malformed/replacement-containing CUEs and oversized metadata are refused. Custom opaque/binary tag data is not interpreted as text.

For legacy words containing ASCII `i`/`I`, recovery requires at least four Cyrillic letters, no other Latin letters and an exact matching intact word in that file's basename (case insensitive). Directory names alone do not authorize this recovery. The ASCII letter remains unchanged: encoding repair does not rewrite spelling or replace `i` with Belarusian `і`. Ambiguous words without filename evidence remain for manual review.

## Original files and audio

For every changed file, the original is retained beside it as `filename.mpswift-<unique-id>.bak`. Each backup is flushed and its complete SHA-256 verified before the original is written. Temporary copies are accessible only to the current user; backups receive a protected copy of the original Windows access rules before source bytes are copied. Encrypted sources are not modified. Keep backups until you have checked the result in your preferred player; to restore, close players/editors and copy the corresponding `.bak` over its original filename.

Audio is never decoded or re-encoded. Independently parsed MPEG frame ranges and FLAC compressed data plus STREAMINFO are hashed before/after preparing the changed copy. Standard text fields, numeric metadata, identifiers and artwork are verified after TagLib saves. Unexpected changes refuse publication to the original.

MP3 can contain legacy padding/junk between declared ID3 metadata and MPEG audio. When the first bytes are not a frame header, a bounded 8 KiB search requires three complete consecutive compatible frames. The entire prefix remains included in the audio-region fingerprint; it is never removed to make a checksum match. Unrecognized/truncated data is reported with its filename, byte offset and first-byte diagnostic, and other files continue. A mixed successful/failed batch returns exit code `1` rather than terminating with an unhandled exception. This does not establish that every file with an `.mp3` extension is a supported MPEG container.

Writing uses the exact Windows file handle held with no write/delete sharing. Parent directories remain pinned; symbolic/hardlinked source files, directory links in traversal, unavailable/network paths and files held incompatibly by players/editors are skipped or refused. A reported write failure triggers restoration from the verified backup. Power loss/process termination during the final in-place write can still require manual restoration from that backup; this is not a filesystem transaction. Ctrl+C cancels between files/preparation phases and allows an active commit/rollback to finish.

No shell commands, ports, telemetry, startup entries or network requests are used. Limits: 100000 scanned directory entries, 64 folder levels, 4 MiB CUE, 32 MiB metadata containers, 65536 characters per standard text field. A failure is reported per file and the next file is attempted; an inaccessible/oversized directory stops the scan. Completed writes and backups remain available.

Exit codes: `0` success (including preview), `1` per-file errors, `2` arguments/folder/scan failure, `3` unsupported write platform, `130` cancellation. Source can run preview/unit checks on Linux; physical write guarantees and the distributed EXE are Windows-only.

## Dependencies and distribution

TagLibSharp 2.3.0 is pinned and remains a separate replaceable DLL. Original LGPL-2.1 COPYING/AUTHORS, NuGet metadata and self-contained .NET notices accompany the package. Application license choice and complete corresponding-source/third-party distribution review remain open; the package is unsigned. No BASS/native audio decoder is included or needed by this utility.
