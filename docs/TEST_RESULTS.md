# Test evidence — Stage A

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

Release cross-build passes with **0 warnings / 0 errors**, and **40 core tests pass, 0 fail, 0 skip**. New tests cover superseded/canceled loads, 300 coalesced changes followed by stop, owner-thread disposal, paused/clamped/unknown-duration seek, independent volume/mute, output failure/retry, explicit bad-file behavior, bounded automatic traversal, actual-played Previous history, removal of the playing entry and literal normalized Unicode search.

All seven official native archives/DLL hashes/PE headers were validated locally. Fourteen real short CC0 fixtures are committed with per-file SHA-256, profile, source duration/tolerances and encoder provenance. `scripts/Generate-FormatFixtures.py` regenerates these into a new development directory using FFmpeg and the owned PCM source; fresh container hashes are recorded. FFmpeg is never an app runtime dependency. Development self-contained publish audits every manifest DLL and upstream companion.

The Windows workflow now executes the fourteen native formats plus actual production load/seek/rapid-prepare/stop/disposal and a real WPF smoke test. UI checks import duplicates without autoplay, native preparation, title/range bindings, stopped seek, search-source independence, binding warnings and screenshot render. These checks are currently **pending CI execution**, not inferred from cross-compilation. Real shared output is opt-in `Smoke.ps1 -Play`; no device listening, capture, gapless, HE-AAC or Windows 11 desktop acceptance has been performed.
