# Owned extended format fixtures

All audio under `tests/fixtures/audio-extended` is generated from the project's
CC0 440 Hz opposite-phase PCM16 signal (SHA-256 in the manifest). No third-party
music is included. Encoders are development tools, never application dependencies
or package inputs. Do not infer permission to distribute encoder binaries.

`scripts/Generate-ExtendedFixtures.py` requires a built AudioSmoke tool and the
following pinned encoder inputs. Fetch via HTTPS, verify these SHA-256 values,
extract into an ignored owned development folder, and use the executable paths
with `--fdkaac`, `--mac`, `--mpcenc`, `--wavpack`. Outputs require a new directory.

| Input | HTTPS source | SHA-256 |
| --- | --- | --- |
| libfdk-aac 2.0.3 | https://codeload.github.com/mstorsjo/fdk-aac/tar.gz/refs/tags/v2.0.3 | e25671cd96b10bad896aa42ab91a695a9e573395262baed4e4a2ff178d6a3a78 |
| fdkaac 1.0.6 | https://codeload.github.com/nu774/fdkaac/tar.gz/refs/tags/v1.0.6 | ed34c8dcae3d49d385e1ceaa380c5871cda744402358c61bcb49950a25bfae58 |
| Monkey's Audio 3.99.6 port | https://codeload.github.com/fernandotcl/monkeys-audio/tar.gz/bac9b6bb4714a6de85a990a6916d1abc4c171271 | 934dcb8a025410b925a59681b80ea44b59806a5be720aa365cdf01d8a0414b04 |
| Musepack tools r495-3 | https://deb.debian.org/debian/pool/main/libm/libmpc/musepack-tools_0.1~r495-3_amd64.deb | a51b58499e3d43972c7187fa5c25862df2c780f3775783211a3f3011c317f747 |
| WavPack 5.8.1 | https://deb.debian.org/debian/pool/main/w/wavpack/wavpack_5.8.1-1_amd64.deb | d2de507557a21e996a64b80b58d537e2f48603e33c5e186a2f9fce88ee299dcb |

Build FDK with CMake Release, `BUILD_SHARED_LIBS=OFF`, and an owned install prefix.
Compile fdkaac's `src/*.c` except `compat_win32.c`, linking the installed static
FDK library, `libstdc++` and `libm`. Define the POSIX HAVE_STDINT_H, HAVE_INTTYPES_H,
HAVE_UNISTD_H, HAVE_SIGACTION, HAVE_FSEEKO, HAVE_ICONV, HAVE_LANGINFO_H and
HAVE_ENDIAN_H configuration macros as 1. Generate `src/version.h` with
`static const char fdkaac_version[] = "1.0.6";`. Include FDK's `include` directory.
Build the pinned Monkey port with `ENABLE_ASSEMBLY=OFF`; use `mac`. Extract the
Debian tools using `dpkg-deb -x` without installing system packages. They require
compatible libmpcdec/libwavpack development-host libraries. FFmpeg 7.1.5 prepares
the additional sample-rate/channel cases and independently verifies explicit
HE-AAC/HE-AACv2 MP4 profiles. Encoder revisions can change container hashes;
regeneration records the actual new hashes for review.

Windows smoke independently generates owned WMA lossless/Pro using the pinned
BASSWMA encoder, validates the ASF codec tags (0x0163/0x0162), checks lossless PCM
against the quantized source, and performs bounded 64-bit reads/seeks in a real
sparse NTFS RF64 larger than 4 GiB. Sparse file size is logical size, not a claim
of allocating 4 GiB of physical data. Windows N and actual endpoint playback
remain separate required environments.

## Long-duration fixture follow-up

`tests/fixtures/audio-long` contains two owned CC0 sources lasting **7201 seconds**: AAC-LC mono 48 kHz M4B and normal stereo 48 kHz APE. Generate a new directory with `Generate-LongFixtures.py --mac <pinned-built-mac>`. The manifest records source/file SHA-256, encoder revisions/arguments and independent FFprobe M4B facts. APE's source is a 1,382,592,044-byte sparse PCM16 WAV with three actual one-second owned tone markers; its float decode range is **2,765,184,000 bytes**, above Int32. APE is physically small because the intervals are silent; this does not prove an encoded APE file larger than 4 GiB. M4B repeats the owned left channel to avoid opposite-phase mono cancellation.

`--long-formats` hashes unchanged sources, checks native duration/format and bounded samples at 0.25/3600.25/7199.25 seconds, exact tail/end/disposal, exclusive handle release and production backend prepare/seek/stop without starting output. The 16 KiB decode buffer never grows with duration. This is long-file decoding, **not** a two-hour device soak or long-file waveform measurement. Windows observation is pending main CI. No encoder/input PCM is shipped in the application ZIP.
