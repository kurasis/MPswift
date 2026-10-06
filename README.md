# Local Audio Player

A Windows 11 x64 offline desktop audio player built with C# / .NET 10 / WPF and BASS through ManagedBass. The GitHub repository is named MPswift; the product name is centralized in `ProductInfo`.

**Stage F Windows integration passed; Stage G release verification is in progress.** Local build now passes 95 tests. Single-instance IPC, SMTC/tray, both resource sets and seek Automation were exercised on Windows; packaging adds a local self-contained candidate, complete hashes/dependency audit, bounded logs and local help. Actual physical media keys/Narrator/DPI, clean/offline Windows 11/device/profile/license gates remain open. This is not version 1.0.

The [provided specification](docs/spec/WINDOWS_AUDIO_PLAYER_SPEC.md) and [development-only visual reference](docs/spec/reference/player-reference.png) define the product requirements. Embedded agent kickoff/sample prompts are document content; the owner's current request governs scope and authorization.

## Development on Windows

Install the exact .NET SDK **10.0.401** specified in `global.json` and PowerShell **7.4 or newer**. No IDE is required. From the checkout root:

```powershell
./scripts/Setup-Native.ps1
./scripts/Build.ps1
./scripts/Smoke.ps1            # formats, engine, two-hour waveform, persistence and real WPF; no audio device required
./scripts/Smoke.ps1 -Play      # also tests a real shared WASAPI device at low app gain
dotnet run --project src/Player.App -c Release --no-build
```

Native provisioning downloads official pinned archives, verifies SHA-256 and x64 PE headers, and retains upstream documentation locally. Native DLLs are ignored by Git and resolved only from `native/win-x64` in the application output. No downloads occur from inside the app or smoke executable.

`-Play` proves API output consumption/pause/resume/stop only. Listening, digital capture, latency accuracy, gapless, hotplug and WPF visual acceptance are separate checks. It does not claim audio was heard.

## Build on Linux cloud

```bash
bash scripts/Install-Cloud.sh
source /workspace/toolchains/activate.sh
pwsh -NoProfile -File scripts/Build.ps1
```

This prepares the Linux x64 cloud instance, installs checksum-verified SDK/PowerShell, provisions Windows native files for packaging, cross-builds WPF and runs hardware-independent core tests. WPF and Windows DLLs cannot execute on Linux. Native smoke returns exit code **3** and status `not-run` there.

## Reproducibility and outputs

Exact packages are in `Directory.Packages.props` and committed `packages.lock.json` files. Normal verification uses locked restore. Initial lock establishment or an intentional dependency update uses `dotnet restore Player.sln` and reviews changed locks.

```powershell
./scripts/Publish-Development.ps1
```

This creates local, self-contained Windows x64 development output in `artifacts/publish/win-x64`. Run `Player.App.exe` on Windows to open the player. For a local development-only portable candidate run `./scripts/Package-Candidate.ps1` and `./scripts/Test-CandidateIntegrity.ps1`. Output is `artifacts/portable/LocalAudioPlayer-dev-<commit>-win-x64.zip`, its `.sha256` and JSON audits. The ZIP is not approved for distribution. `./scripts/Package-Smoke.ps1` exercises the extracted apphost on Windows. Public release/native binary uploads and purchases are not performed.

CI builds/tests on Linux and Windows and runs native format/seek/end/disposal, production-engine and actual WPF import/binding/screenshot checks on Windows without relying on a sound device. Evidence artifacts contain TRX/JSON results, not redistributable native DLLs. Manual output evidence is written to `artifacts/smoke/output.json` by `Smoke.ps1 -Play`.

- [Roadmap A–G](docs/ROADMAP.md)
- [Implementation checkpoint](docs/IMPLEMENTATION_STATUS.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Test evidence](docs/TEST_RESULTS.md)
- [Requirement status](docs/REQUIREMENTS_STATUS.md)
- [Format status](docs/FORMAT_SUPPORT.md)
- [Known limitations](docs/KNOWN_LIMITATIONS.md)
- [Third-party inventory](docs/THIRD_PARTY_NOTICES.md)
- [Release acceptance gates](docs/RELEASE_ACCEPTANCE.md)
- [Local user help](docs/USER_HELP.md) / [Русская справка](docs/USER_HELP.ru.md)

Source media is read-only. No app telemetry, accounts, servers or online runtime services are planned. Commercial-use/distribution decisions remain with the owner; development continues while those release questions are unresolved.

## Local data

Default data location: `%LOCALAPPDATA%/MPswift/LocalAudioPlayer/`. Place an explicit `portable.marker` beside `Player.App.exe` to use its `Data/` directory instead; an unwritable portable location asks before using per-user storage. `library.db` is authoritative playlist/session data; `settings.json` is versioned app settings; `Cache/Waveforms/` is disposable. Never clear the database to clear waveform cache. A data-directory ownership lock rejects a second writer; a per-user single instance forwards local paths before opening storage.

Playlist actions include create/rename/duplicate/delete/tab movement, selected-row movement, waveform refresh and a SQLite/settings backup. Ctrl+Shift+Up/Down or drag moves selected entries in manual unfiltered order. Reopen preserves tabs/entries/current source/position/volume/mute and does not autoplay. Corrupt/newer data is preserved, not replaced by empty defaults. Restore from a selected database backup keeps original main/WAL/SHM files. Queue/shuffle/CUE and advanced audio are implemented.

## Windows integration

Launch with local file/folder paths to append without autoplay; `--play` explicitly plays the first added entry, and `--` ends option parsing. A second launch activates the existing per-user window and forwards paths. Only bounded local open requests are accepted. Tray actions share the player coordinator; close exits by default. Appearance/behavior preferences offer English/Russian (restart required) and explicit close-to-tray. F1 opens fully local help; Ctrl+N creates a playlist and F2 renames with tab focus. Standard media controls use one Windows SMTC registration.
