# Owned lossy profile frame and boundary observations

## Actual measurements

Windows run [37625963338](https://github.com/kurasis/MPswift/actions/runs/37625963338) passed its existing actual codec/profile/EOF checks; a separate WPF storage ACL test later rejected the run. These already-produced native observations remain scoped to those passed checks, not to an overall accepted build. Local FFprobe 7.1.5 packet/stream facts and independent Opus header/granule parsing were matched to each identical fixture SHA-256. [Exact evidence](evidence/lossy-profile-observation-2026-10-07.json).

The owned source contains 144,000 frames at 48 kHz (three seconds). All counts below are real decoded float bytes divided by channels and four, rather than rounded display durations.

| Owned profile | Native frames | Extra frames vs source | Independent trim facts |
| --- | ---: | ---: | --- |
| MP3 CBR | 144,000 | 0 | Encoded packet duration minus first skip/last discard equals 144,000 |
| MP3 VBR Xing/LAME | 144,000 | 0 | Encoded packet duration minus first skip/last discard equals 144,000 |
| Opus | 144,000 | 0 | Final granule minus Opus pre-skip equals 144,000 |
| AAC-LC ADTS | 144,384 | 384 | No presentation-duration equivalence inferred |
| AAC-LC MP4 / M4B | 145,024 | 1,024 | Container/source duration does not imply exact native trim |
| HE-AAC SBR MP4 | 146,752 | 2,752 | Independent FFprobe profile retained |
| HE-AAC v2 MP4 | 147,776 | 3,776 | Independent FFprobe profile retained |

The known AAC outputs do not establish gapless trim to the original presentation range. Adding guessed prefix/tail trimming could cut valid audio and is not implemented. A precise codec/container policy, delay/padding mapping and owner agreement precede such a playback change.

## Additional production mixer regression

The disposable native mixer check is expanded for these exact MP3 CBR/VBR and Opus fixtures: decode each independently to its verified 144,000-frame EOF; prepare two production sources with crossfade disabled; render blocks crossing the join; compare all 288,000 resulting frames with concatenated separately decoded PCM; require exact transition/EOF and released unchanged inputs. Maximum error must remain below 1e-6. The source implementation changes only test coverage. [Successful source run 37632708722](https://github.com/kurasis/MPswift/actions/runs/37632708722) observes **zero** maximum error for each of the three profiles, exact transition/EOF and released unchanged inputs. [Actual report hashes and results](evidence/security-completion-windows-2026-10-07.json).

This is not a general promise covering every encoder/tag/container, phase/trim equivalence with the original uncompressed source, listening or actual endpoint latency/capture. Physical output gapless acceptance remains open.

## Encoded APE larger than 4 GiB

The available pinned Monkey's Audio 3.99.6 development encoder has signed-32-bit `SetSeekByte(int nFrame, int nByteOffset)` and seek byte storage in its source (`src/MACLib/APECompressCreate.cpp`, `APEInfo.cpp`). The existing two-hour APE passes >Int32 decoded-position checks, but is physically small and cannot establish an encoded >4 GiB APE. Modern official source/download pages returned HTTP 406 in this cloud review, including an ordinary browser-header request. No encoder/source/hash was guessed, no installer was executed and no invalid frame concatenation is presented as a real large APE. This gate requires a verified suitable encoder/input plus actual Windows large-file decoding; current RF64 evidence cannot replace it.
