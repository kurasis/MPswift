# Remaining work and acceptance boundaries

## Version 1.0 and per-user installer preparation (2026-10-08)

The owner explicitly requests version 1.0 and an installer. [Release notes](RELEASE_1_0.md) describe current-user installation, separate retained data, manual portable migration, unsigned publisher status and unchanged open acceptance/rights gates. Locked baseline before edits passed 299 tests with zero build warnings/errors. CI adds genuine isolated standard-user install/reinstall/uninstall and real installed EN/RU WPF gates. Final source-specific CI/release evidence is required; this preparation alone is not an observed pass.


Updated 2026-10-07. Latest observed implementation: `e118f3e691b0`, **0.2.90-dev.1**, [successful run 37654713897](https://github.com/kurasis/MPswift/actions/runs/37654713897). All four jobs pass: 299 tests per OS and copied Core, 216 bounded parser cases, secret controls, actual EN/RU/rebuilt-library/extracted WPF and independently downloaded 578-file/13-native ZIP. [Observed bounded snapshots/resource-shutdown completion](ENGINEERING_COMPLETION_2026-10-07.md#observed-completion) supersedes pre-CI pending notes for this implementation. Documentation-only successors still require their own normal CI and versioned ZIP.

## Work possible without owner hardware or commercial decisions

| Work | Completion boundary | Current status |
| --- | --- | --- |
| Diagnostic privacy regression | Configured local prefixes hidden in plain/JSON strings and slash variants; valid JSON, existing size limits and public interfaces retained | Confirmed gaps reproduced and corrected; [finding and tests](DIAGNOSTIC_PRIVACY_2026-10-07.md). Arbitrary secrets/unconfigured paths are outside this guarantee |
| Current documentation | Keep the live AC table/limitations separate from historical checkpoints; retain exact passed evidence and genuine unrun gates | This consolidation records the passed security run and supersedes old pending notes |
| Wider malformed-input corpus | Additional owned audio/tag/image/WVC/CUE/playlist/backup/DB inputs, reproducible failures and recovery assertions in disposable workers | All 216 current cases pass. Future corpus expansion remains ongoing audit work, not an exhaustible safety proof |
| File identity/loader investigation | Reproduce individual mutable-file/hardlink/WAL races, streaming/WVC continuity and native loader behavior; fix confirmed defects with compatibility tests | Directory/parser-open protection is implemented; cache read/touch continuity and atomic backup destinations are addressed by the [current follow-up](FILE_CONTINUITY_2026-10-07.md); nonrecursive staging cleanup and hash/copy continuity are addressed by the [backup follow-up](BACKUP_STAGING_2026-10-07.md); mutable-file link policy, frozen restore snapshots and held-object install/rollback are addressed by the [completion block](SECURITY_COMPLETION_2026-10-07.md); streaming/WVC inputs and correction-dependent waveform keys are addressed by the [stream follow-up](STREAM_INPUT_CONTINUITY_2026-10-07.md); same-user in-place/mapped edits and opaque loader questions retain explicit residual boundaries |
| Managed resource boundaries | Verify long-field combinations and native initialization/copy budgets without reducing documented valid limits | Ordinary maximum item dimensions and oversized-session preservation pass. Approved native snapshot copy now uses 128-page steps and a five-minute soft deadline; a stalled single native I/O call cannot be preempted |
| Additional legal format fixtures | Exact expected metadata/duration/seek/EOF for uncovered required profiles; generated large inputs kept out of Git | Existing profile and >4 GiB RF64 checks pass; encoded >4 GiB APE and other exact uncovered variants remain open |
| Secrets/privacy audit depth | History-aware secret checks without printing findings, more redaction cases and manual preview guidance | All locally present Git blobs (including unreachable/unpushed/binary data) and nonignored current files scanned by a repeatable tool with six cross-boundary families and no hits. Remote-only/unavailable objects, arbitrary/custom secrets and GitHub configuration remain outside this proof |
| License/source preparation | Identify matching upstream source/build material and SDK terms; retain exact provenance | Complete pinned TagLib source/build inventory and a 284-test Core-only regression check (which did not load TagLibSharp) are [prepared](TAGLIB_SOURCE_PREPARATION_2026-10-07.md); actual Windows replacement is [observed passing](TAGLIB_SOURCE_PREPARATION_2026-10-07.md#observed-windows-replacement-2026-10-07) and remains a mandatory CI gate; SDK/WinRT component terms and explicit official REDIST entries are [identified and retained](DISTRIBUTION_REVIEW.md); exact NuGet reproduction, source distribution and owner rights conditions stay open |

Exact owned MP3 CBR/VBR and Opus skip/padding counts are [observed independently](LOSSY_PROFILE_OBSERVATION_2026-10-07.md). Their actual production mixer regressions pass: two independently decoded 144,000-frame sources join into 288,000 frames with zero maximum PCM comparison error and released unchanged inputs. AAC examples have extra native decoded frames and exact trim is not implemented. General lossy gapless remains unclaimed; actual endpoint capture remains separate. The specification does not require claiming universal lossy gapless.

## Open engineering investigations

- **Snapshot-copy policy implemented:** the approved 128-page/five-minute native helper covers backup, recovery and pre-migration snapshots, with rollback/contended/SQLITE_FULL/read-only/live-WAL controls. The source-specific Windows/package and release checks pass. Hard preemption of a stalled native I/O call and new UI cancellation/retry behavior remain separate designs requiring agreement.
- **P2, precise codec/source provenance:** encoded APE larger than 4 GiB still needs a verified suitable encoder; the available legacy source has 32-bit seek offsets and modern official pages returned HTTP 406. Matching opaque addon implementations/advisories need vendor source/version evidence. AAC presentation trimming needs exact metadata mapping and approval before changing playback.
- **P3, broader static analysis:** normal configured locked Release compilation/analyzers pass with zero warnings/errors. The earlier 558 count covered only part of the solution. The current full [inventory/disposition review](ENGINEERING_COMPLETION_2026-10-07.md) fixes confirmed lifecycle defects; `AnalysisLevel=latest-all` remains unclean; [deduplicated before/after observations and limits](ANALYZER_FOLLOWUP_2026-10-07.md) cover all projects; API/style/async and ownership findings require individual review. No public API deletion, blanket suppression or speculative removal of JSON-instantiated types was used to force a pass.
- **Corresponding-source evidence:** the pinned complete TagLib source builds; old Linux Core-only tests did not load it. Actual loaded-library EN/RU Windows WPF validation is now a mandatory CI gate. Windows replacement/WPF validation now passes. Exact NuGet binary reproducibility and the actual corresponding-source distribution mechanism remain open; this preparation alone does not close licensing obligations.

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

Optional: light theme, mini-player, compact playlist rows, spectrum, M4B chapter UI, additional CUE/pregap exposure, module/MIDI/AC3/DTS support, bit-perfect mode, ASIO/native DSD/DoP, and tag editing. The requested per-user installer is now implemented with mandatory isolated standard-user lifecycle and installed WPF checks. These do not replace P0/P1 acceptance. Automatic updates, streaming/cloud/accounts/telemetry, DRM bypass, audio recording and ARM64/x86 release support are explicitly outside v1.0 scope.

Intentional current constraints are not unfinished features: ordinary M3U8 refuses lossy CUE-segment export; ReplayGain is tag-driven rather than library analysis; moving music roots requires explicit relink; waveform CUE clipping uses source-bucket resolution. Public historical diagnostic types are retained because no external-consumer absence is established. A large view model or repeated extension lists alone are not confirmed bugs.

G8 profile checks and G9 full-disk/read-only/migration-kill/watcher-overflow/cancellation checks have passed; old pre-CI paragraphs must not be used to reopen those same checks. [Release gates](RELEASE_ACCEPTANCE.md) and [distribution review](DISTRIBUTION_REVIEW.md) define the remaining stable-release boundaries.
