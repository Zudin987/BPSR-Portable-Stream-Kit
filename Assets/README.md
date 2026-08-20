# Stream visual assets

The Windows release is designed to include the tested visual pack from the original portable setup.

Expected files:

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

These are binary image assets and are intentionally separate from account credentials. Never add OBS `service.json`, Twitch/TikTok stream keys, cookies, or login tokens to this folder or repository.
