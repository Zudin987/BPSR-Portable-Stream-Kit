# StreamKit — Portable Game + VTuber Streaming

A portable Windows launcher for game capture, VTuber avatars, Discord screen share, and optional Twitch/TikTok streaming. StreamKit prepares the OBS setup so normal use stays focused on **game → avatar → destination → start**.

**Website:** https://zudin987.github.io/projects/streamkit/

> **Quick start:** Extract the complete release ZIP, run `BPSRStreamKit.exe`, open your game, choose an avatar/frame/destination, then press the main start button. Returning users can reuse the remembered setup through **Quick Launch**.

## Stream modes

### Discord only

1. Open the game.
2. Choose **Discord only** and an avatar mode.
3. Press **Start Discord Share**.
4. In Discord, share the clean OBS Program Projector window with sound.

Discord-only mode keeps OBS Mic/Aux muted so your normal Discord microphone remains your voice path and is not doubled.

### Discord + Twitch (+ optional TikTok)

- **Discord:** clean Program Projector window.
- **Twitch:** normal 1920×1080 output.
- **TikTok:** optional 1080×1920 Aitum Vertical output.

Connect Twitch through **Advanced settings → Open streaming engine → OBS Settings → Stream**.

TikTok is prepared only when you choose to add it. Use **Open TikTok setup**, configure an Aitum Vertical stream with the RTMP server/key available to your TikTok account, then use **Check TikTok**. A failed/missing TikTok setup does not prevent Twitch + Discord use.

StreamKit cannot grant TikTok LIVE or stream-key access.

## Quick Launch and scenes

After a successful start, StreamKit remembers the selected game, avatar, frame, and destination. **Customize setup** returns to the full setup screen.

The launcher exposes four scene controls:

- **Starting Soon**
- **BRB**
- **Game Clean** — game + frame + avatar.
- **BPSR** — game + frame + avatar + BPSR utility HUD.

When TikTok Vertical is active, horizontal and vertical scenes switch together.

## Avatar modes

### Full VTuber

Uses **VTube Studio + Spout2**. One-time setup in VTube Studio:

1. Load your Live2D model.
2. Enable **Spout2 output**.
3. Use **Color Picker Background**.
4. Enable **Transparent in capture**.
5. Keep VTube Studio open and use **Check my avatar** in StreamKit.

### Simple Talking Avatar

Uses the bundled FloodTuber-style PNG avatar, including `talk_a.png` and `talk_b.png` when available.

### No Avatar

Captures only the selected game and frame.

## Audio privacy

StreamKit avoids global desktop capture:

- The selected game is captured with its own audio.
- Public streams receive the selected game plus OBS Mic/Aux.
- Desktop/system loopback is disabled so unrelated apps, Discord friends, and notifications are not intentionally sent to public streams.
- Public-stream microphone audio receives OBS RNNoise suppression.
- Discord-only mode leaves OBS Mic/Aux muted.

## Frames and included components

StreamKit includes multiple selectable frame themes plus Starting Soon / BRB artwork. Frame choice is independent from avatar choice.

The tested portable bundle currently uses pinned versions of:

- OBS Studio
- Spout2 OBS plugin when Full VTuber needs it
- FloodTuber for the simple PNG-avatar fallback
- Aitum Stream Suite only when TikTok output is configured

VTube Studio is launched through its normal Steam installation and is not bundled.

Portable OBS and its plugins are treated as one tested bundle. Update them through a newer tested StreamKit release rather than letting the portable OBS copy self-update independently.

## Privacy and local credentials

Twitch/TikTok credentials, OAuth data, Aitum outputs, and the generated OBS WebSocket password stay in the extracted StreamKit folder and are excluded from source control.

Never commit or share an already-used OBS configuration containing `service.json`, stream keys, OAuth/login tokens, cookies, generated `user-data/`, or other account credentials.

## Game detection and repair

Open the game first. StreamKit scans visible application windows and remembers recently selected games; use **Find games** if the target is missing.

Advanced settings keep repair/technical actions out of the normal workflow:

- **Open avatar app**
- **Open streaming engine**
- **Open folder**
- **Fix setup**

Repair restores missing runtime/plugin/template files where possible while preserving local account/output data.

## Build

The launcher targets .NET 8 / Windows x64:

```text
dotnet restore src/BPSRStreamKit/BPSRStreamKit.csproj
dotnet publish src/BPSRStreamKit/BPSRStreamKit.csproj -c Release -r win-x64 --self-contained true
```

GitHub Actions validates/builds the Windows package.

## Licensing and disclaimer

This repository currently has **no project-wide `LICENSE` file**. Third-party licenses do not automatically license original StreamKit source/assets. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

StreamKit is an unofficial community utility. OBS Studio, VTube Studio, Spout2, Aitum Stream Suite, FloodTuber, Discord, Twitch, TikTok, Steam, and game names belong to their respective owners.
