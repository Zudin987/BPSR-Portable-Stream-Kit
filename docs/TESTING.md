# Testing checklist

Before tagging a release:

- Windows x64 build completes in GitHub Actions.
- First launch can download and verify portable OBS.
- FloodTuber plugin loads without a missing-module warning.
- A normal running game appears in the game list without requiring any title-specific setup.
- Discord opens the prepared OBS layout and the UI clearly tells the user to share the OBS Projector/preview window in Discord.
- Twitch opens the horizontal streaming layout and preserves local account/stream settings.
- TikTok opens the vertical streaming layout and preserves local account/stream settings.
- Profile A and Profile B both load the correct avatar, frame, Starting Soon and BRB assets.
- Starting Soon screens contain only the text `STARTING SOON`.
- BRB screens contain only the text `BE RIGHT BACK`.
- Both profile frames hug the canvas edges and retain their themed micro-decorations.
- No default Display Capture source exists.
- Existing OBS profile/service files survive Repair.
- Avatar and frame positions remain stable after relaunch.
- Optional title-specific integrations never prevent the generic clean-game layout from working.
