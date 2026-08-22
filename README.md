# StreamKit — Portable Game + VTuber Streaming

StreamKit is a portable Windows launcher that turns a game, VTuber avatar, Discord screen share and Twitch/TikTok streaming into one beginner-friendly workflow. The launcher owns the complicated OBS setup so normal use stays focused on **game → avatar → destination → start**.

> **Quick start:** Extract the complete release ZIP, run `BPSRStreamKit.exe`, open your game, choose an avatar/frame/destination and press the main button. After the first successful start, StreamKit remembers those choices and opens **Quick Launch** for returning users.

## Stream modes

### Discord only

Use this when you only want to share with friends.

1. Open the game.
2. Run StreamKit and select **Discord only**.
3. Choose Full VTuber, Simple Talking Avatar or No Avatar.
4. Press **Start Discord Share**.
5. In Discord, share the clean OBS Program Projector window with sound.

StreamKit sends the selected game audio to the projector while keeping the OBS microphone out of Discord-only mode. Your normal Discord microphone remains your voice path, so your voice is not doubled.

### Discord + Twitch (+ TikTok)

TikTok is **optional**.

- **Discord:** clean Program Projector window.
- **Twitch:** normal 1920×1080 horizontal output.
- **TikTok, when configured:** Aitum 1080×1920 Vertical output.

If TikTok is not configured, the main action is **Start Twitch + Discord** and Aitum is not installed/initialized just to satisfy an unused feature. You can stream Twitch + Discord normally and add TikTok later.

To connect Twitch, open **Advanced settings → Open streaming engine**, then use OBS **Settings → Stream → Twitch → Connect Account**.

To add TikTok later:

1. Select **Discord + Twitch (+ TikTok)**.
2. Click **Add TikTok later**.
3. Click **Open TikTok setup**. StreamKit installs/prepares Aitum only at this point if necessary.
4. In Aitum Outputs, add a **Stream** using the **Vertical** canvas.
5. Paste the TikTok RTMP server + stream key granted to your TikTok account.
6. Return to StreamKit and click **Check TikTok**.

If the TikTok check fails, Twitch and Discord remain usable. StreamKit cannot grant TikTok LIVE or stream-key access.

## Quick Launch

After a successful start, StreamKit remembers the selected game, avatar, frame style and destination.

Returning users see one compact card with the remembered setup and one primary start button. **Customize setup** returns to the full 3-step screen, and **Quick Launch** in the header switches back without restarting the app.

If the remembered game is not running, Quick Launch tells you which game to open rather than silently failing.

## Four scene controls

When OBS is prepared, StreamKit exposes four direct scene buttons:

- **Starting Soon** — intermission before gameplay.
- **BRB** — away screen.
- **Game Clean** — game + selected frame + avatar, without the BPSR utility HUD.
- **BPSR** — game + frame + avatar + DPS meter + dungeon-mechanic HUD.

Older StreamKit scene collections may still contain **Discord Share** and **Twitch Live**. These were legacy cloning/template scenes rather than useful user controls. v2.4 automatically migrates their layout into **Game Clean / BPSR** and removes the redundant scenes from existing OBS collections.

When a TikTok Vertical output is active, StreamKit switches the horizontal and vertical versions together. Without TikTok, the same four buttons control only the active horizontal share/stream and do not attempt an Aitum command.

The microphone button is available for public streaming. **Stop everything** stops public outputs/Virtual Camera, restores audio monitoring and asks portable OBS to close normally so it can save plugin/config state.

## Avatar modes

### Full VTuber — recommended

Uses **VTube Studio + Spout2**. StreamKit installs the pinned portable Spout2 OBS plugin when this mode requires it and verifies that VTube Studio is actually producing a useful transparent frame.

One-time VTube Studio setup:

1. Open your Live2D model.
2. Turn on **Spout2 output**.
3. Use **Color Picker Background**.
4. Enable **Transparent in capture**.
5. Keep VTube Studio open and use **Check my avatar** in StreamKit.

VTube Studio handles webcam tracking, blinking, mouth motion, expressions and physics. The webcam image itself is not added to StreamKit's scene.

StreamKit also waits for an already-starting VTube Studio process instead of repeatedly asking Steam to launch it.

### Simple Talking Avatar

Uses the bundled FloodTuber-based PNG avatar. The talking animation uses both `talk_a.png` and `talk_b.png` when available.

### No Avatar

Shows only the selected game and frame.

## Private audio routing

StreamKit intentionally avoids global desktop capture.

- The selected game is captured directly with its own audio.
- Public streams receive **selected game + OBS Mic/Aux**.
- Desktop/system loopback audio is disabled so Discord friends, notification sounds and unrelated applications are not intentionally sent to Twitch/TikTok.
- The public-stream microphone receives OBS RNNoise suppression automatically.
- Discord-only mode keeps OBS Mic/Aux muted so Discord continues using your normal Discord microphone.

## Reliability and safety behavior

StreamKit uses one canonical controller instead of the older version-specific compatibility layers.

- One canonical start/stop/scene workflow.
- One OBS WebSocket protocol implementation with a local authenticated connection.
- One stable per-process WebSocket credential even if the StreamKit folder becomes read-only.
- Atomic writes for critical scene/preferences/catalog/WebSocket configuration files.
- Periodic game/setup status scans are gated so refreshes do not overlap each other.
- Process handles used for detection are disposed after each scan.
- Normal OBS launches no longer secretly kill a previous portable OBS process.
- If StreamKit controls are slow after OBS has already opened, the working OBS session stays open and **Retry StreamKit controls** reconnects to the same process.
- Shutdown requests a graceful OBS close first and only force-terminates after a long timeout as a last resort.
- If OBS 32 shows its **Crash Detected** dialog for StreamKit's own portable OBS, StreamKit continues in **Normal Mode**, because Safe Mode disables the plugins/WebSockets StreamKit requires.

A separately installed OBS instance is not targeted by StreamKit's portable-process controls.

## UI behavior

The main window is designed around a minimum size of 1040×760 and uses wrapping/trimming instead of allowing dynamic text to overlap adjacent controls. Long game/theme/avatar labels are trimmed with tooltips, descriptive copy wraps, the four scene buttons stay in one equal-width row, and Mic/Stop controls use a separate lower row.

Waiting operations such as OBS/VTube startup use an indeterminate progress bar instead of incorrectly showing `100%` while work is still happening.

Errors are surfaced in the app with context-specific recovery actions such as **Find games**, **Avatar help**, **Open TikTok setup**, **Retry controls** or **Fix setup** instead of routing every failure to the same generic repair button.

## Frame themes

v2.4 includes **nine selectable frame styles**:

- **Sakura** — soft pink / purple minimal frame.
- **Chibi Doctor** — cyan / white medical frame.
- **Neon Tech** — cyan + violet futuristic glow.
- **Black Gold** — restrained black / gold premium styling.
- **Crimson Demon** — angular red / black infernal styling.
- **Ice Crystal** — bright frozen-blue crystal styling.
- **Forest Mystic** — emerald fantasy / nature styling.
- **Cyber Orange** — orange / charcoal esports-tech styling.
- **Moonlight Silver** — silver / navy night styling.

Each style includes a horizontal frame, TikTok vertical frame, Starting Soon screen and BRB screen. The seven additional styles are generated locally the first time you select them and then cached under `user-data/frame-themes`, keeping the downloadable ZIP much smaller. First-time generation may take a brief moment; StreamKit shows its normal busy state while it prepares the files.

Frame style is independent from avatar mode: choosing a new frame never replaces your VTuber model or PNG avatar. Existing Sakura and Chibi Doctor installations remain compatible.

## What StreamKit automates

StreamKit currently handles:

- portable OBS setup,
- selected-game capture + game-only audio,
- local OBS WebSocket control,
- private audio hardening,
- RNNoise microphone cleanup,
- VTube Studio Spout capture and validation,
- FloodTuber fallback avatar,
- horizontal scenes for Discord/Twitch,
- optional vertical TikTok scenes,
- automatic Discord Program Projector,
- optional Virtual Camera in public mode,
- Starting Soon / BRB / Game Clean / BPSR scene control,
- graceful public-output/OBS shutdown,
- saved game/avatar/frame/destination preferences,
- returning-user Quick Launch.

## First-run downloads

The tested bundle currently pins:

- **OBS Studio 32.2.1** — official OBS release, SHA-256 verified.
- **Spout2 OBS plugin 1.12.0** — official Off-World-Live portable release, installed only when Full VTuber needs it.
- **FloodTuber 1.1.0** — PNG-avatar fallback.
- **Aitum Stream Suite 1.2.1** — installed/prepared only when TikTok setup/output requires it.

VTube Studio is launched through its normal Steam app (**Steam App 1325860**) and is not bundled with StreamKit.

## OBS updates

Portable OBS and its plugins are treated as one tested bundle. OBS self-update checks are disabled for StreamKit's portable copy so OBS cannot update independently and break FloodTuber, Spout2 or Aitum compatibility.

Update the streaming engine by moving to a newer tested StreamKit release instead. A separate normally installed OBS copy is not modified.

## Privacy and local credentials

Twitch/TikTok credentials, OAuth data, Aitum outputs and the generated OBS WebSocket password stay in the extracted StreamKit folder. They are excluded from the repository/release source by `.gitignore` rules.

Never commit or share an already-used portable OBS configuration containing:

- `service.json`,
- Twitch/TikTok stream keys,
- OAuth/login tokens,
- cookies,
- generated `user-data/`,
- other local account credentials.

## Game detection

Open the game first. StreamKit scans visible application windows and remembers up to a small set of recently selected games. If the game is missing, use **Find games**.

Historical internal project/scene names still contain `BPSR` for compatibility with existing StreamKit installs; the launcher itself can capture other normal windowed games.

## Advanced / repair

Advanced settings intentionally keep technical controls out of the normal workflow:

- **Open avatar app**
- **Open streaming engine**
- **Open folder**
- **Fix setup**

Repair restores missing runtime/plugin/template files and re-applies StreamKit's safe configuration where possible while preserving local account/output data.

## Building from source

The launcher targets .NET 8 / Windows x64:

```text
dotnet restore src/BPSRStreamKit/BPSRStreamKit.csproj
dotnet publish src/BPSRStreamKit/BPSRStreamKit.csproj -c Release -r win-x64 --self-contained true
```

GitHub Actions also builds the Windows package for pull requests and publishes release branches named `release/v*`.

## Project source licence status

This repository currently does **not** contain a project-wide `LICENSE` file. Third-party licences do not automatically grant a licence to original StreamKit source/assets.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party notices.

## Disclaimer

This is an unofficial community streaming utility. OBS Studio, VTube Studio, Spout2, Aitum Stream Suite, FloodTuber, Discord, Twitch, TikTok, Steam and game names belong to their respective owners. StreamKit is not endorsed by those services unless explicitly stated otherwise.
