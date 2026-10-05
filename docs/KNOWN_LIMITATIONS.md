# Known limitations

- Stage A is the development foundation, not a functional music-player release. The WPF window only checks native dependencies. The actual transport/playlist/waveform UI starts in Stage B/C.
- The cloud host is Linux x64. WPF launch/bindings, native BASS/WASAPI execution, device behavior, listening, DPI, screen-reader use and offline Windows launch cannot be verified on this host.
- Windows CI has successfully exercised native loading and PCM WAV decoding without an audio endpoint. Real output must be tested on an interactive Windows machine using `scripts/Smoke.ps1 -Play`; listening and digital capture require separate evidence.
- No codec/profile is advertised as verified until its actual fixture checks run. Stage A provides a generated PCM WAV fixture, not the complete P0 format suite.
- `BassSmokeSession` has bounded small-fixture decoding and synchronous ownership. It is not the production background/long-file player and is not wired into user playback controls.
- The lexical local-path policy rejects remote URLs, UNC/device paths and alternate streams. The Windows harness rejects mapped network drives and offline file attributes before reading source content. Reparse-point destinations, provider-specific hydration and changing filesystem state need expanded production validation in Stage B/E.
- No database, persisted session, queue, shuffle, CUE parser, waveform, metadata/artwork, EQ, library, Windows integration or Russian product UI exists yet.
- The generated self-contained output is local development output only. A portable release ZIP, clean-Windows execution evidence, traffic audit, licensing obligations and commercial-use decision remain release gates.
- Application source licensing has not been selected by the owner. Third-party license conditions remain in force independently of any future application license.
