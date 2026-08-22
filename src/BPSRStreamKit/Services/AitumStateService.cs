using System.Text.Json.Nodes;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit.Services;

public sealed record AitumState(bool PluginReady, string? VerticalCanvasUuid, bool TikTokOutputConfigured);

public sealed class AitumStateService
{
    public AitumState Read()
    {
        var obsRoot = AppPaths.FindObsRoot();
        var pluginReady = false;
        if (obsRoot is not null)
        {
            try
            {
                var pluginRoot = Path.Combine(obsRoot, "obs-plugins");
                pluginReady = Directory.Exists(pluginRoot)
                              && Directory.EnumerateFiles(pluginRoot, "*.dll", SearchOption.AllDirectories)
                                  .Any(x => Path.GetFileName(x).Contains("aitum", StringComparison.OrdinalIgnoreCase));
            }
            catch { }
        }

        var configRoot = AppPaths.ObsConfigRoot();
        if (configRoot is null) return new AitumState(pluginReady, null, false);
        var path = Path.Combine(configRoot, "basic", "profiles", "Twitch 1080p", "aitum.json");
        if (!File.Exists(path)) return new AitumState(pluginReady, null, false);

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            if (root is null) return new AitumState(pluginReady, null, false);

            var vertical = root["canvas"]?.AsArray()?.OfType<JsonObject>()
                .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), "Vertical", StringComparison.OrdinalIgnoreCase));
            var verticalUuid = vertical?["uuid"]?.GetValue<string>();

            var outputs = root["outputs"]?.AsArray()?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
            var streamOutputs = outputs.Where(IsConfiguredStream).ToList();
            var hasTikTok = streamOutputs.Any(output => LooksLikeTikTokOrVertical(output, verticalUuid));

            // Older Aitum versions do not always persist a recognizable service/canvas field.
            // If there is exactly one configured extra stream, StreamKit's setup flow created it
            // specifically for the Vertical TikTok output, so accept it as the compatibility fallback.
            if (!hasTikTok && streamOutputs.Count == 1) hasTikTok = true;

            return new AitumState(pluginReady, string.IsNullOrWhiteSpace(verticalUuid) ? null : verticalUuid, hasTikTok);
        }
        catch
        {
            return new AitumState(pluginReady, null, false);
        }
    }

    private static bool IsConfiguredStream(JsonObject output)
    {
        var type = ReadString(output, "type");
        if (!string.IsNullOrWhiteSpace(type) && !type.Equals("stream", StringComparison.OrdinalIgnoreCase)) return false;
        var server = ReadString(output, "stream_server", "server", "url", "rtmp_url");
        return !string.IsNullOrWhiteSpace(server);
    }

    private static bool LooksLikeTikTokOrVertical(JsonObject output, string? verticalUuid)
    {
        var searchable = string.Join(' ', new[]
        {
            ReadString(output, "name", "service", "service_name", "platform"),
            ReadString(output, "stream_server", "server", "url", "rtmp_url")
        }).ToLowerInvariant();
        if (searchable.Contains("tiktok") || searchable.Contains("byteoversea") || searchable.Contains("push-rtmp")) return true;

        if (!string.IsNullOrWhiteSpace(verticalUuid))
        {
            foreach (var key in new[] { "canvas_uuid", "canvasUuid", "canvas", "video_canvas_uuid", "output_canvas_uuid" })
            {
                var value = ReadString(output, key);
                if (value.Equals(verticalUuid, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    private static string ReadString(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            try
            {
                var value = obj[key]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            catch { }
        }
        return string.Empty;
    }
}
