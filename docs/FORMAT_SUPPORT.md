# Audio format support — no runtime support claims yet

Pinned development native files: BASS **2.4.18.3**, BASSmix **2.4.13**, BASSWASAPI **2.4.4.1**. Exact official archive/DLL hashes are in [the native manifest](../native/manifest.json). Only these three DLLs are provisioned at Stage A. Format-specific add-ons and their licenses/fixtures are future work; mechanism candidates below are not shipped-support claims.

| Format/container | Priority | Candidate from specification | Verification status |
| --- | --- | --- | --- |
| MP3 | P0 | BASS | Not implemented / not tested |
| WAV | P0 | BASS | Generated 48 kHz stereo PCM16 fixture independently validated; native decode/play not run on Linux |
| AIFF/AIF | P0 | BASS | Not implemented / not tested |
| FLAC | P0 | BASSFLAC | Not implemented / not tested |
| Ogg Vorbis | P0 | BASS | Not implemented / not tested |
| Opus / Ogg Opus | P0 | BASSOPUS | Not implemented / not tested |
| AAC / M4A | P0 | Approved AAC decoding path | Not implemented / not tested |
| ALAC / M4A | P0 | BASSALAC or a verified bundled path | Not implemented / not tested |
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

No shared/exclusive audio mode, gapless combination or source profile is currently verified by Windows execution in the cloud host. Windows CI will record native WAV decoding independently of listening/device checks.
