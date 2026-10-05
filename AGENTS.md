# Repository working agreement

- Communicate with the owner in Russian. Use English for source identifiers, comments, commits and repository documentation. Product UI localization follows the specification (English fallback, Russian in Stage F).
- The owner authorizes automatic commits, pushes and merges for this project. Preserve concurrent work; never force-push to resolve divergence. Check meaningful tests before integrating changes. Do not publish GitHub releases or make licensing purchases without separate authorization.
- Use this existing checkout. Cloud tasks are already isolated; do not create worktrees unless requested.
- Resume from `docs/IMPLEMENTATION_STATUS.md` and follow `docs/ROADMAP.md` and the full provided specification. Keep acceptance evidence truthful, especially Windows/device checks.
- Stack: .NET 10, WPF, CommunityToolkit.Mvvm, ManagedBass/BASS, TagLibSharp, Microsoft.Data.Sqlite, xUnit. Core must stay platform-independent.
- Use pinned dependencies and locked restores for verification. Do not disable signatures, hashes, TLS verification or tests. Do not commit native DLLs, generated artifacts, credentials, private music or local databases.
- Windows-only tests cannot pass on Linux. Do not replace real decoding/waveform/output with simulations. Keep source audio unchanged.
- Update implementation status, test results and applicable requirement statuses before handoff. No delegation is requested by this file.
