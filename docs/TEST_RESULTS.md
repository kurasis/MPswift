# Test evidence — Stages A, B and C

## Local execution

Executed on **2026-10-05**, Debian GNU/Linux 13 x64 cloud host, .NET SDK **10.0.401**, PowerShell **7.6.6**. No Windows runtime or audio device is available in this host.

| Check | Outcome | Evidence |
| --- | --- | --- |
| SDK download integrity | Passed | Official .NET release metadata SHA-512 matches Linux SDK archive; pinned in `scripts/Install-Cloud.sh` |
| PowerShell download integrity | Passed | Official release `hashes.sha256` matches Linux archive; pinned in cloud script |
| Native archive/DLL/architecture provisioning | Passed, file validation only | `Setup-Native.ps1` verified all three official archives, x64 DLL SHA-256 and PE machine headers; repeat invocation verified retained files |
| Exact managed package restore | Passed | NuGet restore established all four committed lock files; subsequent `Build.ps1` used locked restore |
| Release build, including WPF cross-build | Passed | Four projects compiled, **0 warnings / 0 errors** |
| Core domain suite | Unit tested | **22 passed, 0 failed, 0 skipped**, xUnit/VSTest; results under `artifacts/test-results/*.trx` |
| Cloud installation instructions | Passed, current-instance repeatability | `bash scripts/Install-Cloud.sh` completed activation, native verification, locked build and tests using retained verified tools |
| Self-contained development publish | Passed, cross-publish only | `Publish-Development.ps1` produced `artifacts/publish/win-x64`; required runtime, WPF, executable, native DLL and companion files checked (approximately 144 MiB) |
| Generated WAV and Unicode output path | Independently verified | Smoke tool generated PCM16 stereo 48 kHz / 3 s tone; Python `wave`/`struct`/SHA-256 verified format, duration, amplitude and opposite-phase channels |
| Native smoke on Linux | Not run | `--probe` returned **exit 3**, JSON status `not-run`, explicitly requiring Windows x64 |
| WPF launch/visual/binding/accessibility checks | Not run | Windows desktop unavailable |
| BASS/WASAPI native execution and real output | Not run locally | Windows/native/device environment required |
| Audible playback/digital capture/gapless/device changes | Not run | No listening or output measurement occurred |
| Clean Windows/offline first launch | Not run | Cross-publishing does not establish these checks |

Representative commands from the checkout root:

```bash
bash scripts/Install-Cloud.sh
source /workspace/toolchains/activate.sh
pwsh -NoProfile -File scripts/Build.ps1
pwsh -NoProfile -File scripts/Publish-Development.ps1
dotnet tools/Player.AudioSmoke/bin/Release/net10.0/Player.AudioSmoke.dll --generate-fixture 'artifacts/fixture-check/generated tone Музыка 🎵.wav'
dotnet tools/Player.AudioSmoke/bin/Release/net10.0/Player.AudioSmoke.dll --probe
```

The fixture's reproducible SHA-256 is `265bfc54a7b0bfc2b75cf63c427873d00b516bbbb9dcaae17346de76cc4757fb`. Its manifest includes provenance, CC0 dedication and expected format/peak/duration. Generated files stay outside Git.

Build/publish issues found during setup are resolved: WPF requires explicit `System.IO` imports in shared audio code; self-contained runtime packs must be restored; the Windows RID is scoped to `Player.App` instead of propagated to Core by global CLI options. Final scripts use consistent locked assets and preserve failing command status.

## Windows CI and manual checks

`.github/workflows/build.yml` establishes Linux/Windows Release builds and tests, plus Windows `Smoke.ps1` native load/decode/end/seek/disposal checks. **Observed: both jobs passed** in [run 37344651363](https://github.com/kurasis/MPswift/actions/runs/37344651363), source commit `0226ea6b10c03f8517b13a3e025fba488d5ecda2`. GitHub Actions API reported completed/success for Linux and Windows jobs, including the Windows native step. The [Windows job](https://github.com/kurasis/MPswift/actions/runs/37344651363/job/111880064076) successfully ran pinned native provisioning, locked Release build/core tests, and `Smoke.ps1` load/decode/seek/end/disposal/source-preservation checks. This is Windows native integration evidence, not Linux cross-build inference. The job log was retrieved and inspected: **Windows Server 2025 Datacenter 10.0.26100**, image `windows-2025-vs2026 / 20260925.250.1`, SDK **10.0.401**. Core tests reported **22 passed / 0 failed / 0 skipped**. The exact native results extracted from that log are retained in [stage-a-windows-native.json](evidence/stage-a-windows-native.json): expected versions loaded; WAV decoded at 48,000 Hz / 2 channels / 3 seconds, **1,152,000 float PCM bytes**, peak **0.049987793**, source unchanged and handle released. The generated fixture checksum matches the Linux result. Device output is null, audible playback is not manually verified and gapless is not run. The hosted runner is not a Windows 11 desktop acceptance machine.

Windows native decode does not need an audio device. Its JSON results are in `artifacts/smoke/probe.json` and `decode.json`; CI retains these and TRX as evidence artifacts. A hardware check is deliberately separate:

```powershell
./scripts/Smoke.ps1 -Play
```

Record the Windows build, endpoint, JSON report and separate listening result. A successful `output-api-passed` report proves shared output consumption/pause/resume/stop, not audible quality, gapless or accurate audible-position display. WPF diagnostic-button behavior still requires a desktop check even if compilation and the CLI pass.

## Stage B local checks (2026-10-05)

Release cross-build passes with **0 warnings / 0 errors**, and **44 core tests pass, 0 fail, 0 skip**. New tests cover superseded/canceled loads, 300 coalesced changes followed by stop, owner-thread disposal, paused/clamped/unknown-duration seek, independent volume/mute, output failure/retry, explicit bad-file behavior, bounded automatic traversal, actual-played Previous history, removal of the playing entry, natural-end traversal/exhaustion, prepared-then-started history, paused versus stopped selection, pre-canceled replacement silence and literal normalized Unicode search.

All seven official native archives/DLL hashes/PE headers were validated locally. Fourteen real short CC0 fixtures are committed with per-file SHA-256, profile, source duration/tolerances and encoder provenance. `scripts/Generate-FormatFixtures.py` regenerates these into a new development directory using FFmpeg and the owned PCM source; fresh container hashes are recorded. FFmpeg is never an app runtime dependency. Development self-contained publish audits every manifest DLL and upstream companion.

The Windows workflow now executes the fourteen native formats plus actual production load/seek/rapid-prepare/stop/disposal and a real WPF smoke test. UI checks import duplicates without autoplay, native preparation, title/range bindings, stopped seek, search-source independence, binding warnings and screenshot render. These checks **passed on actual Windows CI**, including real Unicode FLAC metadata, metadata handle release, native FLAC seek and the derived-window dark theme. Exact run/job/source evidence follows below. Real shared output is opt-in `Smoke.ps1 -Play`; no device listening, capture, gapless, HE-AAC or Windows 11 desktop acceptance has been performed.

## Stage B Windows integration evidence

Observed [run 37353764766](https://github.com/kurasis/MPswift/actions/runs/37353764766) passed on source commit `52997b50dfa9726d7ca2fd76f6fe41ee2259f4b5`. Both Linux and Windows jobs succeeded. The [Windows job](https://github.com/kurasis/MPswift/actions/runs/37353764766/job/111910852658) ran on **Windows Server 2025 Datacenter 10.0.26100**, image `windows-2025-vs2026 / 20260925.250.1`. Windows TRX reports **44 passed, 0 failed**, with all 44 executed. Release builds have no warnings/errors.

The checksum-verified Actions evidence archive was downloaded and inspected. [Exact native/UI JSON and TRX counters](evidence/stage-b-windows-native-ui.json) retain the source, run/job URLs, archive SHA-256 and per-fixture decode facts. All **14** files passed native codec/sample-rate/channels/duration/bit-depth/signal checks, midpoint seek, decode-to-end, source hash preservation and exclusive read-only reopen after disposal. WAV float resolution is correctly 32 bits after removing BASS's float flag. AAC-LC ADTS/MP4 and ALAC use verified bundled plug-ins with Media Foundation disabled; HE-AAC is untested.

The actual production engine passed stopped prepare, seek, 40 rapid prepares, final-generation identity, stop-to-zero, disposal, handle release and unchanged source. No endpoint was initialized for this check. Real WPF startup imported two occurrences of the Unicode WAV without autoplay, retained shared track/distinct entry IDs, prepared and sought the real native source, preserved playback source under search, read Cyrillic/Unicode FLAC title/artist/album through TagLib, released its metadata handle, and sought FLAC through the production backend. Title/range bindings and the explicitly applied dark window style passed with **0 binding warnings/errors**. The rendered screenshot `stage-b-window.png` is in the Windows evidence artifact; it was downloaded and visually inspected. It does not establish Windows 11 DPI/accessibility/reference acceptance.

An earlier CI screenshot exposed the implicit Window-style inheritance issue; the final window explicitly applies that style and CI asserts the expected background. Additional core checks cover natural-end exhaustion, prepared-then-played history, stopped selection versus paused resume, and pre-canceled replacement silencing an old source. Final local self-contained publish was rebuilt from the final source and all seven native DLL hashes/companions audited.

`WasapiOutput` and `Listening` remain **not-run**. Device pause/resume/volume/callback behavior, audible latency/quality, digital capture, hotplug, gapless, HE-AAC and Windows 11 clean/offline acceptance are open gates. Stage B implementation is delivered; the full M1 audible acceptance set is not closed by hosted CI. No public binary release was published.

## Stage C local verification (2026-10-05)

Locked Release build passes with zero warnings/errors; **59 tests pass, 0 fail, 0 skip**. The actual application SQLite/settings/cache service sources are linked into the platform-neutral test assembly, using the already pinned Microsoft.Data.Sqlite 10.0.12. The intentional test-package addition updated only the test lock/transitive inventory; no version was loosened. SQLite runs on Linux, not as a fake repository.

Covered: Unicode/SQL-looking text parameterization, duplicate track/stable entry identity and enabled/order round trip, session-only writes preserving playlist rows, full transaction rollback after an injected SQL constraint, valid live-WAL backup, corrupt/newer/unrecognized data preservation, explicit validated restore retaining original bytes, second-writer exclusion, atomic settings with previous backup/range checks, all-channel opposite-phase peaks, bounded multi-hour accumulator, corrupt/oversized cache rejection and cache eviction/clear preserving library data, restore prepare/seek without Play.

Windows tests additionally request real BASS analysis with a prepared playback decoder alive, gain/mute independence, cache reload/corruption repair, two-hour owned PCM source boundaries, cancellation/disposal/file release and production-context survival. The real WPF smoke test duplicates/renames/reorders tabs, rejects filtered reorder, checks other-tab/source independence, closes/recreates the model against actual SQLite/settings, restores identity/order/enabled/position/gain/mute without autoplay and renders real waveform. These expanded Windows checks **passed on actual Windows**, as recorded below.

## Stage C observed Windows integration

The [Windows job](https://github.com/kurasis/MPswift/actions/runs/37359855055/job/111931430685) passed at source **`a38e81eefd78d991a4c96dd4adccc5f6e723801b`**. Host: Windows Server 2025 Datacenter 10.0.26100, `windows-2025-vs2026 / 20260925.250.1`. Both Windows TRX reports contain **59 executed / 59 passed / 0 failed**, Release compilation has no warnings/errors, and all fourteen original codec fixtures still pass after the shared native-context change. The checksum-verified Actions artifact was downloaded and inspected; [raw native/waveform/UI reports and counters](evidence/stage-c-windows-persistence-waveform.json) preserve the source/job/archive digest. The corresponding Linux CI job was queued at capture; exact-source Linux local build/tests/publish passed, and the preceding [run 37358722369](https://github.com/kurasis/MPswift/actions/runs/37358722369) passed both OS jobs. A queued job is not reported as passed.

Native waveform integration passed with a production source decoder alive: opposite-phase peaks remain above ±0.045 despite playback gain 0/mute, independent analysis does not move or free its source, completed cache round trip matches peaks, and a damaged cache regenerates. An actual owned **two-hour, 8 kHz mono PCM16 file (115,200,044 bytes)** is decoded end to end: **7,200 seconds / 300,000 buckets / 192 frames per bucket / 2,400,000-byte peak payload**, preserving positive/negative endpoint pulses. This is actual BASS streaming, not a synthesized waveform or a length-only accumulator test. Cancellation/release passes exclusive reopening of the long source while the production decoder remains seekable. Source hashes remain unchanged. It demonstrates bounded peak/PCM allocations, not a full process-memory/performance certification.

Actual WPF startup/import/native seek reaches real waveform data. The test duplicates/renames a tab with Unicode/SQL-looking text, changes selected-row order, rejects filtered reorder and confirms that editing the other tab retains the original playback source/order. It saves and disposes the actual model/database, recreates them against the same files, and restores **two tabs, stable IDs/order/enabled state, original source tab, active title, position 1 second, volume 23 and mute true**, without autoplay. Cached waveform returns with 300 actual buckets and **0 binding warnings/errors**. The downloaded `stage-c-window.png` was visually inspected; geometry uses true full-scale peaks (quiet tones have a small envelope). A separate accessible slider remains functional. The source playlist is identified in the header, and obsolete peaks are cleared when a new file begins loading.

The first Windows run exposed retained pooled handles in two test setup connections before byte-hash reads; these fixtures now disable pooling, matching production ownership. No assertions were removed or checks disabled. Store disposal now signals completion only after its connection and data ownership handle are released, making immediate model reopen safe. Close/save failures remain visible and offer explicit discard instead of losing data silently or trapping the app on a permanently unwritable disk.

Self-contained development publish was rebuilt from the final source and all seven manifest DLL hashes/companions audited locally. The cloud startup draft was updated; no new secrets/services/network requirements were introduced. Actual audio output/listening/capture, HE-AAC, Windows 11 clean/offline/DPI/accessibility, forced crash/kill, disk-full/read-only workflows and complete P0/P1 acceptance remain open. No public portable release was published.


## Stage D local validation (2026-10-05)

Locked Release build: 0 warnings/errors, 73 tests passed, 0 failed/skipped. Fourteen added cases protect batch queue precedence/resumption/detachment, repeat-one/manual escape, no-repeat shuffle and persisted bag/history, source deletion/all-disabled exhaustion, actual SQLite queue/CUE round trip, CUE Unicode/quotes/multi-file/pregap/decreasing/invalid/network/encoding checks, clipped waveform coordinates, invariant ReplayGain and measured pure-PCM EQ attenuation/bypass/Nyquist/final saturation. They do not establish Windows device behavior. New `--mixer` Windows check captures production mixer callback float PCM for a continuously generated 997 Hz lossless split/CUE fixture and checks sample equality across boundaries, scheduled incoming timeline and overlap seek cancellation. The WPF route now also verifies queue IDs/repeat/shuffle/processing after actual reopen. These expanded checks are pending execution; endpoint capture/listening/exclusive/hotplug/sleep and lossy gapless remain unrun.


## Stage D hosted-runner failure and Stage E local checks

The Stage D [run 37363650027](https://github.com/kurasis/MPswift/actions/runs/37363650027) at `e2d162f5f896d714a522ac1ada9008b0c880ee1e` canceled both jobs without any steps. Check annotations explicitly say **“The job was not acquired by Runner of type hosted even after multiple attempts”**. No Stage D Windows/native/WPF tests executed in that run. Historical Stage C's queued Linux job also later canceled without acquisition; its completed Windows job/evidence remains valid at its exact earlier source. CI now targets explicit `windows-2022` / `ubuntu-24.04` pools; these are integration hosts, not Windows 11 desktop acceptance.

Stage E locked Release local cross-build passes with **0 warnings/errors; 80 tests passed / 0 failed / 0 skipped**. Actual SQLite tests add normalized literal Cyrillic/Latin/accent/punctuation search, missing/reappearance generation updates retaining ratings/playlists, once-only listening counts, schema-1→2 migration with a valid schema-1 backup and unchanged stable IDs, and **100,000 actual index rows with a 100-row final page and stable lookup IDs**. Playlist domain tests cover Unicode M3U8 duplicate/order roundtrip, numeric PLS order, URL/recursive rejection, explicit Cyrillic fallback and refusal of lossy CUE export. Listening tests use elapsed playing time, pause and forward-seek markers; they do not validate endpoint output.

Local self-contained development publish verifies all **13** pinned DLL hashes and required upstream companions, including separate MPC/TTA notice folders. Three owned CC0 encodings (WavPack lossless, TTA lossless and AAC-LC M4B) extend the manifest to 17; checksums/provenance/encoder commands are recorded. Native decode execution is pending. Six new plugins were downloaded from HTTPS upstream links, archive/DLL SHA-256 pinned and PE architecture/version audited. WMA optional system modules, HE-AAC and APE/MPC/DSF/DFF/hybrid profiles remain unrun/release blockers.

Expanded WPF smoke commits real owned-file root scan/delete/reappearance with retained IDs/ratings, M3U8 roundtrip/no autoplay, independent-native relink preserving IDs/duplicates, CUE logical preparation/relative seek/200 waveform buckets, a local frozen thumbnail, lossy export refusal and unchanged original/copy source hashes. They are scheduled checks, not observed passes until an actual Windows runner completes. Real output/capture/listening, Windows 11 clean/offline/large-UI/DPI/accessibility, malformed/large embedded tags/artwork, actual watcher overflow, disk-full/crash/sleep/device/exclusive and full P0/P1 acceptance remain open.

Final local review also replaced the import identity cache with a concurrent dictionary so watcher reconciliation can remember tracks during an import without a dictionary race, guards combined playlist capacity while batches arrive, and reads playlist/CUE documents through a bounded file handle with growth detection. The final exact code was rebuilt and locally published with 80 passing tests and 13 audited DLLs/companions.

[Machine-readable local results and hosted-runner status](evidence/stage-de-local-and-runner-status.json) retain the final code commit, actual 80-test counters, dataset size, publish audit and explicitly unrun gates.

## Stage D/E Windows confirmation and Stage F local checks (2026-10-06)

Run [37367043601 attempt 2](https://github.com/kurasis/MPswift/actions/runs/37367043601/attempts/2), source `c8426e6`: Linux and Windows successful, 80 tests each, 17 native format fixtures, actual mixer callback split/CUE error 0, real WPF queue/session/library/M3U8/rating/relink/CUE/artwork/source preservation. Full JSON is retained in [evidence](evidence/stage-de-windows-native-ui.json). Windows Server 2022 hosted CI is not Windows 11/device/listening acceptance.

Stage F locked local Release cross-build: 0 warnings/errors, 92 passed / 0 failed / 0 skipped. New tests cover CLI Unicode/literal options, explicit play, unsupported options/remote/device/ADS rejection, strict/versioned/bounded IPC and backward-compatible language/tray settings. New Windows IPC/tray/SMTC/Automation/virtualization/English-Russian smoke is prepared, not yet reported passed. No endpoint or screen reader is available in Linux.

## Stage F observed Windows and Stage G local verification (2026-10-06)

[Run 37408825503](https://github.com/kurasis/MPswift/actions/runs/37408825503), source `39a8a25`, passed Linux/Windows with 92 tests. Both real EN/RU WPF runs passed second-process activation and concurrent local file forwarding/no autoplay, oversized rejection/server recovery, seek RangeValue Automation, close-to-tray/state and nearly 10k rows with 6 realized containers. SMTC registered and actual published metadata matched the model. Resource coverage was 199 keys. Exact [JSON reports](evidence/stage-f-windows-integration.json) retain unavailable physical key/Narrator/DPI checks. Initial `0ea5598` crashed because native IInspectable slots were omitted; the corrected source passed. Screenshot review subsequently exposed dynamic IPC labels carrying ambient English culture; Stage G fixes explicit resource/display culture and adds post-forwarding assertions. The earlier pass does not claim full localization acceptance.

Stage G local locked build passes **0 warnings/errors, 95 passed / 0 failed / 0 skipped**. Added tests verify default-name localization preserves saved IDs/user names, bounded background log rotation/redaction/JSON, and reporting failed log writes without caller exceptions. Candidate generation passed with 550 files, 14 restored dependency declarations and 13 native x64 DLLs/companions. Four real negative checks reject changed/missing/extra files and an empty checksum list; the restored owned copy passes. The first candidate has a truthful dirty-tree marker; final clean source/artifact evidence follows after commit. New native 1000-cycle preparation/resource stress and extracted apphost execution are prepared but pending Windows execution. No clean Windows 11/offline/device/listening assertion is made.


## Stage G observed candidate/native checks (2026-10-06)

[Run 37410949382](https://github.com/kurasis/MPswift/actions/runs/37410949382), source `1f3e664`, passed both systems with **95 passed / 0 failed / 0 skipped**. Windows passed 17 real format fixtures, native engine/waveform/mixer, EN/RU WPF and extracted self-contained apphost execution from a Unicode path/different working directory with invalid external DOTNET_ROOT. Actual localization checks include IPC-updated state/source labels; 203 paired keys and 9,987 actual rows with 6 realized containers were observed. Package audits on both systems cover 550 files, 14 dependency declarations, 13 native x64 libraries and changed/missing/extra/empty-checksum rejection. [Exact reports](evidence/stage-g-candidate-and-windows.json) retain TRX counts, package checksums and source IDs. Local clean-source candidate/audit evidence is [retained separately](evidence/stage-g-local-candidate.json); ZIPs are local output, not CI uploads/releases.

After 50 warmup iterations, 1,000 actual native load/prepare-next/seek/stop cycles passed the 32-handle/64-MiB growth guards: observed handle growth **3**, private-byte growth **7,278,592**, cycle p95 **0.1871 ms**. Raw samples are retained. This does not prove a stable leak plateau, device transitions, two-hour playback or the specification's output timing targets. Windows Server 2022 build 20348 is not the clean Windows 11/offline target. Physical media keys, Narrator/DPI, devices/listening, remaining format profiles and distribution licensing remain open.

Final keyboard review fixes window-level Escape stealing an open dropdown's dismissal and button/checkbox focus suppressing unrelated playlist shortcuts. The local locked Release cross-build still passes with 0 warnings/errors and all 95 tests. The software-routed real WPF dropdown-Escape regression passed in both languages and the extracted self-contained app in run 37410949382; physical keyboard acceptance remains distinct.

Local candidate `LocalAudioPlayer-dev-1f3e66446808-win-x64.zip` is from clean committed source `1f3e664`, 85,361,406 bytes, SHA-256 `cce375d1abac00f4952273ebb12d370ae6e265b8b9787ac9a5fdd82cca226ad6`. Its full audit and all four negative integrity checks passed. Windows/Linux/local ZIP hashes can differ with package build metadata and timestamps; each report records its own exact hash. Distribution remains unapproved.


## GitHub build publication (2026-10-06)

Added separate main-only development prerelease publication after successful Linux/Windows matrix checks, Windows package smoke and artifact handoff. The publisher validates clean commit identity, package/integrity agreement, ZIP size/hash/checksum and GitHub asset digests before publishing a draft. The first end-to-end workflow will provide the actual publication/download evidence; previous runtime evidence remains source-specific.


## Stage G2 settings recovery and G3 real process crash harness (2026-10-06)

Locked local Release cross-build passes **0 warnings/errors; 102 passed / 0 failed / 0 skipped**. Seven new cases exercise actual settings/backup files: explicit validated restore with retained corrupted bytes, refusal preserving both files, invalid backup never offered, unsupported schema 0/2 never downgraded, missing main requiring a choice and export clamping/no overwrite. Publisher/runtime paths retain their earlier source-specific evidence.

Crash-Smoke.ps1 now runs after EN/RU smoke on Windows. It creates only a fresh owned directory, commits a production WPF checkpoint, holds destructive changes in an uncommitted diagnostic SQLite transaction, kills the actual apphost and restarts it. [Run 37415275489](https://github.com/kurasis/MPswift/actions/runs/37415275489), source `675bdf6`, passed on Linux/Windows with 102 tests each; the Windows child was forcibly terminated with exit -1 and the real restart passed every stated assertion. [Retained raw reports](evidence/stage-g-resilience-windows.json) include seven recovery-case outcomes on each platform and EN/RU coverage of 205 keys. Device/migration/power-loss remain unrun. Failures retain a JSON report; private checkpoint/database files are not uploaded.


## G4–G7 block: local results, Windows checks pending

Locked Release cross-build passes 115 tests, zero warnings/errors. Complete ZIP backup/restore has 13 real SQLite/file cases (live WAL, IDs/session/index/ratings/settings, retained originals, invalid entries/checksums/schema/foreign keys, no overwrite, ownership and occupied-path rejection). The expanded Windows harness exercises complete restore through WPF and invalid-restore recovery, real ACL/file-lock failures and artwork limits, 100k production query/page/render timings and nearly 10k playlist scroll timings. These Windows results are not yet observed. Four owned RF64/WMA/DSF/DFF fixtures extend the matrix to 21; actual new native results are pending. Full Windows 11/device/offline/full-disk/profile/licensing acceptance remains open.
