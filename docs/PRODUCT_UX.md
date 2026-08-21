# Product & UX direction

## Product promise

**Pick a game, pick a destination, and get a clean private stream layout without manually building OBS scenes.**

StreamKit is a game-streaming launcher, not a replacement for OBS. It hides setup complexity until the user intentionally opens advanced controls.

## Main screen hierarchy

1. **Destination** — Discord, Twitch, or TikTok.
2. **Theme** — choose the visual profile.
3. **Game** — select the running game to capture.
4. **Readiness** — plain-language checks for the selected game, portable OBS, avatar layer, and private audio/capture setup.
5. **Primary action** — open the prepared stream layout for the selected platform.
6. **Advanced tools** — Open OBS, Open folder, Repair, and game rescan remain secondary.

## Core behavior

- The default path must work for games generally, not depend on one specific title.
- A game-specific integration may add optional sources or layout enhancements, but the base clean-game workflow remains the product core.
- Users should not need to understand scene collections, window classes, encoder IDs, portable mode, or plugin directories.
- First-run setup reuses the same primary action instead of introducing a separate installer wizard.
- Repair should preserve user account data and local customizations whenever possible.
- Account credentials are never bundled in source control or release assets.

## Platform guidance

### Discord

StreamKit prepares and opens the clean OBS source. Discord still performs the actual screen-share action. The UI should explicitly tell the user to share the OBS Projector/preview window in Discord after pressing **Open Discord Stream**.

### Twitch / TikTok

StreamKit opens the prepared OBS layout. The user connects the platform account/stream method locally and starts the broadcast from OBS.

## Visual direction

The launcher uses a dark neutral surface with restrained accents. Each stream profile can be more expressive:

- **Profile A — Sakura Catgirl:** thin pink/violet frame, edge-hugging margins, tiny petal/sparkle accents.
- **Profile B — Chibi Doctor:** thin cyan/white frame, edge-hugging margins, tiny medical cross/ECG/tech accents.

Starting Soon and BRB screens should stay visually themed but extremely simple: one large phrase only (`STARTING SOON` or `BE RIGHT BACK`). No subtitles, profile labels, game names, or explanatory copy.

## Default streaming behavior

- Discord/Twitch: 1920×1080, 60 FPS.
- TikTok: 1080×1920 vertical layout.
- Hardware encoder is selected automatically when available; x264 is the fallback.
- Capture is application/window based. No default full-display capture.
- Default audio focuses on the selected game + microphone rather than global desktop audio.

## Compatibility note

Some internal filenames and namespaces still contain historical `BPSR` naming for compatibility with existing templates and upgrades. User-facing product copy should remain game-generic.
