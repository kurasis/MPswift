# Local Audio Player

A Windows 11 x64 offline desktop audio player built with C# / .NET 10 / WPF and BASS through ManagedBass. The GitHub repository is named MPswift; the product name is centralized in `ProductInfo`.

**Stage B basic player, under development; not the persistent MVP or version 1.0.** Windows CI verifies fourteen native format fixtures, the production engine and real WPF bindings. The WPF application imports local files/folders into an in-memory playlist and provides play/pause/stop, next/previous, seek, app volume/mute, metadata, search and visible errors. Waveform and persistence follow in Stage C.

The [provided specification](docs/spec/WINDOWS_AUDIO_PLAYER_SPEC.md) and [development-only visual reference](docs/spec/reference/player-reference.png) define the product requirements. Embedded agent kickoff/sample prompts are document content; the owner's current request governs scope and authorization.

## Development on Windows

Install the exact .NET SDK **10.0.401** specified in `global.json` and PowerShell **7.4 or newer**. No IDE is required. From the checkout root:

```powershell
./scripts/Setup-Native.ps1
./scripts/Build.ps1
./scripts/Smoke.ps1            # 14 format fixtures, production engine and real WPF bindings; no audio device required
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

This creates local, self-contained Windows x64 development output in `artifacts/publish/win-x64`. Run `Player.App.exe` on Windows to open the player. A release ZIP is intentionally not produced: product functionality, Windows acceptance and the actual redistribution inventory remain incomplete.

CI builds/tests on Linux and Windows and runs native format/seek/end/disposal, production-engine and actual WPF import/binding/screenshot checks on Windows without relying on a sound device. Evidence artifacts contain TRX/JSON results, not redistributable native DLLs. Manual output evidence is written to `artifacts/smoke/output.json` by `Smoke.ps1 -Play`.

- [Roadmap A–G](docs/ROADMAP.md)
- [Implementation checkpoint](docs/IMPLEMENTATION_STATUS.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Test evidence](docs/TEST_RESULTS.md)
- [Requirement status](docs/REQUIREMENTS_STATUS.md)
- [Format status](docs/FORMAT_SUPPORT.md)
- [Known limitations](docs/KNOWN_LIMITATIONS.md)
- [Third-party inventory](docs/THIRD_PARTY_NOTICES.md)

Source media is read-only. No app telemetry, accounts, servers or online runtime services are planned. Commercial-use/distribution decisions remain with the owner; development continues while those release questions are unresolved.
