# Remaining work and acceptance boundaries

Updated 2026-10-07. The original P0/P1 specification remains the release target. The latest observed baseline is `f8716db56be1`, development build `0.2.77-dev.1`, [successful main run 37614245760](https://github.com/kurasis/MPswift/actions/runs/37614245760). Both OSs passed 270 tests; all 216 bounded parser cases, restricted Windows file controls, EN/RU/extracted WPF and the 577-file/13-native package audit passed. The ZIP/source versions were independently verified. Subsequent source changes need their own CI; this record does not pre-approve them.

## Work possible without owner hardware or commercial decisions

| Work | Completion boundary | Current status |
| --- | --- | --- |
| Diagnostic privacy regression | Configured local prefixes hidden in plain/JSON strings and slash variants; valid JSON, existing size limits and public interfaces retained | Confirmed gaps reproduced and corrected; [finding and tests](DIAGNOSTIC_PRIVACY_2026-10-07.md). Arbitrary secrets/unconfigured paths are outside this guarantee |
| Current documentation | Keep the live AC table/limitations separate from historical checkpoints; retain exact passed evidence and genuine unrun gates | This consolidation records the passed security run and supersedes old pending notes |
| Wider malformed-input corpus | Additional owned audio/tag/image/WVC/CUE/playlist/backup/DB inputs, reproducible failures and recovery assertions in disposable workers | Existing 216 cases pass; further corpus expansion is open, not an exhaustible safety proof |
| File identity/loader investigation | Reproduce individual mutable-file/hardlink/WAL races, streaming/WVC continuity and native loader behavior; fix confirmed defects with compatibility tests | Directory/parser-open protection is implemented; cache read/touch continuity and atomic backup destinations are addressed by the [current follow-up](FILE_CONTINUITY_2026-10-07.md); nonrecursive staging cleanup and hash/copy continuity are addressed by the [backup follow-up](BACKUP_STAGING_2026-10-07.md); mutable-file link policy, frozen restore snapshots and held-object install/rollback are addressed by the [completion block](SECURITY_COMPLETION_2026-10-07.md); streaming/WVC inputs and correction-dependent waveform keys are addressed by the [stream follow-up](STREAM_INPUT_CONTINUITY_2026-10-07.md); same-user in-place/mapped edits and opaque loader questions retain explicit residual boundaries |
| Managed resource boundaries | Verify additional long-field combinations and native initialization/copy budgets without reducing documented valid limits | Ordinary maximum item dimensions and oversized-session preservation pass; further adversarial combinations open |
| Additional legal format fixtures | Exact expected metadata/duration/seek/EOF for uncovered required profiles; generated large inputs kept out of Git | Existing profile and >4 GiB RF64 checks pass; encoded >4 GiB APE and other exact uncovered variants remain open |
| Secrets/privacy audit depth | History-aware secret checks without printing findings, more redaction cases and manual preview guidance | All reachable local history scanned for six high-confidence pattern families with positive controls and no hits; unpushed/dangling refs, binaries, arbitrary/custom secrets and GitHub secret configuration remain outside this proof |
| License/source preparation | Identify matching upstream source/build material and SDK terms; retain exact provenance | Complete pinned TagLib source/build inventory and a 284-test rebuilt-DLL Linux replacement check are [prepared](TAGLIB_SOURCE_PREPARATION_2026-10-07.md); exact NuGet reproduction/Windows replacement/distribution mechanism and SDK/WinRT component terms and explicit official REDIST entries are now [identified and retained](DISTRIBUTION_REVIEW.md); the remaining source/binary/distribution and owner rights conditions stay open |

Exact owned MP3 CBR/VBR and Opus skip/padding counts are [observed independently](LOSSY_PROFILE_OBSERVATION_2026-10-07.md); their added production mixer regression awaits source CI. AAC examples have extra native decoded frames and exact trim is not implemented. General lossy gapless remains unclaimed. Generated fixtures and callback analysis are possible independently; actual endpoint capture remains separate. The specification does not require claiming universal lossy gapless.

## Checks requiring a suitable Windows image, physical setup or owner decision

| Gate | Remaining evidence |
| --- | --- |
| AC-001/002/034 | Clean supported Windows 11; standard user; no SDK/.NET; network disconnected before extraction/first launch; WAV/MP3/FLAC/CUE/library/settings/restart |
| AC-003/005/017–022/029 | Real Shared/Exclusive output, busy/unavailable output, gain/latency/digital boundary capture, listening, device unplug/default changes/physical sleep and media keys |
| AC-004/037 | Actual Windows N/optional-media conditions and all required representative profile combinations; no claim based on extension alone |
| AC-013/030/036 | Reference-PC cold/steady performance; keyboard/Narrator, physical 100/150/200% DPI and monitor removal/moves, EN/RU/high-contrast visual acceptance |
| AC-031/032/039 | Actual power-loss/storage-failure recovery and manual Windows 11 recovery/fallback dialogs |
| AC-033 | Two hours of actual full-player endpoint output plus 1000 transitions with hardware/dataset and CPU/memory/handle measurements |
| AC-035 | Owner intended use/app license, BASS/addon rights, AAC GPL/licensed path, matching TTA/TagLib source/relinking and SDK redistribution obligations |
| Publisher authenticity | Owner certificate/key-storage/signing decisions; current hashes prove integrity relative to trusted metadata, not publisher identity |

Production parser process isolation is not implemented. Test Job resource limits are not an OS-rights sandbox. Moving shipped decoders to another process or replacing decoder/install/update behavior requires a concrete design and owner agreement. Closed addon source/embedded codec version gaps need vendor evidence; no CVE is inferred from missing provenance.

## Optional P2 and excluded scope

Optional: light theme, mini-player, compact playlist rows, spectrum, M4B chapter UI, additional CUE/pregap exposure, module/MIDI/AC3/DTS support, bit-perfect mode, ASIO/native DSD/DoP, tag editing and an installer. These do not replace P0/P1 acceptance. Automatic updates, streaming/cloud/accounts/telemetry, DRM bypass, audio recording and ARM64/x86 release support are explicitly outside v1.0 scope.

Intentional current constraints are not unfinished features: ordinary M3U8 refuses lossy CUE-segment export; ReplayGain is tag-driven rather than library analysis; moving music roots requires explicit relink; waveform CUE clipping uses source-bucket resolution. Public historical diagnostic types are retained because no external-consumer absence is established. A large view model or repeated extension lists alone are not confirmed bugs.

G8 profile checks and G9 full-disk/read-only/migration-kill/watcher-overflow/cancellation checks have passed; old pre-CI paragraphs must not be used to reopen those same checks. [Release gates](RELEASE_ACCEPTANCE.md) and [distribution review](DISTRIBUTION_REVIEW.md) define the remaining stable-release boundaries.
