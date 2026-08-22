# StreamKit Changelog

## v2.4.0 — frame styles and scene cleanup

- Expanded **Frame style** from 2 to 9 choices: Sakura, Chibi Doctor, Neon Tech, Black Gold, Crimson Demon, Ice Crystal, Forest Mystic, Cyber Orange and Moonlight Silver.
- Added matching horizontal + TikTok vertical frames and Starting Soon / BRB screens for the seven new styles.
- New styles are generated once on first selection and cached locally under `user-data/frame-themes` instead of bloating the release ZIP with dozens of large images.
- Kept frame selection independent from avatar selection; changing a frame never replaces the VTuber/PNG avatar.
- Preserved compatibility with the existing Sakura / Chibi Doctor A/B preference while adding a separate expanded-theme preference.
- Removed the redundant **Discord Share** and **Twitch Live** OBS scenes after their layout has been migrated into **Game Clean** and **BPSR**.
- Existing installations clean those two legacy scenes automatically the next time StreamKit prepares the scene collection.
- Added a normal busy/wait state while a never-used frame style is generated, plus safe rollback if theme generation cannot be saved.

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
