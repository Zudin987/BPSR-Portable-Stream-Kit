# StreamKit — Portable Game Streaming Kit

StreamKit is a portable Windows launcher for **game streaming**. It prepares an isolated OBS setup for Discord, Twitch, and TikTok without requiring a normal system-wide OBS installation.

> **Quick start:** Extract the whole release ZIP, run `BPSRStreamKit.exe`, choose a destination, game, and visual profile, then press the main action button. On first use StreamKit downloads its pinned portable OBS/FloodTuber components and prepares the scene automatically.

## Core objective

StreamKit is built for **games in general**, not for one specific title. A detected game gets a clean capture layout with:

- the selected game window,
- your microphone,
- your chosen frame/theme,
- your FloodTuber avatar,
- no default desktop/display capture.

Some games can have optional extra integrations, but those are enhancements rather than the product's core purpose.

## Destinations

### Discord

1. Select **Discord** in StreamKit.
2. Choose your game and profile.
3. Press **Open Discord Stream**.
4. StreamKit opens the prepared OBS scene/projector.
5. In Discord, start screen sharing and choose the **OBS Projector / OBS preview window**.

Discord performs the actual broadcast; StreamKit prepares the clean video/audio source for it.

### Twitch

1. Select **Twitch**.
2. Choose your game and profile.
3. Press **Open Twitch Stream**.
4. Connect your Twitch account or stream key inside the local portable OBS environment if this is your first time.
5. Press **Start Streaming** in OBS.

### TikTok

1. Select **TikTok**.
2. Choose your game and profile.
3. Press **Open TikTok Stream**.
4. Complete the TikTok streaming method available to your account inside portable OBS.
5. Start streaming from OBS.

Twitch/TikTok credentials remain in the user's local portable OBS configuration and are never included in this repository or release assets.

## Visual profiles

### Profile A — Sakura Catgirl

- Sakura/pink FloodTuber avatar set
- Thin edge-hugging frame with small petal/sparkle details
- Matching 16:9 and 9:16 layouts
- Sakura-themed **STARTING SOON** and **BE RIGHT BACK** screens

### Profile B — Chibi Doctor

- Chibi doctor FloodTuber avatar set
- Thin cyan/white medical frame with small cross/ECG details
- Matching 16:9 and 9:16 layouts
- Medical-themed **STARTING SOON** and **BE RIGHT BACK** screens

The selected profile is remembered locally and applied automatically.

## Quick Start

1. Download the latest release ZIP and **extract the whole folder**.
2. Run `BPSRStreamKit.exe` and allow the Administrator prompt.
3. Choose **Discord / Twitch / TikTok**, select the game you want to capture, choose Profile A or B, then press the main action button.

Do not move only the EXE out of the extracted folder. The packaged assets, templates, and portable runtime files belong together.

## Game detection

StreamKit scans running application windows and builds a game list automatically. If a game is not listed yet, open the game and press **Scan games**.

A small number of titles may have optional custom layouts or integrations. Those do not change the default behavior: StreamKit should remain useful as a generic game-streaming tool.

## Privacy / capture model

The default scene is designed around **selected game + microphone**, not full-display capture.

The repository must never contain:

- OBS `service.json`,
- Twitch/TikTok stream keys,
- OAuth/login tokens,
- browser cookies,
- exported streaming profiles containing credentials.

Always review the active OBS scene before going live, especially after manually editing templates or sources.

## Why Administrator permission appears

The launcher currently requests Administrator permission for game/window detection and portable-OBS integration. This behavior is explicit in the Windows manifest.

## First-run downloads

StreamKit currently pins:

- **OBS Studio 32.2.1** — downloaded from the official OBS GitHub release and verified against a hard-coded SHA-256.
- **FloodTuber 1.1.0** — downloaded from the official FloodTuber GitHub release and checked for the expected plugin layout.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party notices.

## Repair / advanced controls

Advanced tools can open OBS, open the portable folder, rescan games, and repair the prepared configuration. Repair is intended to preserve local streaming-account settings rather than replacing them.

## Visual assets

Release assets live under:

```text
Assets/Frames/
Assets/Screens/
Assets/MyAvatar/                       # Profile A
Assets/Themes/Profile_B_Doctor/       # Profile B
```

See [Assets/README.md](Assets/README.md).

Only add visual assets you own or are permitted to distribute/use.

## Building from source

The project targets .NET 8 on Windows.

```text
dotnet restore src/BPSRStreamKit/BPSRStreamKit.csproj
dotnet publish src/BPSRStreamKit/BPSRStreamKit.csproj -c Release -r win-x64 --self-contained true
```

The historical repository/project namespace still contains `BPSR` for compatibility, but the product direction and user experience are game-generic.

## Project source licence status

This private repository currently does **not** contain a project-wide `LICENSE` file. Third-party licences for OBS, FloodTuber, .NET/runtime components, and other dependencies do not automatically grant a licence to the original StreamKit source/assets.

## Disclaimer

This is an unofficial community streaming utility. Game titles, OBS Studio, FloodTuber, Discord, Twitch, TikTok, and other names/services belong to their respective owners. StreamKit is not endorsed by those services unless explicitly stated by them.
