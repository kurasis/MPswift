# Implementation checkpoint

Updated: **2026-10-05**.

## Current scope

**Stage A / M0 foundation implemented; Windows CI native proof passed, real-device/desktop proof remains pending.** This is not the MVP or version 1.0. The owner requested beginning development from the first stage, with Russian chat, English repository content and automatic push/merge. [Stages A–G](ROADMAP.md) map directly to specification M0–M6. The uploaded specification and reference are retained under `docs/spec/` for development only.

## Implemented

- Two production projects (`Player.Core`, WPF `Player.App`), xUnit suite and a linked-code `Player.AudioSmoke` development harness.
- Exact SDK/central packages/lock files, NuGet source configuration, Release build/test/native-provision/publish/cloud scripts and Linux/Windows CI.
- Independent local-path/CUE-frame/segment-seek rules with 22 passing tests.
- Hash-verified native resolution from controlled output paths; synchronous smoke session with decode/end/seek and shared WASAPI pause/resume/stop/disposal checks.
- Generated legal WAV/fixture manifest, source preservation/hash/handle-release assertions and honest JSON statuses.
- Native diagnostics WPF window with centralized dark theme, resource-based strings and injected toolkit view model.
- Acceptance/format/license/architecture/limitation documents.

## Versions and boundaries

- SDK **10.0.401**, C# 14 stable, .NET 10; Linux PowerShell **7.6.6** (Windows scripts require 7.4+).
- ManagedBass / Mix / Wasapi **4.0.2**, CommunityToolkit.Mvvm **8.4.2**, TagLibSharp **2.3.0**, Microsoft.Data.Sqlite **10.0.12**; tests use xUnit **2.9.3**, VS runner **4.0.0**, test SDK **18.10.1**. See `Directory.Packages.props` and package locks.
- Native BASS **2.4.18.3**, BASSmix **2.4.13**, BASSWASAPI **2.4.4.1**, x64 only, exact archive/DLL hashes in `native/manifest.json`.
- Core has no platform/native/storage dependencies. Audio mutations/callback ownership stay outside views/view models. The Stage A smoke session is not the future production asynchronous engine.
- App-scoped `RuntimeIdentifier=win-x64` and `SelfContained=true` restore/publish the needed Windows packs without changing Core's RID/lock.

## Current verification

Linux Release cross-build succeeds without warnings; all **22 core tests pass**. Native archives/hashes/PE architecture, repeatable scripts and generated WAV were checked. Self-contained development output was generated and required files audited. Native probe on Linux correctly reports `not-run`/exit 3. See [exact test evidence](TEST_RESULTS.md).

[GitHub CI run 37344651363](https://github.com/kurasis/MPswift/actions/runs/37344651363) passed for source commit `0226ea6b10c03f8517b13a3e025fba488d5ecda2`: Linux and Windows builds/core tests succeeded; Windows native load/WAV decode/seek/end/disposal checks succeeded on Windows Server 2025 10.0.26100, with [native JSON evidence](evidence/stage-a-windows-native.json) retained from the job log. Both local and Windows core suites ran 22 tests without failures/skips. Actual WASAPI output was deliberately not run in CI. WPF execution, real-device output, listening, capture, clean-machine/offline checks and licensing/distribution gates remain pending. No format or gapless support is advertised as verified.

## Artifacts and commands

- Source root: `/workspace/MPswift`.
- Local development publish: `artifacts/publish/win-x64/Player.App.exe` (run on Windows only).
- Core TRX: `artifacts/test-results/`.
- Generated fixture/JSON: `artifacts/fixture-check/`; Windows smoke evidence uses `artifacts/smoke/`.
- Native DLLs/upstream companions: ignored `native/win-x64/`, copied to app/tool outputs by MSBuild.
- Cloud toolchains: `/workspace/toolchains`; activate with `source /workspace/toolchains/activate.sh`.
- Verify: `pwsh -NoProfile -File scripts/Build.ps1`; publish locally: `scripts/Publish-Development.ps1`.
- Windows decode: `scripts/Smoke.ps1`; Windows device: `scripts/Smoke.ps1 -Play`.
- Cloud configuration draft saved successfully: full `install_script` and `start_skill`. It does not apply/publish automatically; the owner reviews/saves settings and publishes the environment for future snapshot reuse. No new secret/network requirements were added.
- No public portable ZIP or release has been produced. Generated artifacts, caches and native DLLs are intentionally untracked.

## Next work

The Windows load/decode gate is now verified through CI; retain real-device output and desktop checks as pending gates. For Stage B, introduce `IAudioPlayer` and a serialized, cancellable production engine/coordinator with generation IDs; implement functional file/folder import, main layout and transport/seek/volume/error handling; add legal MP3/FLAC/core-format fixtures. Keep future storage/waveform/queue features in their planned stages and update requirement evidence as real tests run.
