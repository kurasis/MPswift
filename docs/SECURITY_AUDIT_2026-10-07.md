# End-user security audit — 2026-10-07

Scope: source at `6156d55c9d509747afbc034a70baf00194e8bff4`, locked dependencies, the verified Windows build 66 package, portable distribution, build/acceptance/publication scripts and actual CLI/IPC. Static inspection preceded any repository execution. Dynamic checks use only GUID-named temporary synthetic databases/archives in the isolated cloud workspace and disposable Windows CI; no real workstation, owner music, malicious executable or fuzzed native decoder is used. No application installation/update policy or dependency version is changed by the approved fixes below.

## Confirmed findings and minimal fixes

### SEC-01 — Unqualified Explorer executable (medium)

- File/section: `src/Player.App/Views/MainWindow.xaml.cs`, `OnShowFile`, formerly `new ProcessStartInfo("explorer.exe")` with UseShellExecute=false.
- Conditions: an attacker can plant an executable in a searched application/working/PATH directory and the user invokes Show file in Explorer. Windows CreateProcess search order for an unqualified module can select that executable. This is conditional local executable planting, not filename-to-shell injection or a remote execution claim.
- Damage: arbitrary execution with the player's existing user rights, potentially access to that user's files. asInvoker prevents this from independently elevating rights.
- Fix: resolve Explorer inside the Windows Known Folder and keep validated source and /select as separate ArgumentList entries. Refuse an unavailable/unqualified Windows folder. Never use cmd/PowerShell to reveal a file.
- Target check: actual WPF integration constructs the production command while CWD contains an owned inert explorer.exe shadow, verifies absolute trusted FileName and separate arguments, then restores CWD. The shadow is never launched.
- Reference: https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw . The documented module search order supplies the exploitation basis; no hostile executable is executed.

### SEC-02 — PATH lookup of privileged diagnostic tools (medium, potentially high when run elevated)

- Files/sections: `scripts/Resilience-Smoke.ps1` Invoke-OwnedDiskPart; `scripts/Acceptance-Helpers.ps1` ETW start; `scripts/Desktop-Acceptance.ps1` ETW stop and trace conversion. Commands formerly invoked diskpart.exe/logman.exe/tracerpt.exe by name.
- Conditions: a developer/operator runs the optional acceptance script with a hostile earlier PATH entry (or shell command shadow). PowerShell external command resolution differs from CreateProcess: CWD alone is not an implicit PowerShell search directory. Normal player use never invokes DiskPart/ETW.
- Damage: attacker code inherits the diagnostic runner's rights, including administrator rights if already granted; an operator could expose disks/data. No automatic player elevation exists.
- Fix: invoke the exact System Known Folder executable paths, preserving existing arguments, token-owned VHD selection, bounded ETW session and existing cleanup. No physical disk is selected.
- Validation: PowerShell AST parsing locally; actual revised diagnostic flow only in disposable Windows CI. No DiskPart/ETW invocation on the local workspace/workstation. The existing G9 workflow creates a unique 128 MiB virtual disk and never selects a physical disk.
- Reference: https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_command_precedence . Build tools such as dotnet/ffmpeg remain explicitly provisioned toolchain commands; these are not arbitrary commands from music metadata.

### SEC-03 — Database size checks after materialization (medium availability risk)

- Files/sections: `src/Player.App/Services/Storage/SqlitePlayerStore.cs` LoadAsync `reader.GetString(2)` before the 524288-character check and ExecuteScalar before the 16 Mi-character session check; `SqliteLibraryIndex.cs` JSON reads; untrusted backup database readers in `DatabaseRecovery.cs` and `BackupBundle.cs`.
- Conditions: a user restores an attacker-controlled valid SQLite backup, or an attacker can replace the data DB, with very large cells. The ZIP database limit of 2 GiB did not limit native/managed allocation for a single cell. Reading the string first makes the later size rejection too late for availability.
- Damage: high memory use and possible out-of-memory termination before rejecting the input; no SQL injection or native code execution is established by these tests.
- Fix: configure per-connection SQLITE_LIMIT_LENGTH=49 MiB before first validation/query on all production source/store/ZIP database readers. This limits native cell/row allocation before materializing huge values. Existing 16 Mi UTF-16 session characters need at most 48 MiB in UTF-8, with 1 MiB row overhead retained. Existing stricter field/JSON limits still apply; no global process setting, schema or valid-session limit changes.
- Evidence: both owned 60 MiB Tracks and Session cases initially reached the late managed InvalidDataException; after the fix SQLite refuses with SQLITE_TOOBIG (18), database hashes remain unchanged and restoring valid test content lets the same production store reopen. No unbounded/gigabyte input is generated.
- Reference: https://www.sqlite.org/limits.html . This cap is not a complete bound on database validation CPU, row count or all native parser allocations.

### SEC-04 — Truncated manifest accepted (low integrity hardening)

- File/section: `src/Player.App/Services/Storage/BackupBundle.cs`, ExtractValidated manifest JSON deserialization from entry.Open without checking actual body length.
- Conditions: ZIP central directory declares a larger manifest body (within 64 KiB) than the actual valid JSON stream. The baseline restored this inconsistent archive successfully.
- Damage: acceptance of damaged/ambiguous backup metadata despite other entry-length guarantees. Payload hashes and strict file names already protect ordinary data entry integrity; no archive traversal, arbitrary destination or memory exhaustion is established by this finding.
- Fix: bounded manifest buffer <=64 KiB, ReadExactly, explicit truncated/beyond-size rejection before deserialization and before installing user data.
- Evidence: an owned ordinary complete backup is patched only in the central-directory uncompressed manifest length; the baseline accepts it, the fixed version rejects and preserves current DB/settings with no preserved/staging leftovers. All normal backup/rollback cases continue to pass.

### SEC-05 — Unowned development UI validation route (medium, fixed with owner approval)

- File/section: `src/Player.App/App.xaml.cs`, uiSmoke argument detection; `UiSmokeValidation.RunAsync`, fixed artifacts/smoke data/report locations and cover creation; CompletionValidation explicitly replaces/reads clipboard for its diagnostic test.
- Conditions: the shipped executable is launched with --ui-smoke and two usable fixture paths in an arbitrary CWD, without the GUID ownership marker used by other acceptance routes. A crafted local shortcut/invocation can activate a developer-only write/test workflow. It is not an IPC/network command and does not expose an internet endpoint.
- Damage: unwanted test data/report/artwork writes under CWD/artifacts, including conflicts with pre-existing diagnostic paths, and unsolicited clipboard replacement. No source audio modification is inferred; source hashes are checked.
- Fix: `Services/Storage/UiSmokeWorkspace.cs` requires directory name mpswift-ui-smoke-<GUID N> and a matching <=128-byte UTF-8 .player-ui-validation marker before files/clipboard/IPC. Root/parents/entries refuse reparse/offline/recall attributes. The bounded directory walk permits only the marker and optional artifacts/smoke/stage-c-data/settings.json language seed <=64 KiB. An invalid workspace exits 2 before reporting or normal player startup. Normal launch and argument spelling remain unchanged.
- Approval: the owner explicitly agreed to protect the diagnostic CLI. Smoke.ps1 creates a fresh workspace per EN/RU run, copies evidence, then removes only that owned workspace; Package-Smoke.ps1 seeds its own token workspace under the Unicode extraction path. Existing CI invokes these updated runners.
- Evidence: ten targeted workspace cases cover fresh/seeded data, missing/mismatched/oversized markers, unexpected content, oversized settings, ordinary directory names and linked marker/output paths. The Windows integration test launches actual apphosts in missing-marker, wrong-token and matching-marker/foreign-file workspaces, requiring exit 2, unchanged files/clipboard and no artifact directories before existing IPC tests. ACL/hardlink/concurrent reparse races remain separate checks.

## Areas inspected without a confirmed exploit

- Commands: runtime has only explicit Explorer reveal; input is lexically/filesystem validated, arguments are separate. No shell eval, user-metadata command interpolation or document/browser execution. Python fixture generators use argv lists/check=True, no shell=True. No remote script is piped into execution.
- Files: local drive/UNC/ADS/device restrictions, parent/reparse/network/hydration validation, bounded metadata/artwork/CUE/settings/wave caches. Audio is read-only. ZIP backup accepts exactly library.db/settings.json/manifest.json, refuses duplicate/unexpected/traversal entries, checks actual data lengths/hashes and validates the staged DB before retaining/installing originals. New backup files use CreateNew/no overwrite; restore preserves old DB/WAL/SHM/settings and rolls back. Export only permits .m3u8. Cache/log deletion stays in configured application subdirectories.
- Privileges: manifest is asInvoker/uiAccess=false. No service, registry association, scheduled task, startup registration, installer, firewall change or autonomous privilege request. Close-to-tray is opt-in (default false); owned database/waveform/log threads are background threads in the player process.
- Privacy: no runtime HTTP client, browser store access, document collection, camera or normal microphone recording. User-selected roots scan media; sibling cover reading is local. SMTC shares current metadata with the local OS media UI. Clipboard copying is an explicit UI action except SEC-05's diagnostic test. Optional Digital acceptance captures bounded loopback (may include other app audio) only through its explicit marker-guarded local workflow; no upload.
- IPC: current-user-only named pipe, per-SID name, bounded 64 KiB JSON/depth/path counts/32-request queue, per-connection deadlines; only append local paths/activate/explicit play. No HTTP/TCP player API. Optional ETW positive control uses a temporary loopback TCP listener and closes it; no ordinary runtime open port.
- Secrets: no password/token credential feature. Checked 205 tracked text files for high-confidence private-key/GitHub-token/AWS-ID patterns without printing values; zero matches. This is not full secret-history/binary scanning or proof against arbitrary custom secrets. GitHub publication uses injected job token, not a committed token; no environment/credential dump is performed. Local logs are bounded and redact configured prefixes; diagnostics are previewed/redacted before explicit copy.
- Supply chain: one HTTPS NuGet source, locked resolved/content hashes, native official HTTPS archives with SHA-256/DLL/companion pins; pinned SDK/PowerShell download hashes and action commits. No disabled TLS certificate validation. Build jobs have contents:read; publication alone has contents:write and runs after main checks. No pull_request_target or untrusted PR publication. Dependency build-time code still executes in CI and must be trusted/isolated.
- Native loading: own app/native directory and verified file hashes, resolver refuses unknown ManagedBass imports. This is not an Authenticode signature or absolute proof about Windows transitive DLL search/TOCTOU; see residual checks.
- Installation/updates: portable ZIP, manually downloaded/extracted. No installed/updating background service, automatic download/execute, rollback channel or updater exists. ZIP/inner hashes and GitHub API asset digests are checked by development publication/acceptance, but a malicious actor who replaces both unsigned bytes and colocated manifests is outside hash-only authentication. Installer/updater/signing design would require owner agreement and certificate/key management; none is introduced here.
- No Electron/WebView/web-content execution/XamlReader loading from media was found; WPF metadata is ordinary bound text.

## Official dependency check and boundaries

[Retained exact versions/feed URLs/digests](evidence/security-dependencies-2026-10-07.json) cover 26 locked managed/test/framework identities. Official NuGet VulnerabilityInfo snapshot updated 2026-10-06 and GitHub advisory GHSA-2m69-gcr7-jv3q were queried with normal TLS. The SQLite advisory CVE-2025-6965 affects SQLitePCLRaw.lib.e_sqlite3 <=2.1.11; pinned 2.1.12 is outside that range. Actual packaged native e_sqlite3.dll strings identify SQLite 3.53.3/source ID, matched to upstream release log. No matching advertised range was found for the other checked identities. This is a dated database observation, not zero-day/native decoder clearance.

Official .NET 10 release metadata identifies deployed runtime 10.0.12 as the latest security release at this check. Its CVE list records fixes, not vulnerabilities automatically attributable to that patched release. Official BASS page identifies 2.4.18.3, matching the core pin. Complete maintained advisories and exact embedded FLAC/Opus/FAAD/APE/WV/etc version/build mappings were not available for every closed addon; wrapper versions cannot substitute for them. No speculative CVE is assigned to a DLL without evidence. Windows/WIC/codec components also depend on actual OS patching, which this Linux/static audit does not establish.

## Residual work in an isolated environment

- Arbitrary-workstation invocation of diagnostic scripts remains unsuitable. The approved UI-smoke marker is an accidental/shortcut misuse guard, not authentication against a malicious process already running as the same user; deliberate synthetic validation still modifies its isolated clipboard and requires a disposable session.
- Windows standard-user ACL/reparse/hardlink races and transitive native DLL dependency search; do not run planted executables on a real workstation. Current same-user-writable package/data locations are not a security boundary against code already running as that user.
- Native audio/image/database fuzzing under resource limits and disposable VM/worker. Current malformed-tag/image/archive tests do not establish memory-corruption safety for every native parser.
- CPU/time/ZIP central-directory entry-count limits and huge database schema/validation workloads; the new row limit addresses cell allocation only. Process-level parser isolation would change architecture/behavior and needs design agreement.
- Independent signed release/update provenance and owner-controlled signing keys, if requested. Existing hashes provide integrity relative to trusted metadata, not publisher authenticity by themselves.
- Complete matching-source/license acceptance remains tracked in DISTRIBUTION_REVIEW.md; this audit does not purchase rights or select a source license.

## Verification

Baseline: locked Release build, 215 tests, zero warnings/errors. Targeted demonstrations intentionally failed before the relevant fix. Post-fix three archive/large-cell cases pass, with source hashes/current-data preservation and valid-data reopening. Post-fix locked Release build passes **228 tests, zero failed/skipped, zero warnings/errors**, including ten workspace cases. PowerShell scripts parse without executing DiskPart/ETW locally. Actual Explorer command construction, rejected UI-smoke apphosts, changed diagnostic paths and EN/RU/extracted package validation await the source Windows CI at this commit's local checkpoint; final CI evidence is retained with the published build. No full-safety claim follows from these results.
