# StreamKit — Portable Game + VTuber Streaming

StreamKit is a portable Windows launcher for getting a game, a VTuber avatar, Discord screen share, Twitch and TikTok into one simple workflow.

> **Quick start:** Extract the whole release ZIP, run `BPSRStreamKit.exe`, choose a stream mode, avatar, frame theme and running game, then press the main button.

## Stream modes

### Discord Share

This is the fastest mode and is designed to avoid double microphone audio.

1. Open the game.
2. Run StreamKit.
3. Choose **Discord Share**.
4. Leave **Full VTuber** selected if you want VTube Studio, or choose PNG Avatar / None.
5. Press **Open Discord Share**.
6. StreamKit opens VTube Studio when needed, prepares portable OBS and automatically opens **Windowed Projector (Program)**.
7. In Discord choose **Share Your Screen** and select the OBS Windowed Projector window with sound.
8. Keep your normal Discord microphone selected.

Discord share audio is intentionally **game audio only**. StreamKit mutes the OBS microphone in Discord-only mode so your friends do not hear your voice twice. Your normal Discord microphone remains the voice path.

### Discord + Twitch + TikTok

This mode uses one portable OBS instance with separate horizontal and vertical outputs:

- **Discord:** clean Windowed Projector (Program); OBS Virtual Camera also remains available as a fallback.
- **Twitch:** main 1920×1080 horizontal canvas.
- **TikTok:** Aitum 1080×1920 vertical canvas.

On first use StreamKit installs the pinned Aitum Stream Suite plugin, creates the vertical canvas and prepares matching horizontal/vertical layouts.

There is one unavoidable account step the first time:

1. In portable OBS **Settings → Stream**, connect Twitch.
2. In **Aitum Stream Suite → Settings → Outputs**, add a TikTok stream output.
3. Choose the **Vertical** canvas and enter the TikTok server/key provided to your account.
4. Close OBS when finished.

After that, **Start All Platforms** starts Twitch + TikTok together and opens the Discord share projector.

TikTok requires LIVE/stream-key access on the TikTok account. StreamKit cannot grant that access.

## Private audio routing

StreamKit deliberately avoids global desktop audio.

- The selected game is captured directly with its own application/game audio.
- Twitch/TikTok receive **selected-game audio + Mic/Aux only**.
- Desktop/system loopback audio is disabled, so Discord friends, notification sounds and unrelated applications are not intentionally sent to Twitch/TikTok.
- The OBS microphone gets the built-in **RNNoise** noise-suppression filter automatically.
- In Discord-only mode, the OBS mic is muted and not monitored; Discord uses your normal Discord mic instead.
- In All Platforms mode, the OBS mic goes to Twitch/TikTok but is not monitored into the Discord projector audio path.

The Discord projector obtains game sound from OBS's selected-game monitoring path. No default Display Capture or Desktop Audio source is required.

## Stream controls

Once a stream/share is prepared, StreamKit exposes simple controls so OBS can stay in the background:

- **Starting Soon** — switches the horizontal scene and TikTok vertical scene together.
- **Live** — returns to the game + VTuber layout.
- **BRB** — switches both layouts to the BRB screen.
- **Mute Mic / Unmute Mic** — controls the OBS mic used by Twitch/TikTok. In Discord-only mode this control is locked because the OBS mic intentionally stays out of the Discord share.
- **Stop Stream** — stops configured streams/Virtual Camera and closes StreamKit's portable OBS while leaving VTube Studio open.

The main button becomes **Reopen Discord Share** while active, so the clean projector can be reopened without rebuilding the stream.

## Avatar modes

### Full VTuber — recommended

Uses **VTube Studio + Spout2** as the main avatar and hides the old PNG/FloodTuber source.

StreamKit automatically installs the pinned portable Spout2 OBS plugin and creates the `VTube Studio Avatar` source with sender `VTubeStudioSpout` and **Premultiplied Alpha**. This avoids capturing the VTube Studio window/background/UI.

VTube Studio only needs a small one-time setup:

1. Choose your Live2D model and webcam/tracker in VTube Studio.
2. Turn on **Spout2 output** in VTube Studio.
3. Select **Color Picker Background**.
4. Enable **Transparent in capture**.
5. Leave the Spout sender name as `VTubeStudioSpout` for the normal single-instance setup.

StreamKit no longer trusts the instruction screen alone: after OBS opens it takes a small source screenshot and checks that VTube Studio is actually producing a useful transparent Spout frame. The one-time setup is only marked complete after that check passes.

Webcam face tracking, head motion, blinking, mouth movement, expressions and Live2D physics remain handled by VTube Studio. The webcam image itself is not added to the stream scene.

### PNG Avatar

Uses the existing lightweight FloodTuber avatar. This is kept as a fallback for PCs or users that do not want webcam tracking.

### None

Hides both avatar systems and streams only the game + selected frame.

## Frame themes

**Profile A — Sakura** uses the pink/purple Sakura frame and matching Starting Soon / BRB screens.

**Profile B — Chibi Doctor** uses the cyan/white medical frame and matching Starting Soon / BRB screens.

The frame theme is independent from the avatar mode. A full VTube Studio model can therefore use either frame.

## What StreamKit automates

StreamKit handles the repetitive pieces for you:

- portable OBS setup,
- Spout2 transparent VTube Studio capture and runtime verification,
- FloodTuber fallback setup,
- Aitum Stream Suite setup for multistream mode,
- local authenticated OBS WebSocket automation,
- selected-game video + audio capture,
- private audio hardening and RNNoise mic suppression,
- horizontal Twitch/Discord scene,
- vertical TikTok scenes including Starting Soon / Live / BRB,
- automatic Discord Program projector,
- optional OBS Virtual Camera in All Platforms mode,
- starting all configured Twitch/TikTok stream outputs together,
- StreamKit scene/mic controls,
- remembering your stream mode, avatar choice, frame theme and last game.

VTube Studio, Twitch and TikTok still require their own legitimate account/model access. StreamKit does not bypass platform requirements.

## OBS updates

StreamKit treats portable OBS and its plugins as a **tested pinned bundle**. To avoid OBS updating itself independently and potentially breaking FloodTuber, Spout2 or Aitum compatibility, StreamKit disables OBS's built-in update checks for its portable OBS instance.

This is intentional. Update the streaming engine by moving to a newer StreamKit release after its OBS/plugin combination has been tested, rather than accepting an OBS self-update prompt inside the portable copy.

This setting affects only the OBS copy inside the StreamKit folder. A separate normally-installed OBS installation is not changed.

## Privacy and local credentials

The default scene is designed around **selected game + microphone**, not full desktop capture.

Twitch/TikTok credentials, OAuth data, Aitum outputs and the local OBS WebSocket password stay inside the extracted StreamKit folder on the user's PC. They are not part of the repository or release ZIP.

Never commit or share:

- OBS `service.json`,
- Twitch/TikTok stream keys,
- OAuth/login tokens,
- browser cookies,
- your generated `user-data/` files,
- an already-used portable OBS config folder containing account credentials.

## First-run downloads

StreamKit currently pins:

- **OBS Studio 32.2.1** — official OBS GitHub release, SHA-256 verified.
- **Spout2 OBS plugin 1.12.0** — official Off-World-Live portable release, SHA-256 verified; installed when Full VTuber mode needs it.
- **FloodTuber 1.1.0** — official FloodTuber release; used for PNG fallback mode.
- **Aitum Stream Suite 1.2.1** — official Aitum GitHub release; installed only when All Platforms mode needs it.

VTube Studio is launched through its normal Steam app (**Steam App 1325860**) instead of being bundled with StreamKit.

## Game detection

Open the game first. StreamKit scans visible running application windows. If the game is not listed, press **Scan games**.

The historical internal project/scene names still contain `BPSR` for compatibility with existing installs. User-facing behavior is game-generic.

## Repair / advanced controls

Advanced controls provide:

- **VTube Studio** — open it through Steam,
- **Open OBS** — inspect/edit the portable OBS setup,
- **Open folder** — open the StreamKit folder,
- **Repair** — restore missing runtime/plugin/layout files and re-apply private audio hardening while preserving existing local account/output settings where possible.

## Building from source

The launcher targets .NET 8 on Windows:

```text
dotnet restore src/BPSRStreamKit/BPSRStreamKit.csproj
dotnet publish src/BPSRStreamKit/BPSRStreamKit.csproj -c Release -r win-x64 --self-contained true
```

## Project source licence status

This private repository currently does **not** contain a project-wide `LICENSE` file. Third-party licences do not automatically grant a licence to original StreamKit source/assets.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party notices.

## Disclaimer

This is an unofficial community streaming utility. OBS Studio, VTube Studio, Spout2, Aitum Stream Suite, FloodTuber, Discord, Twitch, TikTok, Steam and game names belong to their respective owners. StreamKit is not endorsed by those services unless explicitly stated otherwise.
