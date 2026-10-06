# G10 desktop and offline acceptance

The portable candidate includes Windows PowerShell 5.1-compatible acceptance runners. No SDK, encoder download or installed .NET is needed by the apphost. Run the scripts from the extracted candidate with Windows PowerShell or PowerShell 7:

```powershell
./acceptance/Desktop-Acceptance.ps1 -CandidateDirectory . -Language en
./acceptance/Desktop-Acceptance.ps1 -CandidateDirectory . -Language ru
```

The runner checks the complete candidate inventory/checksums and copies only manifested files into a fresh token-owned workspace. It never imports existing portable Data, touches source music, terminates processes by name, changes adapters/registry/audit policy or seizes an existing ETW session. It generates low-amplitude owned PCM16/WAV and CUE fixtures, runs the self-contained app from a different working directory with invalid external runtime paths, checks native preparation/waveform/CUE/library and automation, persists the session, closes the actual apphost, then starts it again without autoplay. Each report records source commit, package manifest SHA-256, OS, process privilege, observed adapters/runtime locations, phase and source hash. Workspace files remain available for inspection.

An independently named Kernel-Network/Kernel-Process ETW session observes actual app PIDs and descendants. An owned TCP loopback exchange before and after both apphosts is a positive control. Native stop-time lost-event/buffer counters must be zero; root start/end events, PID fields and both control sides are required. Missing, lossy, truncated or un-attributable evidence cannot pass as zero network traffic. A separate apphost analyzes the XML after tracing stops. Reports contain counts and hashes, not captured network addresses or packet payloads. Raw ETL/XML may contain unrelated OS process/network metadata: keep them local, do not commit or share them without review.

ETW collection normally needs an elevated observer. The player remains `asInvoker`; this script does not elevate it or change the user's token. `-SkipTraffic` allows independent non-elevated desktop checks and explicitly leaves traffic untested. A standard-user player with a separate elevated trace observer is a manual acceptance workflow; running the entire script elevated does not establish standard-user acceptance.

`-RequireCleanBaseline` rejects unmet observed prerequisites: Windows 11 **client**, app not elevated, no up adapters, no SDK/runtime in checked global/user locations or PATH, and successful trace. It never disconnects a machine automatically. Runtime detection covers named locations and PATH rather than scanning private drives; record the clean image provenance separately. This prerequisite check does not close the full P0 gate: representative MP3/FLAC workflows, physical keyboard/Narrator, 100/150/200% monitor changes/removal and listening still need their own records.

Automation uses actual WPF peers, accessible track/tab names, range/toggle patterns, Tab focus traversal, state/error live regions and minimum-window bounds. PNGs render the actual visual at 96/144/192 bitmap DPI; they are **not** physical DPI transition proof. The report separately records actual window DPI, PerMonitorV2 awareness, monitor bounds and observed high contrast. Hosted Windows Server and an installed SDK cannot be relabeled Windows 11/offline baseline acceptance.

For a manual record retain source/package hashes; clean image and OS build; standard-user app and observer privileges; physical monitors/scales; screen-reader/tool versions; each exact workflow and result; start/end UTC; dataset hashes; measured traffic/captures; remaining blocked/not-run items. Never write a passed placeholder for an unavailable check.
