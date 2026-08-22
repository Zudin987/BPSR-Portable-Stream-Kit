# StreamKit v2.2.0

## Easier platform setup

- TikTok is now optional. Discord and Twitch can start without a TikTok stream key.
- When TikTok is not connected, StreamKit shows **Start Twitch + Discord** instead of blocking the workflow.
- The TikTok setup card remains available so a server/key can be added later.
- If Twitch is not connected either, Discord sharing still remains usable and StreamKit explains what is missing.

## Four synchronized scenes

The scene controller now exposes the same four choices in Discord-only and all-platform modes:

- **Starting Soon**
- **BRB**
- **Game Clean** — game + frame + avatar, without DPS/mechanic HUD overlays
- **BPSR** — game + frame + avatar + DPS meter + dungeon mechanic HUD

Horizontal and TikTok vertical views switch together when the vertical canvas is active.

## Safer OBS and VTube Studio startup

- VTube Studio launch requests are throttled; StreamKit waits for an already-starting process rather than repeatedly asking Steam to launch it.
- Existing portable OBS setup windows are reused rather than duplicated.
- OBS gets a much longer graceful shutdown period before a force-close is considered.
- StreamKit detects the OBS 32 unclean-shutdown prompt for its own portable OBS and continues in Normal Mode automatically, because StreamKit requires plugins and WebSockets.
- Start/stop/setup/avatar/scene actions are routed through one controller so legacy async click handlers cannot run a second time.

## Compatibility repairs

- Normalizes old/current scene source naming differences before OBS starts.
- Repairs `BPSR Game + Audio` to `Selected Game + Audio` when needed.
- Repairs the old `TikTok Live` naming to the vertical clean-game scene expected by the current automation.
- Adds explicit clean and BPSR HUD scene variants for both horizontal and vertical layouts.
