using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit.Services;

/// <summary>
/// Keeps StreamKit's portable OBS audio deliberately narrow: selected-game audio plus Mic/Aux only.
/// This prevents desktop/system audio (including Discord voice chat) from accidentally reaching
/// Twitch/TikTok, while Discord screen-share audio is supplied from the selected game via monitoring.
/// </summary>
public static class AudioPrivacyService
{
    public static void HardenPortableObsConfig()
    {
        var configRoot = AppPaths.ObsConfigRoot();
        if (configRoot is null) return;
        var sceneRoot = Path.Combine(configRoot, "basic", "scenes");
        if (!Directory.Exists(sceneRoot)) return;

        foreach (var file in Directory.EnumerateFiles(sceneRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            try { HardenSceneCollection(file); }
            catch { /* Never destroy an otherwise usable collection because of one malformed optional source. */ }
        }
    }

    private static void HardenSceneCollection(string file)
    {
        var root = JsonNode.Parse(File.ReadAllText(file))?.AsObject();
        if (root is null) return;

        // OBS desktop audio is global. StreamKit never needs it because the chosen game is captured
        // directly with application/game audio. Removing these keys is the strongest protection
        // against Discord friends or notification sounds leaking into Twitch/TikTok.
        for (var i = 1; i <= 4; i++) root.Remove($"DesktopAudioDevice{i}");

        if (root["AuxAudioDevice1"] is JsonObject mic)
        {
            mic["monitoring_type"] = 0; // mic must never be played back into the Discord projector audio path
            mic["enabled"] = true;
        }

        var sources = root["sources"]?.AsArray();
        if (sources is not null)
        {
            foreach (var source in sources.OfType<JsonObject>())
            {
                var id = source["id"]?.GetValue<string>() ?? string.Empty;
                var versionedId = source["versioned_id"]?.GetValue<string>() ?? string.Empty;
                var name = source["name"]?.GetValue<string>() ?? string.Empty;
                var kind = string.IsNullOrWhiteSpace(id) ? versionedId : id;

                // Disable any manually-added output/process loopback source. The selected game uses
                // game_capture capture_audio=true, so these sources are unnecessary and are the usual
                // route by which Discord voice chat or system sounds leak into a public stream.
                if (kind.Contains("wasapi_output_capture", StringComparison.OrdinalIgnoreCase) ||
                    kind.Contains("wasapi_process_output_capture", StringComparison.OrdinalIgnoreCase) ||
                    (name.Contains("Discord", StringComparison.OrdinalIgnoreCase) &&
                     kind.Contains("audio", StringComparison.OrdinalIgnoreCase)))
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

        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }
}