# Third-party notices

BPSR Portable Stream Kit downloads/uses third-party software to provide its portable streaming environment. Those components remain under their own licences and are not relicensed by this project.

## OBS Studio

- Project: `obsproject/obs-studio`
- Version currently pinned by Stream Kit: `32.2.1`
- Purpose: capture, compositing, encoding, recording, and streaming
- Upstream licence: GNU General Public License v2 or later, with additional notices for individual bundled components where applicable

Stream Kit downloads the official Windows ZIP from the OBS GitHub Release and verifies it against the hard-coded SHA-256 before extraction.

## FloodTuber

- Project: `justflood/flood-tuber`
- Version currently pinned by Stream Kit: `1.1.0`
- Purpose: reactive avatar / PNGTuber OBS plugin
- Upstream licence: GNU General Public License v2

Stream Kit downloads the official portable ZIP from the FloodTuber GitHub Release and checks that the expected plugin files/layout are present before copying them into portable OBS.

The current Stream Kit source does not contain a hard-coded authoritative SHA-256 for the FloodTuber v1.1.0 ZIP. The URL is version-pinned, but checksum verification remains a known supply-chain hardening opportunity.

## Microsoft .NET

The release is built as a self-contained .NET 8 Windows application. Microsoft .NET runtime/framework components and their transitive third-party components retain their applicable Microsoft/open-source licence and notice terms.

## Visual assets and service names

Repository visual assets under `Assets/` are separate from the third-party OBS/FloodTuber software licences. Only distribute assets for which you have the necessary rights.

Discord, Twitch, TikTok, Blue Protocol: Star Resonance, OBS Studio, FloodTuber, and other third-party names/trademarks belong to their respective owners.

## Project source licence status

This file documents third-party components only. The repository currently does not contain a project-wide `LICENSE` file for the original BPSR Portable Stream Kit source/assets, so no project-wide source licence should be inferred from the licences above.
