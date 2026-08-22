using System.Text.Json;
using System.Text.Json.Nodes;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit.Services;

/// <summary>
/// Keeps StreamKit's portable OBS audio deliberately narrow: selected-game audio plus Mic/Aux only.
/// It also repairs portable scene asset paths after the StreamKit ZIP is moved/extracted elsewhere.
/// </summary>
public static class AudioPrivacyService
{
    public static void HardenPortableObsConfig()
    {
        var configRoot = AppPaths.ObsConfigRoot();
        if (configRoot is null) return;
        var sceneRoot = Path.Combine(configRoot, "basic", "scenes");
        if (!Directory.Exists(sceneRoot)) return;

        foreach (var name in new[] { "BPSR_Horizontal.json", "BPSR_TikTok_Vertical.json" })
        {
            var file = Path.Combine(sceneRoot, name);
            if (!File.Exists(file)) continue;
            try { HardenSceneCollection(file); }
            catch { /* A malformed optional source must not destroy an otherwise usable collection. */ }
        }
    }

    private static void HardenSceneCollection(string file)
    {
        var root = JsonNode.Parse(File.ReadAllText(file))?.AsObject();
        if (root is null) return;

        // Desktop audio is global. StreamKit uses direct selected-game audio instead, so deleting
        // these entries prevents Discord/system notifications from leaking to public outputs.
        for (var i = 1; i <= 4; i++) root.Remove($"DesktopAudioDevice{i}");
        for (var i = 2; i <= 4; i++) root.Remove($"AuxAudioDevice{i}");

        if (root["AuxAudioDevice1"] is JsonObject mic)
        {
            mic["monitoring_type"] = 0;
            mic["enabled"] = true;
        }

        var sources = root["sources"]?.AsArray();
        if (sources is not null)
        {
            RepairPortableAssetPaths(sources, file);

            foreach (var source in sources.OfType<JsonObject>())
            {
                var id = source["id"]?.GetValue<string>() ?? string.Empty;
                var versionedId = source["versioned_id"]?.GetValue<string>() ?? string.Empty;
                var name = source["name"]?.GetValue<string>() ?? string.Empty;
                var kind = string.IsNullOrWhiteSpace(id) ? versionedId : id;

                if (kind.Contains("wasapi_output_capture", StringComparison.OrdinalIgnoreCase)
                    || kind.Contains("wasapi_process_output_capture", StringComparison.OrdinalIgnoreCase)
                    || (name.Contains("Discord", StringComparison.OrdinalIgnoreCase)
                        && kind.Contains("audio", StringComparison.OrdinalIgnoreCase)))
                {
                    source["muted"] = true;
                    source["mixers"] = 0;
                    source["enabled"] = false;
                }

                if (name.Equals("Selected Game + Audio", StringComparison.Ordinal))
                {
                    var settings = source["settings"]?.AsObject() ?? new JsonObject();
                    source["settings"] = settings;
                    settings["capture_audio"] = true;
                    source["muted"] = false;
                    source["enabled"] = true;
                    source["mixers"] = 255;
                }
            }
        }

        AtomicFile.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static void RepairPortableAssetPaths(JsonArray sources, string sceneFile)
    {
        static string ObsPath(string path) => path.Replace('\\', '/');
        static JsonObject? FindSource(JsonArray array, string name) => array.OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), name, StringComparison.Ordinal));

        var isVerticalCollection = Path.GetFileName(sceneFile).Contains("TikTok", StringComparison.OrdinalIgnoreCase);
        var normalFrameName = isVerticalCollection ? "TikTok Minimal Frame" : "Minimal Stream Frame";
        var existingFrame = FindSource(sources, normalFrameName)?["settings"]?["file"]?.GetValue<string>() ?? string.Empty;
        var doctorTheme = existingFrame.Contains("Profile_B_Doctor", StringComparison.OrdinalIgnoreCase);

        string avatarDirectory;
        string horizontalFrame;
        string verticalFrame;
        string horizontalStarting;
        string verticalStarting;
        string horizontalBrb;
        string verticalBrb;

        if (doctorTheme)
        {
            var themeRoot = Path.Combine(AppPaths.AssetsDirectory, "Themes", "Profile_B_Doctor");
            avatarDirectory = Path.Combine(themeRoot, "Avatar");
            horizontalFrame = Path.Combine(themeRoot, "Frames", "Discord_1080p.png");
            verticalFrame = Path.Combine(themeRoot, "Frames", "TikTok_1080x1920.png");
            horizontalStarting = Path.Combine(themeRoot, "Screens", "Starting_1080p.jpg");
            verticalStarting = Path.Combine(themeRoot, "Screens", "Starting_TikTok_1080x1920.jpg");
            horizontalBrb = Path.Combine(themeRoot, "Screens", "BRB_1080p.jpg");
            verticalBrb = Path.Combine(themeRoot, "Screens", "BRB_TikTok_1080x1920.jpg");
        }
        else
        {
            avatarDirectory = Path.Combine(AppPaths.AssetsDirectory, "MyAvatar");
            horizontalFrame = Path.Combine(AppPaths.AssetsDirectory, "Frames", "01_Minimal_Thin_1080p.png");
            verticalFrame = Path.Combine(AppPaths.AssetsDirectory, "Frames", "05_TikTok_Minimal_1080x1920.png");
            horizontalStarting = Path.Combine(AppPaths.AssetsDirectory, "Screens", "Starting_1080p.png");
            verticalStarting = Path.Combine(AppPaths.AssetsDirectory, "Screens", "Starting_TikTok_1080x1920.png");
            horizontalBrb = Path.Combine(AppPaths.AssetsDirectory, "Screens", "BRB_1080p.png");
            verticalBrb = Path.Combine(AppPaths.AssetsDirectory, "Screens", "BRB_TikTok_1080x1920.png");
        }

        void SetImage(string name, string path)
        {
            var source = FindSource(sources, name);
            if (source is null) return;
            var settings = source["settings"]?.AsObject() ?? new JsonObject();
            source["settings"] = settings;
            settings["file"] = ObsPath(path);
        }

        void SetAvatar(string name)
        {
            var avatar = FindSource(sources, name);
            if (avatar is null) return;
            var settings = avatar["settings"]?.AsObject() ?? new JsonObject();
            avatar["settings"] = settings;
            var talkA = Path.Combine(avatarDirectory, "talk_a.png");
            var talkB = Path.Combine(avatarDirectory, "talk_b.png");
            settings["path_idle"] = ObsPath(Path.Combine(avatarDirectory, "idle.png"));
            settings["path_blink"] = ObsPath(Path.Combine(avatarDirectory, "blink.png"));
            settings["path_action"] = ObsPath(Path.Combine(avatarDirectory, "action.png"));
            settings["path_talk_1"] = ObsPath(talkA);
            settings["path_talk_2"] = ObsPath(File.Exists(talkB) ? talkB : talkA);
            settings["path_talk_3"] = ObsPath(talkA);
            settings["custom_avatars_path"] = ObsPath(avatarDirectory);
        }

        SetImage(normalFrameName, isVerticalCollection ? verticalFrame : horizontalFrame);
        SetImage("Starting Screen", isVerticalCollection ? verticalStarting : horizontalStarting);
        SetImage("BRB Screen", isVerticalCollection ? verticalBrb : horizontalBrb);
        SetAvatar("FloodTuber Avatar");

        // Aitum's imported vertical sources live inside the horizontal collection after first setup.
        SetImage("Vertical - Stream Frame", verticalFrame);
        SetImage("Vertical - Starting Screen", verticalStarting);
        SetImage("Vertical - BRB Screen", verticalBrb);
        SetAvatar("Vertical - PNG Avatar");
    }
}
