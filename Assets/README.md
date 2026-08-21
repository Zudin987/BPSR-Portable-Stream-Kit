# Stream visual assets

StreamKit ships two complete visual profiles for generic game streaming. The release build regenerates the active frame and intermission art so horizontal and vertical layouts stay consistent.

Expected Profile A files:

```text
Assets/
├─ MyAvatar/
│  ├─ idle.png
│  ├─ blink.png
│  ├─ action.png
│  ├─ talk_a.png
│  └─ talk_b.png
├─ Frames/
│  ├─ 01_Minimal_Thin_1080p.png
│  └─ 05_TikTok_Minimal_1080x1920.png
└─ Screens/
   ├─ Starting_1080p.png
   ├─ BRB_1080p.png
   ├─ Starting_TikTok_1080x1920.png
   └─ BRB_TikTok_1080x1920.png
```

Profile B lives under `Assets/Themes/Profile_B_Doctor/` with its own Avatar, Frames and Screens folders.

Design rules for v0.3.4:

- Profile A frames sit very close to the canvas edge and use tiny sakura/petal/sparkle accents.
- Profile B frames sit very close to the canvas edge and use tiny medical cross/ECG accents.
- Starting Soon screens display only `STARTING SOON` as text.
- BRB screens display only `BE RIGHT BACK` as text.
- No game title is baked into either profile's intermission art.

These are visual assets only. Never add OBS `service.json`, Twitch/TikTok stream keys, cookies, login tokens, or local account data to this folder or repository.
