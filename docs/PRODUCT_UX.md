# Product & UX direction

## Product promise

**Open the launcher, click one button, stream the exact BPSR layout without exposing the rest of the desktop.**

The launcher is intentionally not an OBS replacement. It hides OBS setup work until the user actually needs advanced controls.

## Main screen hierarchy

1. **System readiness** — three plain-language checks: OBS, BPSR, Resonance Logs.
2. **Primary action** — `Start Discord` is visually dominant because it is the most common path.
3. **Secondary platforms** — Twitch and TikTok remain one click away without competing with the main action.
4. **Expectation setting** — short notes explain what happens after clicking without requiring a manual.
5. **Advanced tools** — Open OBS and Repair Setup are hidden under Settings.

## Why this layout

- Users should never have to understand `portable_mode.txt`, scene collections, profiles, window classes, encoder IDs, or plugin directories.
- A missing optional app should not look like a fatal error. Resonance Logs is shown as optional when BPSR itself is ready.
- First-run setup reuses the same primary button rather than introducing a separate installer wizard.
- Repair is non-destructive: existing scene positions, profile changes, Twitch/TikTok account data, and stream keys are preserved.
- Account credentials are never bundled in source control or release assets.

## Visual direction

The UI uses a dark neutral surface with restrained pink-to-violet accents inspired by the included avatar/frame art. Decorative styling is intentionally limited to the launcher identity and primary CTA so it stays readable and does not look like a game-cheat utility or a complex broadcasting dashboard.

## Default streaming behavior

- Discord/Twitch: 1920×1080, 60 FPS, Simple output mode.
- TikTok: 1080×1920, 30 FPS, Simple output mode.
- Hardware encoder is selected automatically when NVIDIA/AMD/Intel is detected; x264 is the fallback.
- Capture is application/window based. No default full-display capture.
- BPSR audio is captured with the game source; global desktop audio is not part of the default scene.
