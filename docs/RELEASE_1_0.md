# MPswift 1.0

The owner explicitly requested version 1.0 and a Windows installer on 2026-10-08. This supersedes the earlier development-only publication authorization for this version. It does not establish unperformed hardware acceptance or resolve third-party rights by itself.

## Install or use portable

- Setup EXE: English/Russian, Windows x64, current user only, no UAC elevation request. Default program folder: `%LOCALAPPDATA%/Programs/MPswift`. The .NET desktop runtime is bundled; setup does not download a runtime. Start menu shortcut is included; desktop shortcut is optional. No startup, background service, automatic updater or file-association/default-player changes are made.
- Installed data: `%LOCALAPPDATA%/MPswift/LocalAudioPlayer`. Reinstall and uninstall retain this directory, music and unknown files. The uninstaller removes registered application files and per-user registration/shortcuts, without recursive user-data cleanup. The program can be removed through Windows Installed apps.
- Portable ZIP: extract the whole folder and keep `Data` when replacing it. The ZIP retains `portable.marker`. Setup refuses to overwrite a portable folder or an unrelated nonempty directory. To move a portable session to installed storage, back up playlists/session in the old player, close it, launch the installed player and restore the backup. Absolute music paths may need explicit relinking if the files moved.
- Setup is unsigned because an owner publisher certificate is not configured. Download from the repository's GitHub release and compare the attached SHA-256; hashes do not establish publisher identity independently of trusted release metadata.

Release version is `1.0.0+build.<run-number>.<attempt>`; the visible product version is 1.0.0, and Windows file metadata records the run/attempt. Ordinary main builds continue as `1.0.<run-number>-dev.<attempt>`. The stable tag is `v1.0.0`; it must not be overwritten. A workflow dispatch with `release_version=1.0.0` is required for the owner-authorized release. All normal checks and the actual installer lifecycle must pass before publishing.

## Installer provenance and verification

The installer uses unmodified Inno Setup 6.7.3. The official GitHub compiler installer is pinned by exact URL, byte length and SHA-256 in `installer/toolchain.json`; Windows validates its Authenticode signature before executing it. It is provisioned only in an owned temporary directory on the isolated Windows build runner. The shipped installer retains Inno Setup's original notices/about metadata; its original [license](INNO_SETUP_LICENSE.txt) is also retained in the product documentation. This build-tool license does not change codec/application obligations.

The installed payload is derived from the already verified portable ZIP. Removing `portable.marker` and adding the installation ownership marker produces its own full per-file manifest/checksum list. Verification permits only the known Inno-generated uninstall metadata when explicitly checking an installed directory. It otherwise retains missing/changed/extra-file rejection, pinned native binaries and notices.

`Installer-Smoke.ps1 -IsolatedRunner` requires a disposable Windows CI runner. It creates a new local standard user, installs/reinstalls/uninstalls with that identity and checks HKCU registration/shortcuts, HKLM absence, retained data and unknown files, and refusal of foreign/portable directories. Real installed EN/RU WPF/native workflows use the runner's existing desktop, not that standard-user identity. Cleanup removes the temporary account and unloaded owned profile; any still-loaded profile remains only until the disposable runner is destroyed. This is Windows Server integration evidence, not a clean supported Windows 11 claim.

## Acceptance and rights still open

Baseline before installer/version edits: locked build with zero warnings/errors and 299 tests passed on 2026-10-08. This document is preparation; source-specific final CI and release assets establish the actual completion outcome.

Clean disconnected Windows 11 without an SDK, physical audio endpoints/power/DPI/accessibility, two-hour output and uncovered encoded profiles remain unperformed. Signing requires the owner's certificate and secure key storage. [Release acceptance](RELEASE_ACCEPTANCE.md), [remaining work](REMAINING_WORK.md) and [distribution review](DISTRIBUTION_REVIEW.md) retain the exact evidence boundaries. Application license/intended use and BASS/addon/AAC/TTA/corresponding-source rights remain owner/vendor decisions. No purchases, automatic application license choice or codec replacement are made. Package `distributionApproved=false` continues to describe those unresolved rights and is not silently flipped by version numbering.
