# StreamKit

A zero-config portable streaming launcher built around **Blue Protocol: Star Resonance**, with a clean fallback layout for other games.

Extract once, open `BPSRStreamKit.exe`, choose a destination and game, then launch the prepared portable OBS scene without managing OBS profiles, plugins, capture paths or audio routing by hand.

## v0.2 highlights

- **One-click hero action** with Discord, Twitch and TikTok destination switching.
- **Game switcher** that detects running game windows and remembers recent selections.
- **BPSR Full layout:** game + frame + FloodTuber + DPS meter + Dungeon HUD.
- **Game Clean layout:** selected game + frame + FloodTuber only; BPSR-only DPS/HUD stay hidden.
- **Privacy by default:** no Display Capture and no global desktop-audio source in the prepared scenes.
- **Readiness HUD:** game hook, portable engine, avatar layer and isolated audio shown in plain language.
- **Progressive disclosure:** repair/open-folder/OBS tools stay behind Advanced.
- **Portable:** no normal OBS installation is required.
- **Administrator manifest:** the launcher elevates automatically so it can reliably detect/hook elevated games such as BPSR.

## Distribution

GitHub Actions builds a self-contained Windows x64 launcher and validates that the release ZIP contains the required templates and visual assets. Twitch/TikTok account credentials and stream keys remain local and are never committed to this repository.
