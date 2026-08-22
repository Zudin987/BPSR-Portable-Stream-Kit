# StreamKit Changelog

## v2.3.1 — OBS session recovery hotfix

- Fixed a regression where StreamKit could close a correctly running portable OBS session when a later WebSocket/control step timed out.
- Added **Retry StreamKit controls** so a slow automation connection reuses the same OBS process instead of restarting it.
- Kept an explicit **Stop** control available during recovery; OBS is only closed when the user asks to stop or when an intentional setup restart is required.
- Made normal VTube Studio verification non-blocking so a slowly loading avatar no longer aborts an otherwise working Discord/Twitch session.
- Increased OBS and VTube Studio startup allowances for slower systems.
- Made public-stream audio, initial scene selection, Virtual Camera and Discord projector preparation degrade safely instead of tearing down a working stream.
- Protected an already-started Twitch/TikTok stream if the optional Discord projector cannot open immediately.
- Fixed misleading **Find games** recovery actions caused by automation errors mentioning the OBS source `Selected Game + Audio`.

## v2.3.0 — architecture and UX overhaul

- Replaced layered/version-specific window controllers with one canonical workflow.
- Rebuilt Quick Launch and stream controls as stable XAML rather than runtime visual-tree mutation.
- Made TikTok genuinely optional; Twitch + Discord can run without Aitum.
- Added four scene controls: **Starting Soon**, **BRB**, **Game Clean**, and **BPSR**.
- Centralized graceful portable OBS shutdown and removed hidden normal-launch force restarts.
- Unified OBS WebSocket control, added atomic settings writes, and hardened process/state handling.
- Improved overflow safety, error routing, progress states, and setup copy throughout the launcher.
