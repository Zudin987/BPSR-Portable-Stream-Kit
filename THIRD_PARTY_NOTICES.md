# Third-party notices

StreamKit downloads or integrates with third-party software to provide its portable streaming environment. Those components remain under their own licences/terms and are not relicensed by this project.

## OBS Studio

- Project: `obsproject/obs-studio`
- Version currently pinned by StreamKit: `32.2.1`
- Purpose: capture, compositing, encoding, virtual camera, recording, and streaming
- Upstream licence: GNU General Public License v2 or later, with additional notices for individual bundled components where applicable

StreamKit downloads the official Windows ZIP from the OBS GitHub Release and verifies it against a hard-coded SHA-256 before extraction.

## FloodTuber

- Project: `justflood/flood-tuber`
- Version currently pinned by StreamKit: `1.1.0`
- Purpose: optional lightweight PNG/avatar fallback inside OBS
- Upstream licence: GNU General Public License v2

StreamKit downloads the official portable ZIP from the FloodTuber GitHub Release and checks that the expected plugin files/layout are present before copying them into portable OBS.

The current StreamKit source does not contain a hard-coded authoritative SHA-256 for the FloodTuber v1.1.0 ZIP. The URL is version-pinned, but checksum verification remains a supply-chain hardening opportunity.

## Aitum Stream Suite

- Project: `Aitum/obs-aitum-stream-suite`
- Version currently pinned by StreamKit: `1.2.1`
- Purpose: optional multi-output support and a separate 1080×1920 vertical canvas for TikTok while Twitch/Discord use the horizontal canvas

Aitum is downloaded only when the **Discord + Twitch + TikTok** mode needs it. StreamKit uses the official version-pinned Windows ZIP and validates that the expected OBS plugin files are present before treating setup as complete.

The current StreamKit source does not yet contain a hard-coded authoritative SHA-256 for the Aitum 1.2.1 Windows ZIP. This is a known supply-chain hardening opportunity.

## VTube Studio

- Product: VTube Studio by DenchiSoft
- Steam App ID used by StreamKit: `1325860`
- Purpose: optional face-tracked Live2D avatar source

VTube Studio is **not bundled with StreamKit**. When Full VTuber mode is selected, StreamKit asks Steam to open the user's installed VTube Studio application and captures its window in OBS. The user is responsible for installing/configuring VTube Studio and for having the rights to use their chosen Live2D model.

VTube Studio remains subject to its own licence/terms and any model-specific licence terms.

## Microsoft .NET

The release is built as a self-contained .NET 8 Windows application. Microsoft .NET runtime/framework components and their transitive third-party components retain their applicable Microsoft/open-source licence and notice terms.

## Visual assets and service names

Repository visual assets under `Assets/` are separate from the third-party software licences. Only distribute assets for which you have the necessary rights.

Discord, Twitch, TikTok, Steam, VTube Studio, Aitum Stream Suite, Blue Protocol: Star Resonance, OBS Studio, FloodTuber, and other third-party names/trademarks belong to their respective owners.

## Project source licence status

This file documents third-party components only. The repository currently does not contain a project-wide `LICENSE` file for the original StreamKit source/assets, so no project-wide source licence should be inferred from the licences above.
