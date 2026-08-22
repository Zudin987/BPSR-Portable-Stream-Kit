using System.Text.Json;
using System.Text.Json.Nodes;
using BPSRStreamKit.Infrastructure;
using BPSRStreamKit.Models;

namespace BPSRStreamKit.Services;

public sealed class SceneLayoutService
{
    private const string SelectedGameSource = "Selected Game + Audio";
    private const string LegacyGameSource = "BPSR Game + Audio";
    private const string VTubeSource = "VTube Studio Avatar";
    private const string PngAvatarSource = "FloodTuber Avatar";
    private const string DpsSource = "DPS Meter";
    private const string DungeonSource = "Dungeon Mech";

    public void PrepareBaseScenes(AvatarMode avatarMode)
    {
        var configRoot = AppPaths.ObsConfigRoot()
                         ?? throw new InvalidOperationException("Portable OBS config path could not be resolved.");
        var sceneRoot = Path.Combine(configRoot, "basic", "scenes");
        PatchHorizontal(Path.Combine(sceneRoot, "BPSR_Horizontal.json"), avatarMode);
        PatchVertical(Path.Combine(sceneRoot, "BPSR_TikTok_Vertical.json"), avatarMode);
    }

    public void FinalizeVerticalBpsrScene(string canvasUuid)
    {
        if (string.IsNullOrWhiteSpace(canvasUuid)) return;

        var configRoot = AppPaths.ObsConfigRoot()
                         ?? throw new InvalidOperationException("Portable OBS config path could not be resolved.");
        var sceneRoot = Path.Combine(configRoot, "basic", "scenes");
        var horizontalPath = Path.Combine(sceneRoot, "BPSR_Horizontal.json");
        var verticalPath = Path.Combine(sceneRoot, "BPSR_TikTok_Vertical.json");

        var horizontal = ReadObject(horizontalPath);
        var vertical = ReadObject(verticalPath);
        var hSources = Sources(horizontal);
        var vSources = Sources(vertical);

        RemoveSourceByName(hSources, "Vertical BPSR");
        RemoveSourceByName(hSources, "Vertical - DPS Meter");
        RemoveSourceByName(hSources, "Vertical - Dungeon Mech");
        RemoveOrder(horizontal, "Vertical BPSR");

        var live = FindSource(hSources, "Vertical Live");
        var verticalBase = FindSource(vSources, "Game Clean Vertical") ?? FindSource(vSources, "TikTok Live");
        if (live is null || verticalBase is null) return;

        var bpsr = live.DeepClone().AsObject();
        bpsr["name"] = "Vertical BPSR";
        bpsr["uuid"] = Guid.NewGuid().ToString();
        bpsr["canvas_uuid"] = canvasUuid;
        bpsr["hotkeys"] = new JsonObject { ["OBSBasic.SelectScene"] = new JsonArray() };

        var bpsrSettings = bpsr["settings"]?.AsObject() ?? new JsonObject();
        bpsr["settings"] = bpsrSettings;
        var bpsrItems = bpsrSettings["items"]?.AsArray() ?? new JsonArray();
        bpsrSettings["items"] = bpsrItems;

        var sourceItems = verticalBase["settings"]?["items"]?.AsArray();
        var nextId = bpsrItems.OfType<JsonObject>().Select(x => x["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty().Max() + 1;

        foreach (var pair in new[]
                 {
                     (Original: DpsSource, NewName: "Vertical - DPS Meter"),
                     (Original: DungeonSource, NewName: "Vertical - Dungeon Mech")
                 })
        {
            var originalSource = FindSource(vSources, pair.Original);
            if (originalSource is null) continue;

            var sourceClone = originalSource.DeepClone().AsObject();
            var clonedUuid = Guid.NewGuid().ToString();
            sourceClone["name"] = pair.NewName;
            sourceClone["uuid"] = clonedUuid;
            sourceClone["canvas_uuid"] = canvasUuid;
            hSources.Add(sourceClone);

            var originalItem = sourceItems?.OfType<JsonObject>()
                .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), pair.Original, StringComparison.Ordinal));
            if (originalItem is null) continue;

            var item = originalItem.DeepClone().AsObject();
            item["name"] = pair.NewName;
            item["source_uuid"] = clonedUuid;
            item["id"] = nextId++;
            item["visible"] = true;
            bpsrItems.Add(item);
        }

        bpsrSettings["id_counter"] = Math.Max(0, nextId - 1);
        hSources.Add(bpsr);
        AddOrder(horizontal, "Vertical BPSR");
        WriteObject(horizontalPath, horizontal);
    }

    private static void PatchHorizontal(string path, AvatarMode avatarMode)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The horizontal StreamKit scene template is missing. Use Fix setup once.", path);

        var root = ReadObject(path);
        var sources = Sources(root);
        NormalizeGameSource(sources);
        var vtube = EnsureVTubeSource(sources);

        RemoveSourceByName(sources, "Vertical BPSR");
        RemoveSourceByName(sources, "Vertical - DPS Meter");
        RemoveSourceByName(sources, "Vertical - Dungeon Mech");
        RemoveOrder(root, "Vertical BPSR");

        var baseScene = FindSource(sources, "Twitch Live")
                        ?? FindSource(sources, "Discord Share")
                        ?? FindSource(sources, "Game Clean")
                        ?? FindSource(sources, "BPSR")
                        ?? throw new InvalidDataException("StreamKit could not find a gameplay scene to prepare.");

        var clean = CloneGameplayScene(baseScene, "Game Clean", vtube, avatarMode, showHud: false);
        var bpsr = CloneGameplayScene(baseScene, "BPSR", vtube, avatarMode, showHud: true);

        // Legacy v2.0-v2.3 scenes were only cloning templates. Once Game Clean/BPSR are rebuilt,
        // keeping them visible in OBS is confusing and provides no additional function.
        RemoveSourceByName(sources, "Discord Share");
        RemoveSourceByName(sources, "Twitch Live");
        RemoveOrder(root, "Discord Share");
        RemoveOrder(root, "Twitch Live");

        RemoveSourceByName(sources, "Game Clean");
        RemoveSourceByName(sources, "BPSR");
        sources.Add(clean);
        sources.Add(bpsr);

        RemoveOrder(root, "Game Clean");
        RemoveOrder(root, "BPSR");
        AddOrder(root, "Game Clean");
        AddOrder(root, "BPSR");
        root["current_scene"] = "Game Clean";
        root["current_program_scene"] = "Game Clean";
        WriteObject(path, root);
    }

    private static void PatchVertical(string path, AvatarMode avatarMode)
    {
        if (!File.Exists(path)) return;

        var root = ReadObject(path);
        var sources = Sources(root);
        NormalizeGameSource(sources);
        var vtube = EnsureVTubeSource(sources);

        var live = FindSource(sources, "Game Clean Vertical") ?? FindSource(sources, "TikTok Live");
        if (live is null)
            throw new InvalidDataException("The TikTok gameplay scene is missing. Use Fix setup once.");

        if (!string.Equals(live["name"]?.GetValue<string>(), "Game Clean Vertical", StringComparison.Ordinal))
        {
            live["name"] = "Game Clean Vertical";
            RenameOrder(root, "TikTok Live", "Game Clean Vertical");
            if (string.Equals(root["current_scene"]?.GetValue<string>(), "TikTok Live", StringComparison.Ordinal))
                root["current_scene"] = "Game Clean Vertical";
            if (string.Equals(root["current_program_scene"]?.GetValue<string>(), "TikTok Live", StringComparison.Ordinal))
                root["current_program_scene"] = "Game Clean Vertical";
        }

        SetHudVisibility(live, false);
        EnsureAvatarItems(live, vtube, avatarMode);
        WriteObject(path, root);
    }

    private static JsonObject CloneGameplayScene(JsonObject source, string name, JsonObject vtubeSource,
        AvatarMode avatarMode, bool showHud)
    {
        var scene = source.DeepClone().AsObject();
        scene["name"] = name;
        scene["uuid"] = Guid.NewGuid().ToString();
        scene["hotkeys"] = new JsonObject { ["OBSBasic.SelectScene"] = new JsonArray() };
        SetHudVisibility(scene, showHud);
        EnsureAvatarItems(scene, vtubeSource, avatarMode);
        return scene;
    }

    private static void NormalizeGameSource(JsonArray sources)
    {
        var game = FindSource(sources, SelectedGameSource) ?? FindSource(sources, LegacyGameSource);
        if (game is null) return;
        game["name"] = SelectedGameSource;

        foreach (var scene in sources.OfType<JsonObject>().Where(IsScene))
        {
            var items = scene["settings"]?["items"]?.AsArray();
            if (items is null) continue;
            foreach (var item in items.OfType<JsonObject>())
            {
                if (string.Equals(item["name"]?.GetValue<string>(), LegacyGameSource, StringComparison.Ordinal))
                    item["name"] = SelectedGameSource;
            }
        }
    }

    private static JsonObject EnsureVTubeSource(JsonArray sources)
    {
        var existing = FindSource(sources, VTubeSource);
        if (existing is not null) return existing;

        var source = new JsonObject
        {
            ["prev_ver"] = 537001985,
            ["name"] = VTubeSource,
            ["uuid"] = Guid.NewGuid().ToString(),
            ["id"] = "spout_capture",
            ["versioned_id"] = "spout_capture",
            ["settings"] = new JsonObject
            {
                ["spoutsenders"] = "VTubeStudioSpout",
                ["tickspeedlimit"] = 100,
                ["compositemode"] = 4
            },
            ["mixers"] = 0,
            ["sync"] = 0,
            ["flags"] = 0,
            ["volume"] = 1.0,
            ["balance"] = 0.5,
            ["enabled"] = true,
            ["muted"] = false,
            ["push-to-mute"] = false,
            ["push-to-mute-delay"] = 0,
            ["push-to-talk"] = false,
            ["push-to-talk-delay"] = 0,
            ["hotkeys"] = new JsonObject(),
            ["deinterlace_mode"] = 0,
            ["deinterlace_field_order"] = 0,
            ["monitoring_type"] = 0,
            ["private_settings"] = new JsonObject()
        };
        sources.Add(source);
        return source;
    }

    private static void EnsureAvatarItems(JsonObject scene, JsonObject vtubeSource, AvatarMode avatarMode)
    {
        var settings = scene["settings"]?.AsObject() ?? new JsonObject();
        scene["settings"] = settings;
        var items = settings["items"]?.AsArray() ?? new JsonArray();
        settings["items"] = items;

        var png = items.OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), PngAvatarSource, StringComparison.Ordinal));
        if (png is not null) png["visible"] = avatarMode == AvatarMode.PngAvatar;

        var vtube = items.OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), VTubeSource, StringComparison.Ordinal));
        if (vtube is null)
        {
            vtube = png?.DeepClone().AsObject() ?? new JsonObject
            {
                ["locked"] = true,
                ["rot"] = 0.0,
                ["align"] = 5,
                ["bounds_type"] = 2,
                ["bounds_align"] = 5,
                ["bounds_crop"] = false,
                ["crop_left"] = 0,
                ["crop_top"] = 0,
                ["crop_right"] = 0,
                ["crop_bottom"] = 0,
                ["pos"] = new JsonObject { ["x"] = 20.0, ["y"] = 500.0 },
                ["scale"] = new JsonObject { ["x"] = 1.0, ["y"] = 1.0 },
                ["bounds"] = new JsonObject { ["x"] = 520.0, ["y"] = 570.0 },
                ["scale_filter"] = "lanczos",
                ["blend_method"] = "default",
                ["blend_type"] = "normal",
                ["show_transition"] = new JsonObject { ["duration"] = 0 },
                ["hide_transition"] = new JsonObject { ["duration"] = 0 },
                ["private_settings"] = new JsonObject()
            };
            vtube["name"] = VTubeSource;
            vtube["source_uuid"] = vtubeSource["uuid"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
            vtube["id"] = items.OfType<JsonObject>().Select(x => x["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty().Max() + 1;
            items.Add(vtube);
        }
        else
        {
            vtube["source_uuid"] = vtubeSource["uuid"]?.GetValue<string>();
        }

        vtube["visible"] = avatarMode == AvatarMode.VTubeStudio;
        settings["id_counter"] = items.OfType<JsonObject>().Select(x => x["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty().Max();
    }

    private static void SetHudVisibility(JsonObject scene, bool visible)
    {
        var items = scene["settings"]?["items"]?.AsArray();
        if (items is null) return;
        foreach (var item in items.OfType<JsonObject>())
        {
            var name = item["name"]?.GetValue<string>();
            if (string.Equals(name, DpsSource, StringComparison.Ordinal) ||
                string.Equals(name, DungeonSource, StringComparison.Ordinal))
                item["visible"] = visible;
        }
    }

    private static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path))?.AsObject()
        ?? throw new InvalidDataException($"Scene file '{Path.GetFileName(path)}' could not be read.");

    private static void WriteObject(string path, JsonObject root) =>
        AtomicFile.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));

    private static JsonArray Sources(JsonObject root) =>
        root["sources"]?.AsArray() ?? throw new InvalidDataException("OBS scene collection has no sources array.");

    private static bool IsScene(JsonObject value) =>
        string.Equals(value["id"]?.GetValue<string>(), "scene", StringComparison.Ordinal);

    private static JsonObject? FindSource(JsonArray sources, string name) =>
        sources.OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), name, StringComparison.Ordinal));

    private static void RemoveSourceByName(JsonArray sources, string name)
    {
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            if (string.Equals(sources[i]?["name"]?.GetValue<string>(), name, StringComparison.Ordinal))
                sources.RemoveAt(i);
        }
    }

    private static JsonArray EnsureOrder(JsonObject root)
    {
        var order = root["scene_order"]?.AsArray();
        if (order is not null) return order;
        order = new JsonArray();
        root["scene_order"] = order;
        return order;
    }

    private static void RemoveOrder(JsonObject root, string name)
    {
        var order = root["scene_order"]?.AsArray();
        if (order is null) return;
        for (var i = order.Count - 1; i >= 0; i--)
        {
            if (string.Equals(order[i]?["name"]?.GetValue<string>(), name, StringComparison.Ordinal))
                order.RemoveAt(i);
        }
    }

    private static void AddOrder(JsonObject root, string name)
    {
        var order = EnsureOrder(root);
        order.Add(new JsonObject { ["name"] = name });
    }

    private static void RenameOrder(JsonObject root, string oldName, string newName)
    {
        var order = root["scene_order"]?.AsArray();
        if (order is null) return;
        foreach (var item in order.OfType<JsonObject>())
        {
            if (string.Equals(item["name"]?.GetValue<string>(), oldName, StringComparison.Ordinal))
                item["name"] = newName;
        }
    }
}
