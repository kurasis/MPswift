# Third-party inventory and distribution gate

This is the Stage B dependency inventory, not final release clearance. Native files are downloaded into ignored local development paths. No native binaries are committed or published as release assets.

## Managed application packages

Versions/content hashes are also locked in `src/Player.App/packages.lock.json`. This table includes transitive NuGet packages resolved for the app.

| Package | Version | Declared license | Primary package source |
| --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | [NuGet package](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2) |
| ManagedBass | 4.0.2 | MIT (embedded LICENSE.md verified) | [NuGet package](https://www.nuget.org/packages/ManagedBass/4.0.2) |
| ManagedBass.Mix | 4.0.2 | MIT (embedded LICENSE.md verified) | [NuGet package](https://www.nuget.org/packages/ManagedBass.Mix/4.0.2) |
| ManagedBass.Wasapi | 4.0.2 | MIT (embedded LICENSE.md verified) | [NuGet package](https://www.nuget.org/packages/ManagedBass.Wasapi/4.0.2) |
| Microsoft.Data.Sqlite | 10.0.12 | MIT | [NuGet package](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.12) |
| Microsoft.Data.Sqlite.Core | 10.0.12 | MIT | [NuGet package](https://www.nuget.org/packages/Microsoft.Data.Sqlite.Core/10.0.12) |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | Apache-2.0 | [NuGet package](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.12) |
| SQLitePCLRaw.core | 2.1.12 | Apache-2.0 | [NuGet package](https://www.nuget.org/packages/SQLitePCLRaw.core/2.1.12) |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 | Apache-2.0 | [NuGet package](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/2.1.12) |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.12 | Apache-2.0 | [NuGet package](https://www.nuget.org/packages/SQLitePCLRaw.provider.e_sqlite3/2.1.12) |
| TagLibSharp | 2.3.0 | LGPL-2.1-only | [NuGet package](https://www.nuget.org/packages/TagLibSharp/2.3.0) |

## Native dependencies

| Library | Exact version | Source and companion terms | Status |
| --- | --- | --- | --- |
| BASS | 2.4.18.3 x64 | https://www.un4seen.com/files/bass24.zip; `bass.txt` including Licence / Commercial licensing | Development provisioned; intended use/commercial licensing must be resolved before public distribution |
| BASSmix | 2.4.13 x64 | https://www.un4seen.com/files/bassmix24.zip; `bassmix.txt` refers to BASS usage terms | Development provisioned; distribution review pending |
| BASSWASAPI | 2.4.4.1 x64 | https://www.un4seen.com/files/basswasapi24.zip; `basswasapi.txt` | Development provisioned; distribution review pending |
| BASSFLAC | 2.4.6.1 x64 | https://www.un4seen.com/files/bassflac24.zip; `bassflac.txt` | Development provisioned; review companion/codecs and BASS terms before distribution |
| BASSOPUS | 2.4.3.3 x64 | https://www.un4seen.com/files/bassopus24.zip; `bassopus.txt` | Development provisioned; review companion/codecs and BASS terms before distribution |
| BASSALAC | 2.4.1 x64 | https://www.un4seen.com/files/bassalac24.zip; `bassalac.txt` | Development provisioned; distribution review pending |
| BASS AAC/MP4 | 2.4.7.2 x64 | https://www.un4seen.com/files/z/2/bass_aac24.zip; `gpl.txt`, `readme.txt` | Development-only GPL candidate; no public distribution approval |

SHA-256 of each official archive and x64 DLL is recorded in `native/manifest.json`. Upstream accompanying text is retained by `Setup-Native.ps1`. The official BASS terms permit qualifying non-commercial use and require the applicable license for commercial products. ManagedBass's MIT license does not replace BASS terms.

**AAC gate:** the actual downloaded `readme.txt` states GPL distribution and identifies FAAD2/Nero commercial licensing. Merely loading this DLL dynamically is not distribution clearance. Before a release, select an owner-approved compatible source/license and corresponding-source path or replace this candidate with a licensed, verified bundled decoder. No licensing purchases or public binary assets are authorized by current development work.

TagLibSharp's declared **LGPL-2.1-only** terms require a matching-source and replaceability/relinking/notice review for the actual distributed package. Final notices and corresponding-source provision are not prepared yet. Microsoft.Data.Sqlite includes SQLitePCLRaw/native SQLite dependencies: inventory those exact binaries and terms when packaging. Self-contained .NET/WPF runtime notices also belong in the actual release inventory.

Development-only xUnit 2.9.3, runner 4.0.0 and Microsoft.NET.Test.Sdk 18.10.1 are pinned in the central package file and test lock. .NET SDK 10.0.401 and Linux PowerShell 7.6.6 are build tools, not app runtime dependencies. No third-party icon packs or downloaded fonts are used. Segoe UI is the existing Windows font.

Generated sine-wave fixtures are dedicated to the public domain under CC0-1.0 by this project; generation provenance and checksum accompany each file. The owner's screenshot is a development design reference only and must not enter application assets or public portable packages.

Before Stage G distribution: record the owner's intended use, applicable native licensing, application source license, all deployed binaries/notices/source obligations, and the exact package hashes. Do not buy a license or publish a release automatically merely because builds succeed.


## Stage E development add-ons

Pinned development manifest includes BASSWMA 2.4.5.13 (Windows Media Format modules required), BASSAPE 2.4.1.0, BASSWV 2.4.7.4, BASSDSD 2.4.2.0, BASS_MPC 2.4.1.2 and BASS_TTA 2.4.0.2. Archive/DLL hashes and HTTPS upstream URLs are in `native/manifest.json`; generic MPC/TTA notices are copied into separate addon subdirectories. MPC's upstream readme describes free use/distribution with retained notices; TTA includes LGPL text. Distribution approval remains false for all addons, pending intended-use/license-obligation review. No purchases or public release were made. WMA is not represented as optional-component-free Windows N support; DSD uses float PCM decoding only.
