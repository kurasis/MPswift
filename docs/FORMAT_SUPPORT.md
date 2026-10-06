# Audio format support — Stage E implementation and verification matrix

Pinned base and FLAC/Opus/ALAC/AAC add-ons are inventoried in [the native manifest](../native/manifest.json). Seventeen real generated [fixtures](../tests/fixtures/audio/manifest.json) exercise native decode/seek/end/disposal in Windows CI. Stage B Windows native integration passed for all fourteen profiles; exact results are retained in [test evidence](TEST_RESULTS.md). Device playback, listening and full profile acceptance are separate gates.

| Format/container | Priority | Candidate from specification | Verification status |
| --- | --- | --- | --- |
| MP3 | P0 | BASS | CBR and VBR/Xing fixtures; native decode/seek/end/disposal passed |
| WAV | P0 | BASS | PCM16/24/32/float32 fixtures; all four native decode/seek/end/disposal checks passed |
| AIFF/AIF | P0 | BASS | PCM16 fixture; native decode/seek/end/disposal passed |
| FLAC | P0 | BASSFLAC | 16/24-bit fixtures; native decode/seek/end/disposal passed |
| Ogg Vorbis | P0 | BASS | Vorbis fixture; native decode/seek/end/disposal passed |
| Opus / Ogg Opus | P0 | BASSOPUS | Opus fixture; native decode/seek/end/disposal passed |
| AAC / M4A | P0 | Approved AAC decoding path | AAC-LC ADTS/MP4 fixtures; native decode/seek/end/disposal passed; HE-AAC untested; distribution path unresolved |
| ALAC / M4A | P0 | BASSALAC or a verified bundled path | ALAC MP4 fixture; native decode/seek/end/disposal passed |
| WMA | P1 | Pinned BASSWMA | Provisioned; Windows Media Format system dependency explicit; native/profile/N acceptance unrun |
| APE | P1 | Pinned BASSAPE | Provisioned; actual codec/seek/large fixtures unrun |
| WavPack / WV | P1 | Pinned BASSWV | Lossless decode/seek/end/disposal passed; hybrid/correction-file unrun |
| Musepack / MPC | P1 | Pinned BASS_MPC | Upstream binary/notice provisioned; real profile fixture unrun, distribution review open |
| TTA | P1 | Pinned BASS_TTA | Lossless decode/seek/end/disposal passed; LGPL terms retained/review open |
| DSF / DFF | P1 | Pinned BASSDSD | Float PCM path only; provisioned, actual DSF/DFF conversion fixtures unrun; no native DSD claim |
| CUE + supported audio | P1 | Tokenizer/parser and bounded logical decoder sources | Domain and real logical bounds/seek/waveform/WPF passed; production callback CUE sample error 0 |
| M4B without DRM | P1 | Pinned AAC/MP4 path | AAC-LC decode/seek/end/disposal passed; chapters not implemented |
| MOD / XM / IT / S3M | P2 | BASS music/module API | Not implemented / not tested |
| MIDI | P2 | BASSMIDI + legally distributed local soundfont | Not implemented / not tested |
| AC-3 / DTS / other audio codecs | P2 | Individually approved add-ons | Not implemented / not tested |
| SACD ISO, archives, exotic game formats | Out of v1.0 | Separate proposal | Excluded from v1.0 |
| DRM-protected media | Excluded | None | Excluded from v1.0 |

Windows CI verified source sample rate/channels, duration, codec, applicable bit depth, decoded signal, seek/end and unchanged-source/handle-release checks for 17 representative committed fixtures. This covers those exact encodings only. No audio output mode or gapless combination is advertised as verified. The Linux cloud host cannot run Windows native code. Listening/device checks remain separate and not run.

Stage E provisions 13 development DLLs and extends the fixture manifest to 17. SHA-256/x64/package audits passed locally; this does not prove decode support. Expanded Windows checks remain unrun after hosted-runner acquisition failure. The historical fourteen-profile result is not silently extended to new binaries/profile combinations. Unverified required profiles remain release blockers.

Stage D/E [run 37367043601 attempt 2](https://github.com/kurasis/MPswift/actions/runs/37367043601/attempts/2) supersedes the earlier pending status: 17 real representative decode/seek/end/disposal profiles passed at `c8426e6`, including WV/TTA/M4B. Production callback PCM split-lossless and contiguous CUE measured maximum error 0. Endpoint capture/listening/latency and advertised lossy gapless combinations remain separate, unrun gates. Full evidence is [retained](evidence/stage-de-windows-native-ui.json).


G4 adds owned RF64 PCM16, WMA v2/ASF and deterministic first-order DSD64 DSF/DSDIFF fixtures (21 total). Actual decode/seek/end checks are pending Windows CI. DSF/DFF expectations are 88.2 kHz float PCM, not native DSD output. The generator creates fresh files only and does not change source music. FFprobe independently accepted both DSD containers; that result does not substitute for BASS decoding.
