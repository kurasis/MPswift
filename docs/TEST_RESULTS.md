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

`.github/workflows/build.yml` establishes Linux/Windows Release builds and tests, plus Windows `Smoke.ps1` native load/decode/end/seek/disposal checks. CI execution has not yet been observed in this checkpoint. A configured workflow alone is not passing evidence.

Windows native decode does not need an audio device. Its JSON results are in `artifacts/smoke/probe.json` and `decode.json`; CI retains these and TRX as evidence artifacts. A hardware check is deliberately separate:

```powershell
./scripts/Smoke.ps1 -Play
```

Record the Windows build, endpoint, JSON report and separate listening result. A successful `output-api-passed` report proves shared output consumption/pause/resume/stop, not audible quality, gapless or accurate audible-position display. WPF diagnostic-button behavior still requires a desktop check even if compilation and the CLI pass.
