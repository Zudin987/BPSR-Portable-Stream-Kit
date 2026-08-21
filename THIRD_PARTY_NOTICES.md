# Third-party notices

StreamKit downloads or integrates with third-party software to provide its portable streaming environment. Those components remain under their own licences/terms and are not relicensed by this project.

## OBS Studio

- Project: `obsproject/obs-studio`
- Version currently pinned by StreamKit: `32.2.1`
- Purpose: capture, compositing, encoding, virtual camera, recording, and streaming
- Upstream licence: GNU General Public License v2 or later, with additional notices for individual bundled components where applicable

StreamKit downloads the official Windows ZIP from the OBS GitHub Release and verifies it against a hard-coded SHA-256 before extraction. The built-in updater is disabled for StreamKit's portable OBS copy so OBS and its plugins stay on the tested pinned combination; this does not alter a separate system-installed OBS copy.

## Spout2 OBS plugin

- Project: `Off-World-Live/obs-spout2-plugin`
- Version currently pinned by StreamKit: `1.12.0`
- Purpose: transparent VTube Studio video transfer into portable OBS without capturing the VTube Studio window/background/UI
- Upstream licence: GNU General Public License v2

When **Full VTuber** mode is used, StreamKit downloads the official portable Windows x64 ZIP and verifies it against the published SHA-256 before copying the portable OBS plugin layout into StreamKit's OBS folder.

Pinned portable archive SHA-256:

`6c5a31d6f30a44277b1955d4f85a1da1c0baa97a13075594d2bbca475104ee8a`

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

VTube Studio is **not bundled with StreamKit**. When Full VTuber mode is selected, StreamKit asks Steam to open the user's installed VTube Studio application. VTube Studio sends the avatar through Spout2 to the prepared OBS source. The user is responsible for installing/configuring VTube Studio and for having the rights to use their chosen Live2D model.

VTube Studio remains subject to its own licence/terms and any model-specific licence terms.

## Microsoft .NET

The release is built as a self-contained .NET 8 Windows application. Microsoft .NET runtime/framework components and their transitive third-party components retain their applicable Microsoft/open-source licence and notice terms.

## Visual assets and service names

Repository visual assets under `Assets/` are separate from the third-party software licences. Only distribute assets for which you have the necessary rights.

Discord, Twitch, TikTok, Steam, VTube Studio, Spout2, Aitum Stream Suite, Blue Protocol: Star Resonance, OBS Studio, FloodTuber, and other third-party names/trademarks belong to their respective owners.

## Project source licence status

This file documents third-party components only. The repository currently does not contain a project-wide `LICENSE` file for the original StreamKit source/assets, so no project-wide source licence should be inferred from the licences above.
