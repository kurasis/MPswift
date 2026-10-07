# Stage G release acceptance

This is an executable development candidate and a verification workflow, not approved version 1.0. No scope reduction is accepted. The owner now authorizes GitHub development prereleases with ZIP/checksum assets after successful main CI. Stable version 1.0 acceptance, licensing purchases, registry associations/default overrides and private audio distribution remain separate.

## Automated reproducible commands

```powershell
./scripts/Build.ps1
./scripts/Smoke.ps1 -SkipBuild # includes real Windows crash/restart validation
./scripts/Package-Candidate.ps1 -SkipBuild
./scripts/Package-Smoke.ps1
```

Build uses locked restore. Package creation uses a fresh owned staging folder, audits all application/runtime/native files, retains dependency declarations/available upstream texts, writes a per-file SHA-256 manifest/checksum list and an outer ZIP checksum, and rejects data/log/cache/music/reference/debug inputs. Every candidate is explicitly marked DEVELOPMENT-ONLY, with distributionApproved=false. Package creation itself does not upload assets. Main CI publishes the Windows-built ZIP as a separate development prerelease after both matrix jobs and Windows package smoke pass. Remote asset digests are checked before the draft becomes visible. Verify-Candidate rejects missing/changed/extra files and checks native x64 PE/hash. Package smoke runs the extracted self-contained executable from a Unicode path and different working directory with invalid DOTNET_ROOT, using only owned fixtures. An installed SDK elsewhere on a hosted runner means this is not clean-machine proof.

## Required evidence still preventing version 1.0

| Gate | Required recorded workflow | Current boundary |
| --- | --- | --- |
| AC-001/002 clean offline baseline | Exact supported Windows 11 build; standard user; no SDK/.NET; disconnect network before extraction/first run; WAV/MP3/FLAC/CUE/library/settings and restart | Hosted Windows Server is useful integration evidence, not baseline acceptance |
| AC-003/005/017–022 output | Device model/driver; shared/exclusive availability/busy; actual output consumption/pause/resume/seek; digital boundary captures; app/system volume separation; unplug/default switch/sleep/resume | Callback PCM proves mixer scheduling only; endpoint output/listening/latency are distinct |
| AC-004/018/037 profiles | Legal HE-AAC/large RF64/WMA(lossless/N conditions)/APE/MPC/additional DSF/DFF/WV hybrid fixtures, rates/channels; per-profile duration/seek/end/metadata; measured advertised lossy padding/boundaries | G8 passed 30 committed profiles, owned WMA lossless/Pro and >4 GiB RF64; exact combinations are recorded. Actual Windows N, large APE/other untested variants and endpoint/gapless claims remain open |
| AC-013/030/036 usability | Windows 11 EN/RU screenshots, keyboard-only and Narrator; physical 100/150/200% DPI; cross-monitor move/removal; 10k scroll and 100k query+UI timings | Hosted warm 100k query+WPF and 9987-row scroll measurements passed; reference Windows 11/cold/manual acceptance remains open |
| AC-023–025/031/032/039 resilience | Malformed/large tags/artwork; watcher overflow; cancellation; disk full/read-only; kill during checkpoint/migration; database recovery and no autoplay | Complete ZIP restore, explicit settings recovery, owned apphost kill/restart, actual ACL write denial/file locks/restore rollback and corrupt/oversized portrait-artwork checks passed. G9 actual full-disk/save+backup recovery, read-only portable fallback choices, huge tags, native watcher overflow/cancellation and kill during production migration/retry passed. Actual power loss/manual dialog/Windows 11 remain separate |
| AC-033 stress/resources | Real two-hour output and 1000 transitions; resource samples after warm-up; exact CPU/core accounting, hardware/dataset, handles/working set and p95 operations | Decoder/PCM stress is separate from a two-hour playback/device soak |
| AC-034 offline traffic | Monitor this PID and child processes during representative local workflows; record method/tool/version; distinguish OS/dev-tool traffic | Controlled hosted G10 ETW with positive controls and zero application events passed; clean disconnected Windows 11 baseline remains separate |
| AC-035 licensing/inventory | Intended owner use/app source license; BASS/add-ons terms; AAC GPL/FAAD2 and matching source/licensed path; TagLib/TTA LGPL source/relinking; Windows SDK redistributable list; SQLite/runtime notices | nuspec metadata and retained texts do not approve commercial distribution or satisfy every source obligation |

## Manual record template

For each AC record: source commit; package SHA-256; exact OS build; standard-user status; CPU/RAM/storage; runtime/device/driver; dataset and source hashes; cold/warm state; start/end UTC; precise steps; measured values/capture paths; passed/failed/blocked/not-run; deviations and remaining issue. Never replace an unavailable workflow with a passed placeholder. Keep owner music and personal paths outside committed evidence.

Run Verify-Candidate on a copied/extracted candidate. For a negative integrity check, change only an owned candidate copy and confirm verification fails, then re-extract. Do not alter source music. Back up owned test databases before crash/full-disk scenarios. File association setup and installer/signing remain separately requested work.


G13 notice completeness/provenance preparation is implemented; [distribution review](DISTRIBUTION_REVIEW.md) separates the retained MIT/Apache/LGPL/native original texts from unresolved owner rights and complete corresponding-source/relinking/SDK evidence. AC-035 is not accepted by this change.
