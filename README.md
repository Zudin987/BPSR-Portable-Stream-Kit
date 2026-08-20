# BPSR Portable Stream Kit

A beginner-friendly portable streaming launcher for **Blue Protocol: Star Resonance**.

The goal is simple: extract once, open the launcher, then start Discord/Twitch/TikTok streaming without manually managing OBS profiles, portable mode, plugins, or capture-source paths.

## UX principles

- **One primary action:** Start Discord.
- **Automatic setup:** OBS Portable + FloodTuber + scene templates are prepared for the user.
- **Privacy by default:** no full-monitor capture and no global desktop-audio capture in the default scene.
- **Simple status:** BPSR, Resonance Logs, OBS and microphone readiness are shown in plain language.
- **Advanced settings stay hidden** until the user asks for them.
- **Portable:** no installer is required for normal use.

## Planned distribution

GitHub Actions builds a self-contained Windows launcher (`BPSRStreamKit.exe`). The launcher downloads/repairs the supported portable OBS setup on first run and keeps the user's local streaming-account credentials out of the repository.
