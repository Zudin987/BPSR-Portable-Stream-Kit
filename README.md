# BPSR Portable Stream Kit

A portable Windows streaming launcher that prepares a private OBS setup for **BPSR and other supported games** without requiring a normal system-wide OBS installation.

> **TL;DR:** Extract the release ZIP, run `BPSRStreamKit.exe`, choose your destination + game, then press the main Start button. On first use, Stream Kit downloads its pinned portable OBS/FloodTuber components and prepares the private scene for you.

## What this app does

- Provides one launcher for **Discord**, **Twitch**, and **TikTok** streaming setups.
- Detects supported games and prepares the matching OBS scene/layout.
- Keeps the capture focused on the selected game + microphone instead of a general desktop capture.
- Includes the tested BPSR stream frames, starting/BRB screens, templates, and avatar images in the release ZIP.
- Uses portable OBS configuration rather than modifying a normal installed OBS profile.
- Downloads the pinned OBS/FloodTuber runtime components on first setup instead of committing those binaries to this repository.

## Quick Start — 1, 2, 3

1. Download the release ZIP and **extract the whole folder**.
2. Run `BPSRStreamKit.exe`, allow the Administrator prompt, then choose **Discord / Twitch / TikTok** and your game.
3. Press the main **Start** action. Complete any streaming-service login/key setup inside the local portable OBS environment when required.

Do not move only the EXE out of the extracted folder; the packaged assets/templates are part of the release.

## Why Administrator permission appears

The Windows launcher manifest requests Administrator permission. This is intentional for the current game/window/portable-OBS integration and is not hidden from the user.

## First-run downloads

Stream Kit currently pins:

- **OBS Studio 32.2.1** — downloaded from the official OBS GitHub Release; the ZIP is verified against a hard-coded SHA-256 before extraction.
- **FloodTuber 1.1.0** — downloaded from the official FloodTuber GitHub Release and checked for the expected plugin layout before installation.

### Supply-chain note

OBS already has explicit SHA-256 verification in the launcher.

FloodTuber is version-pinned to the official `v1.1.0` release URL, but the current launcher does **not** have a hard-coded authoritative SHA-256 for the FloodTuber ZIP. Do not replace the URL with mirrors or unknown binaries. This remains a known hardening opportunity if an authoritative checksum is obtained later.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Privacy / capture model

The prepared templates are designed around **game capture + your microphone**, not general display capture.

The repository must never contain:

- OBS `service.json`,
- Twitch/TikTok stream keys,
- OAuth/login tokens,
- browser cookies,
- exported streaming profiles containing credentials.

Streaming-service credentials stay in the user's local portable OBS/config environment.

Always review the active OBS scene before going live, especially after manually editing templates or sources.

## Visual assets

The release intentionally contains the tested files under:

```text
Assets/Frames/
Assets/Screens/
Assets/MyAvatar/
```

See [Assets/README.md](Assets/README.md) for the expected layout.

Only add visual assets you own or are permitted to distribute/use.

## Repair / advanced controls

The launcher can reopen OBS, open its portable folder, rescan games, and repair the prepared configuration. Repair is designed to preserve local streaming account settings rather than replacing them with repository credentials.

## Building from source

The project targets .NET 8 on Windows.

```text
dotnet restore src/BPSRStreamKit/BPSRStreamKit.csproj
dotnet publish src/BPSRStreamKit/BPSRStreamKit.csproj -c Release -r win-x64 --self-contained true
```

GitHub Actions packages the self-contained launcher together with the required templates/assets. Generated ZIP/EXE/DLL output should remain in Actions/Releases rather than source control.

## Project source licence status

This private repository currently does **not** contain a project-wide `LICENSE` file. Third-party licences for OBS, FloodTuber, .NET/runtime components, and other dependencies do not automatically grant a licence to the original Stream Kit source/assets.

## Disclaimer

This is an unofficial community streaming utility. BPSR, OBS Studio, FloodTuber, Discord, Twitch, TikTok, and other names/services belong to their respective owners. The project is not endorsed by those services unless explicitly stated by them.
