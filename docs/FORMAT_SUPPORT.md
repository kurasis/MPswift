# Audio format support — Stage B verification matrix

Pinned base and FLAC/Opus/ALAC/AAC add-ons are inventoried in [the native manifest](../native/manifest.json). Fourteen real generated [fixtures](../tests/fixtures/audio/manifest.json) exercise native decode/seek/end/disposal in Windows CI. Stage B Windows native integration passed for all fourteen profiles; exact results are retained in [test evidence](TEST_RESULTS.md). Device playback, listening and full profile acceptance are separate gates.

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
| WMA | P1 | Verified BASS/Windows path or approved decoder | Not implemented / not tested |
| APE | P1 | BASSAPE | Not implemented / not tested |
| WavPack / WV | P1 | BASSWV | Not implemented / not tested |
| Musepack / MPC | P1 | Approved MPC add-on | Not implemented / not tested |
| TTA | P1 | Approved TTA add-on | Not implemented / not tested |
| DSF / DFF | P1 | BASSDSD | Not implemented / not tested |
| CUE + supported audio | P1 | Application CUE parser + existing decoder | Not implemented / not tested |
| M4B without DRM | P1 | Verified MP4/AAC/ALAC path | Not implemented / not tested |
| MOD / XM / IT / S3M | P2 | BASS music/module API | Not implemented / not tested |
| MIDI | P2 | BASSMIDI + legally distributed local soundfont | Not implemented / not tested |
| AC-3 / DTS / other audio codecs | P2 | Individually approved add-ons | Not implemented / not tested |
| SACD ISO, archives, exotic game formats | Out of v1.0 | Separate proposal | Excluded from v1.0 |
| DRM-protected media | Excluded | None | Excluded from v1.0 |

Windows CI verified source sample rate/channels, duration, codec, applicable bit depth, decoded signal, seek/end and unchanged-source/handle-release checks for the fourteen committed fixtures. This covers those exact encodings only. No audio output mode or gapless combination is advertised as verified. The Linux cloud host cannot run Windows native code. Listening/device checks remain separate and not run.
