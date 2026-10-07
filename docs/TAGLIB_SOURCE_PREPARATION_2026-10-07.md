# Exact TagLibSharp source preparation and replacement check

The deployed managed dependency remains pinned to TagLibSharp 2.3.0. No application license, dependency version or shipped decoder is changed.

## Prepared material

The complete official commit archive for `b5ae84f2e84087bf160bb0471420200dd2b5d809` is 102,677,407 bytes, SHA-256 `ab6fab7e22c7423e428f591d7525a38ca283d217a49f9772eb9a29cd682c175d`. It contains 673 ZIP entries, 125,890,796 declared uncompressed bytes, including COPYING, AUTHORS, the complete source/tests/examples, signing/build material and original workflows. Preparation retains the archive unchanged and inventories every extracted file. Archive/download digests establish continuity to this reviewed HTTPS snapshot, not publisher authentication or universal license clearance.

`scripts/Prepare-TagLibSource.py` requires a new directory, fixed size/hash and local confined regular entries; it performs no build or upstream script execution. Existing-output and corrupt-archive controls preserve their sentinels and reject extraction. A reviewed source-only `netstandard2.0` Release build under SDK 10.0.401 succeeds with zero warnings/errors. The test project from upstream (with its historical dependencies) is not restored or executed.

Both NuGet and rebuilt DLLs identify `TagLibSharp, Version=2.3.0.0, Culture=neutral, PublicKeyToken=db62eba44689b5b0`. A separate owned Core-test output copy passed **284/284 tests**, zero failures/skips, after copying the rebuilt DLL. **Correction: Core does not reference/load TagLibSharp; this result establishes Core regression continuity, not TagLib replacement/relink compatibility.** The normal application/test outputs retain the pinned NuGet binary. [Source/build hashes and test counters](evidence/taglib-source-preparation-2026-10-07.json).

## Repeat the preparation

```bash
python scripts/Prepare-TagLibSource.py artifacts/taglib-source-review
# Read the source project and Directory.Build.props/targets before building.
dotnet build artifacts/taglib-source-review/taglib-sharp-b5ae84f2e84087bf160bb0471420200dd2b5d809/src/TaglibSharp/TaglibSharp.csproj -c Release -p:LibTargetFrameworks=netstandard2.0 -p:GeneratePackageOnBuild=false
```

For actual replacement testing, run `pwsh -NoProfile -File scripts/Test-TagLibReplacement.ps1` in disposable Windows x64 CI or an explicitly configured isolated lab. It applies a source-only locked restore, copies application outputs into a fresh owned directory, replaces only TagLibSharp.dll and runs the real EN/RU WPF metadata/native workflow. Reports require the loaded DLL identity/SHA-256 to match the rebuild and original outputs/fixtures to remain unchanged. The copied 286-test Core suite is a separate regression gate, explicitly not a TagLib-loading assertion. A mandatory `taglib-replacement` CI job gates ZIP publication. Windows observation is pending this source run. The source preparation is not included automatically in the player ZIP and is not a written corresponding-source offer.

## Limits still open

The rebuilt PE SHA-256 differs from the NuGet PE SHA-256. Modern SDK compilation plus matching assembly identity does not establish the exact NuGet build recipe or instruction-for-instruction equivalence. The original compiler/build reproducibility, complete matching-source distribution mechanism and permissions remain open. Windows replacement/WPF evidence is now a dedicated source-specific CI gate; it remains unobserved until that run passes. LGPL text remains in ordinary packages, but this preparation does not decide all distribution obligations. AAC/BASS_TTA implementation source, BASS/commercial grants and the owner's application-license decision remain separate requirements.
