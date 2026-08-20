# Testing checklist

Before tagging a release:

- Windows x64 build completes in GitHub Actions.
- First launch can download and verify portable OBS.
- FloodTuber plugin loads without a missing-module warning.
- `Start Discord` opens `Profile: Discord Share` and `Scenes: BPSR Horizontal`.
- BPSR game capture targets `StarSEA.exe`.
- Resonance Logs targets `resonance-logs-cn.exe` for both the DPS meter and Dungeon Mech HUD.
- No default Display Capture source exists.
- Existing OBS profile/service files survive Repair Setup.
- Avatar and frame positions remain unchanged after relaunch.
