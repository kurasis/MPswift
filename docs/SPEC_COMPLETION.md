# Specification completion block — 2026-10-07

The committed specification is byte-for-byte identical to the original uploaded ZIP (SHA-256 `cecdaf961e7553b739bc853e1a16cb748ddbfa86feca45873c46c152d9d9a640`). Later owner requests change the product name/icon/design and authorize development ZIP publication; they do not waive P0/P1 acceptance.

## Implemented follow-up

| Requirement | Behavior | Evidence boundary |
| --- | --- | --- |
| §11.1 duplicate management | Preview/count; first canonical path + exact logical bounds retained; CUE songs remain distinct; stale preview refused; no audio deletion, queued/current snapshots retained | Domain tests and production WPF workflow |
| §13.3/§19.1 cache | Actual usage; configurable live budget; LRU eviction; explicit clear; old jobs cannot refill after clear; unrelated files/database preserved | Real file tests and WPF/native regeneration |
| §15.5/§19.1 settings | Accent, energy/peak waveform, repeat/shuffle defaults, active-session/position restore, remembered local open directories; old schema/default compatibility | JSON roundtrip and actual production reopen |
| §20.3 diagnostics | Local inspectable redacted preview; bounded clipboard report with app/native/deployed-decoder/output/DSP/log information; no automatic sending | Plain/JSON/Unicode redaction and actual WPF clipboard |
| §9.3 power recovery | WM_POWERBROADCAST suspend/resume requests pause + saved context; no automatic resume | Owned software paused-source check; physical sleep still not-run |
| §21/previous audit | Paused EQ edits retain only immediately used filter state; smoothing curve/filter ownership/public constructor retained | 1000-processor collection and independent 20 ms output curve |

UI strings are English/Russian. Cache work remains local/background. Defaults preserve current startup and rendering behavior. The PlayerSettings positional constructor and schema version remain intact; additional init properties have explicit legacy defaults. No package/native upgrades or music writes are introduced.

Two additional owned 7201-second M4B/APE fixtures add bounded real native duration/range/EOF and production prepare/seek/stop checks. APE decoded float offsets exceed Int32; its highly compressed silent source is not a >4 GiB encoded APE test. Failed WASAPI Start now frees the unusable context before explicit retry; real driver recovery still needs device acceptance.

## Acceptance that cannot be inferred from code completion

- **AC-001/002/034:** clean supported Windows 11, standard user, no installed .NET, physically offline before extraction/first launch. Hosted controlled PID/descendant ETW already passed; this separate baseline has not.
- **AC-003/005/017–022:** actual endpoint consumption/listening, busy exclusive, hotplug/default switch, latency/app/system gain and digital boundary capture. Existing hosted outputs are unavailable; callback PCM is not endpoint proof.
- **AC-013/030/036:** cold/reference-PC performance, physical multi-monitor DPI and Narrator. Hosted warm 100k/10k and software peers/renders are narrower passed evidence.
- **AC-033:** two-hour real output plus 1000 transitions/full player resource stability. Existing native PCM stress/short soak passed; real-device profiles remain blocked without devices.
- **AC-004/037:** every required representative encoding/environment including actual Windows N and large/long media. The recorded 30 profiles and owned WMA/RF64 checks are specific coverage, not all combinations.
- **AC-031/039:** physical power loss remains distinct from recorded process-kill/migration/disk-full tests.
- **AC-035:** owner intended use and application license, BASS/add-on conditions and GPL/LGPL matching-source/relinking requirements remain distribution decisions. Publishing a development candidate does not approve stable 1.0 or licensing purchases.

P2 extensions (light theme, mini-player, spectrum, module/MIDI/AC3/DTS, chapter UI, installer/ASIO/native DSD) remain optional. This block does not silently introduce them or declare all P0/P1 accepted.

The pinned native AAC/MP4 decoder reports stereo PCM for the owned mono M4B. The manifest distinguishes independently encoded mono from decoded stereo; validation requires equal left/right PCM rather than changing the original source facts. UI/diagnostics additionally show encoded metadata channel count when it differs from native decode channels. This is not bit-perfect mono decoding.
