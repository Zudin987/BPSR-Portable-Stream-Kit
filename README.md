# StreamKit — Portable Game + VTuber Streaming

StreamKit is a portable Windows launcher for getting a game, a VTuber avatar, Discord camera output, Twitch and TikTok into one simple workflow.

> **Quick start:** Extract the whole release ZIP, run `BPSRStreamKit.exe`, choose a stream mode, avatar, frame theme and running game, then press the main button.

## Stream modes

### Discord Only

This is the fastest mode.

1. Open the game.
2. Run StreamKit.
3. Choose **Discord Only**.
4. Leave **Full VTuber** selected if you want VTube Studio, or choose PNG Avatar / None.
5. Press **Open Discord VTuber**.
6. StreamKit opens VTube Studio through Steam when needed, prepares portable OBS and starts **OBS Virtual Camera**.
7. In Discord choose **OBS Virtual Camera** as your camera.
8. Keep your normal microphone selected in Discord.

OBS Virtual Camera carries video, not the game audio. If you need Discord viewers to hear game audio too, use Discord screen share/audio separately.

### Discord + Twitch + TikTok

This mode uses one portable OBS instance with separate horizontal and vertical outputs:

- **Discord:** OBS Virtual Camera
- **Twitch:** main 1920×1080 horizontal canvas
- **TikTok:** Aitum 1080×1920 vertical canvas

On first use StreamKit installs the pinned Aitum Stream Suite plugin, creates the vertical canvas and prepares matching horizontal/vertical layouts.

There is one unavoidable account step the first time:

1. In normal OBS **Settings → Stream**, connect Twitch.
2. In **Aitum Stream Suite → Settings → Outputs**, add a TikTok stream output.
3. Choose the **Vertical** canvas and enter the TikTok server/key provided to your account.
4. Close OBS when finished.

After that, **Start All Platforms** can start Twitch + TikTok together while OBS Virtual Camera supplies Discord.

TikTok requires LIVE/stream-key access on the TikTok account. StreamKit cannot grant that access.

## Avatar modes

### Full VTuber — recommended

Uses **VTube Studio** as the main avatar and hides the old PNG/FloodTuber source.

- VTube Studio opens through Steam automatically.
- Webcam face tracking, head motion, blinking, mouth movement, expressions and Live2D physics are handled by VTube Studio.
- The webcam itself is not added to the stream scene.
- Use a bundled/sample VTube Studio model or any compatible Live2D model you have permission to use.

For a clean transparent OBS capture, enable VTube Studio's transparent/game-capture background option for the model output.

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
- FloodTuber fallback setup,
- Aitum Stream Suite setup for multistream mode,
- local authenticated OBS WebSocket automation,
- game-window capture,
- VTube Studio capture source,
- horizontal Twitch/Discord scene,
- vertical TikTok scene,
- OBS Virtual Camera,
- starting all configured Twitch/TikTok stream outputs together,
- remembering your stream mode, avatar choice, frame theme and last game.

VTube Studio, Twitch and TikTok still require their own legitimate account/model access. StreamKit does not bypass platform requirements.

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
- **Repair** — restore missing runtime/plugin/layout files while preserving existing local account/output settings where possible.

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

This is an unofficial community streaming utility. OBS Studio, VTube Studio, Aitum Stream Suite, FloodTuber, Discord, Twitch, TikTok, Steam and game names belong to their respective owners. StreamKit is not endorsed by those services unless explicitly stated otherwise.
