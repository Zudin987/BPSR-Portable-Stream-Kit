using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BPSRStreamKit.Infrastructure;
using BPSRStreamKit.Models;

namespace BPSRStreamKit.Services;

public enum StreamTheme
{
    ProfileA,
    ProfileB
}

public sealed class ObsService
{
    public void Launch(StreamMode mode, GameTarget? game = null, StreamTheme theme = StreamTheme.ProfileA,
        AvatarMode avatarMode = AvatarMode.VTubeStudio, VTubeCaptureTarget? vTubeTarget = null)
    {
        var obsExe = AppPaths.FindObsExe() ?? throw new FileNotFoundException("Portable OBS is not ready yet.");
        var workingDirectory = Path.GetDirectoryName(obsExe)!;

        if (mode != StreamMode.PlainObs)
        {
            EnsureCleanRestart();
            var selected = game ?? throw new InvalidOperationException("Choose a running game first.");
            if (!selected.IsRunning)
                throw new InvalidOperationException($"{selected.DisplayName} is not running. Open the game and refresh the game list first.");

            ApplyTheme(theme, avatarMode, vTubeTarget);
            PrepareGenericCapture(selected, vertical: false);
            if (mode == StreamMode.AllPlatforms) PrepareGenericCapture(selected, vertical: true);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = obsExe,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add("--portable");
        startInfo.ArgumentList.Add("--disable-shutdown-check");
        startInfo.ArgumentList.Add("--disable-updater");
        AddWebSocketArguments(startInfo);

        switch (mode)
        {
            case StreamMode.DiscordOnly:
                // Discord screen-share mode uses a clean Program projector instead of Virtual Camera.
                // The launcher opens that projector after OBS WebSocket is ready.
                AddSelection(startInfo, "Discord Share", "BPSR Horizontal", "Game Clean");
                break;
            case StreamMode.AllPlatforms:
                AddSelection(startInfo, "Twitch 1080p", "BPSR Horizontal", "Game Clean");
                // Keep Virtual Camera available as an optional Discord fallback while the default
                // Discord workflow is the projector opened by StreamKit.
                startInfo.ArgumentList.Add("--startvirtualcam");
                break;
        }

        Process.Start(startInfo);
    }

    public void LaunchAvatarPreview(StreamTheme theme)
    {
        var obsExe = AppPaths.FindObsExe() ?? throw new FileNotFoundException("Portable OBS is not ready yet.");
        EnsureCleanRestart();
        ApplyTheme(theme, AvatarMode.VTubeStudio, null);
        AudioPrivacyService.HardenPortableObsConfig();

        var startInfo = new ProcessStartInfo
        {
            FileName = obsExe,
            WorkingDirectory = Path.GetDirectoryName(obsExe)!,
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add("--portable");
        startInfo.ArgumentList.Add("--disable-shutdown-check");
        startInfo.ArgumentList.Add("--disable-updater");
        AddWebSocketArguments(startInfo);
        AddSelection(startInfo, "Discord Share", "BPSR Horizontal", "Game Clean");
        Process.Start(startInfo);
    }

    public void LaunchAitumBootstrap(GameTarget game, StreamTheme theme, AvatarMode avatarMode, VTubeCaptureTarget? vTubeTarget)
    {
        var obsExe = AppPaths.FindObsExe() ?? throw new FileNotFoundException("Portable OBS is not ready yet.");
        EnsureCleanRestart();
        ApplyTheme(theme, avatarMode, vTubeTarget);
        PrepareGenericCapture(game, vertical: false);

        var info = new ProcessStartInfo
        {
            FileName = obsExe,
            WorkingDirectory = Path.GetDirectoryName(obsExe)!,
            UseShellExecute = true
        };
        info.ArgumentList.Add("--portable");
        info.ArgumentList.Add("--disable-shutdown-check");
        info.ArgumentList.Add("--disable-updater");
        AddWebSocketArguments(info);
        AddSelection(info, "Twitch 1080p", "BPSR Horizontal", "Game Clean");
        Process.Start(info);
    }

    public void PrepareAllPlatforms(string verticalCanvasUuid, GameTarget game, StreamTheme theme,
        AvatarMode avatarMode, VTubeCaptureTarget? vTubeTarget)
    {
        if (string.IsNullOrWhiteSpace(verticalCanvasUuid))
            throw new ArgumentException("Vertical canvas UUID is required.", nameof(verticalCanvasUuid));

        EnsureCleanRestart();
        ApplyTheme(theme, avatarMode, vTubeTarget);
        PrepareGenericCapture(game, vertical: false);
        PrepareGenericCapture(game, vertical: true);
        ImportVerticalCanvasScene(verticalCanvasUuid, avatarMode);
    }

    public bool Stop()
    {
        var obsExe = AppPaths.FindObsExe();
        if (string.IsNullOrWhiteSpace(obsExe)) return false;
        var expectedPath = Path.GetFullPath(obsExe);
        var processName = Path.GetFileNameWithoutExtension(obsExe);
        var stoppedAny = false;

        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                var actualPath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(actualPath) ||
                    !Path.GetFullPath(actualPath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase)) continue;
                stoppedAny = true;
                if (!process.CloseMainWindow() || !process.WaitForExit(4000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
            catch { }
            finally { process.Dispose(); }
        }
        return stoppedAny;
    }

    private void EnsureCleanRestart()
    {
        if (Stop()) Thread.Sleep(1100);
    }

    private static void AddWebSocketArguments(ProcessStartInfo info)
    {
        info.ArgumentList.Add("--websocket_port");
        info.ArgumentList.Add(ObsAutomationService.Port.ToString());
        info.ArgumentList.Add("--websocket_password");
        info.ArgumentList.Add(ObsAutomationService.GetOrCreatePassword());
        info.ArgumentList.Add("--websocket_ipv4_only");
    }

    private static void ApplyTheme(StreamTheme theme, AvatarMode avatarMode, VTubeCaptureTarget? vTubeTarget)
    {
        var configRoot = AppPaths.ObsConfigRoot() ?? throw new InvalidOperationException("Portable OBS config path could not be resolved.");
        var sceneRoot = Path.Combine(configRoot, "basic", "scenes");

        string avatarDirectory;
        string horizontalFrame;
        string verticalFrame;
        string horizontalStarting;
        string horizontalBrb;
        string verticalStarting;
        string verticalBrb;
        var showDpsPanel = theme == StreamTheme.ProfileA;

        if (theme == StreamTheme.ProfileB)
        {
            var themeRoot = Path.Combine(AppPaths.AssetsDirectory, "Themes", "Profile_B_Doctor");
            avatarDirectory = Path.Combine(themeRoot, "Avatar");
            horizontalFrame = Path.Combine(themeRoot, "Frames", "Discord_1080p.png");
            verticalFrame = Path.Combine(themeRoot, "Frames", "TikTok_1080x1920.png");
            horizontalStarting = Path.Combine(themeRoot, "Screens", "Starting_1080p.jpg");
            horizontalBrb = Path.Combine(themeRoot, "Screens", "BRB_1080p.jpg");
            verticalStarting = Path.Combine(themeRoot, "Screens", "Starting_TikTok_1080x1920.jpg");
            verticalBrb = Path.Combine(themeRoot, "Screens", "BRB_TikTok_1080x1920.jpg");
        }
        else
        {
            avatarDirectory = Path.Combine(AppPaths.AssetsDirectory, "MyAvatar");
            horizontalFrame = Path.Combine(AppPaths.AssetsDirectory, "Frames", "01_Minimal_Thin_1080p.png");
            verticalFrame = Path.Combine(AppPaths.AssetsDirectory, "Frames", "05_TikTok_Minimal_1080x1920.png");
            horizontalStarting = Path.Combine(AppPaths.AssetsDirectory, "Screens", "Starting_1080p.png");
            horizontalBrb = Path.Combine(AppPaths.AssetsDirectory, "Screens", "BRB_1080p.png");
            verticalStarting = Path.Combine(AppPaths.AssetsDirectory, "Screens", "Starting_TikTok_1080x1920.png");
            verticalBrb = Path.Combine(AppPaths.AssetsDirectory, "Screens", "BRB_TikTok_1080x1920.png");
        }

        var required = new[]
        {
            Path.Combine(avatarDirectory, "idle.png"), Path.Combine(avatarDirectory, "blink.png"),
            Path.Combine(avatarDirectory, "action.png"), Path.Combine(avatarDirectory, "talk_a.png"),
            horizontalFrame, verticalFrame, horizontalStarting, horizontalBrb, verticalStarting, verticalBrb
        };
        var missing = required.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
            throw new FileNotFoundException("The selected theme is incomplete. Download the latest complete StreamKit release ZIP or use Advanced → Repair.", missing);

        ApplyThemeToCollection(Path.Combine(sceneRoot, "BPSR_Horizontal.json"), "Minimal Stream Frame", avatarDirectory,
            horizontalFrame, horizontalStarting, horizontalBrb, showDpsPanel, false, avatarMode, vTubeTarget);
        ApplyThemeToCollection(Path.Combine(sceneRoot, "BPSR_TikTok_Vertical.json"), "TikTok Minimal Frame", avatarDirectory,
            verticalFrame, verticalStarting, verticalBrb, showDpsPanel, true, avatarMode, vTubeTarget);
    }

    private static void ApplyThemeToCollection(string file, string frameSourceName, string avatarDirectory,
        string frameFile, string startingScreen, string brbScreen, bool showDpsPanel, bool vertical,
        AvatarMode avatarMode, VTubeCaptureTarget? vTubeTarget)
    {
        _ = vTubeTarget;
        if (!File.Exists(file)) throw new FileNotFoundException("The OBS scene collection is missing. Use Advanced → Repair once.", file);
        var root = JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? throw new InvalidDataException("OBS scene collection could not be read.");
        var sources = root["sources"]?.AsArray() ?? throw new InvalidDataException("OBS scene collection has no sources array.");
        static string ObsPath(string path) => path.Replace('\\', '/');

        var avatar = FindSource(sources, "FloodTuber Avatar") ?? throw new InvalidOperationException("The PNG avatar source is missing. Use Advanced → Repair once.");
        var avatarSettings = avatar["settings"]?.AsObject() ?? new JsonObject();
        avatar["settings"] = avatarSettings;
        avatarSettings["path_idle"] = ObsPath(Path.Combine(avatarDirectory, "idle.png"));
        avatarSettings["path_blink"] = ObsPath(Path.Combine(avatarDirectory, "blink.png"));
        avatarSettings["path_action"] = ObsPath(Path.Combine(avatarDirectory, "action.png"));
        avatarSettings["path_talk_1"] = ObsPath(Path.Combine(avatarDirectory, "talk_a.png"));
        avatarSettings["path_talk_2"] = ObsPath(Path.Combine(avatarDirectory, "talk_a.png"));
        avatarSettings["path_talk_3"] = ObsPath(Path.Combine(avatarDirectory, "talk_a.png"));
        avatarSettings["custom_avatars_path"] = ObsPath(avatarDirectory);

        var vTube = FindSource(sources, "VTube Studio Avatar") ?? throw new InvalidOperationException("The VTube Studio source is missing. Use Advanced → Repair once.");
        if (avatarMode == AvatarMode.VTubeStudio)
        {
            vTube["id"] = "spout_capture";
            vTube["versioned_id"] = "spout_capture";
            vTube["mixers"] = 0;
            vTube["settings"] = new JsonObject
            {
                ["spoutsenders"] = "VTubeStudioSpout",
                ["tickspeedlimit"] = 100,
                ["compositemode"] = 4
            };
        }

        var frame = FindSource(sources, frameSourceName) ?? throw new InvalidOperationException($"The stream frame source '{frameSourceName}' is missing.");
        var frameSettings = frame["settings"]?.AsObject() ?? new JsonObject();
        frame["settings"] = frameSettings;
        frameSettings["file"] = ObsPath(frameFile);

        foreach (var pair in new[] { ("Starting Screen", startingScreen), ("BRB Screen", brbScreen) })
        {
            var source = FindSource(sources, pair.Item1) ?? throw new InvalidOperationException($"The {pair.Item1} source is missing.");
            var settings = source["settings"]?.AsObject() ?? new JsonObject();
            source["settings"] = settings;
            settings["file"] = ObsPath(pair.Item2);
        }

        foreach (var scene in sources.OfType<JsonObject>().Where(x => string.Equals(x["id"]?.GetValue<string>(), "scene", StringComparison.Ordinal)))
        {
            var items = scene["settings"]?["items"]?.AsArray();
            if (items is null) continue;
            foreach (var item in items.OfType<JsonObject>())
            {
                var name = item["name"]?.GetValue<string>();
                if (string.Equals(name, "DPS Panel", StringComparison.Ordinal)) item["visible"] = showDpsPanel;
                if (string.Equals(name, frameSourceName, StringComparison.Ordinal))
                    SetSceneItemBox(item, 0, 0, vertical ? 1080 : 1920, vertical ? 1920 : 1080);
                if (string.Equals(name, "FloodTuber Avatar", StringComparison.Ordinal))
                {
                    item["visible"] = avatarMode == AvatarMode.PngAvatar;
                    SetSceneItemBox(item, 24, vertical ? 1380 : 580, vertical ? 430 : 420, vertical ? 520 : 490);
                }
                if (string.Equals(name, "VTube Studio Avatar", StringComparison.Ordinal))
                {
                    item["visible"] = avatarMode == AvatarMode.VTubeStudio;
                    SetSceneItemBox(item, 20, vertical ? 1270 : 500, vertical ? 560 : 520, vertical ? 650 : 570);
                }
            }
        }
        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static void ImportVerticalCanvasScene(string canvasUuid, AvatarMode avatarMode)
    {
        var configRoot = AppPaths.ObsConfigRoot() ?? throw new InvalidOperationException("Portable OBS config path could not be resolved.");
        var sceneRoot = Path.Combine(configRoot, "basic", "scenes");
        var horizontalFile = Path.Combine(sceneRoot, "BPSR_Horizontal.json");
        var verticalFile = Path.Combine(sceneRoot, "BPSR_TikTok_Vertical.json");

        var horizontal = JsonNode.Parse(File.ReadAllText(horizontalFile))?.AsObject() ?? throw new InvalidDataException("Horizontal OBS collection is invalid.");
        var vertical = JsonNode.Parse(File.ReadAllText(verticalFile))?.AsObject() ?? throw new InvalidDataException("Vertical OBS collection is invalid.");
        var dst = horizontal["sources"]?.AsArray() ?? throw new InvalidDataException("Horizontal collection has no sources.");
        var src = vertical["sources"]?.AsArray() ?? throw new InvalidDataException("Vertical collection has no sources.");

        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Selected Game + Audio"] = "Vertical - Selected Game + Audio",
            ["TikTok Minimal Frame"] = "Vertical - Stream Frame",
            ["FloodTuber Avatar"] = "Vertical - PNG Avatar",
            ["VTube Studio Avatar"] = "Vertical - VTube Studio Avatar",
            ["Starting Screen"] = "Vertical - Starting Screen",
            ["BRB Screen"] = "Vertical - BRB Screen"
        };
        var sceneNames = new[] { "Vertical Live", "Vertical Starting Soon", "Vertical BRB" };
        var removeNames = names.Values.Concat(sceneNames).ToHashSet(StringComparer.Ordinal);
        for (var i = dst.Count - 1; i >= 0; i--)
        {
            var name = dst[i]?["name"]?.GetValue<string>();
            if (name is not null && removeNames.Contains(name)) dst.RemoveAt(i);
        }

        var uuidMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in names)
        {
            var original = FindSource(src, pair.Key) ?? throw new InvalidOperationException($"Vertical source '{pair.Key}' is missing.");
            var clone = original.DeepClone().AsObject();
            var uuid = Guid.NewGuid().ToString();
            uuidMap[pair.Key] = uuid;
            clone["name"] = pair.Value;
            clone["uuid"] = uuid;
            clone["canvas_uuid"] = canvasUuid;
            dst.Add(clone);
        }

        void AddScene(string sourceSceneName, string destinationSceneName)
        {
            var sourceScene = FindSource(src, sourceSceneName) ?? throw new InvalidOperationException($"Vertical scene '{sourceSceneName}' is missing. Use Repair once.");
            var scene = sourceScene.DeepClone().AsObject();
            scene["name"] = destinationSceneName;
            scene["uuid"] = Guid.NewGuid().ToString();
            scene["canvas_uuid"] = canvasUuid;
            scene["hotkeys"] = new JsonObject { ["OBSBasic.SelectScene"] = new JsonArray() };
            var settings = scene["settings"]?.AsObject() ?? new JsonObject();
            scene["settings"] = settings;
            var items = settings["items"]?.AsArray() ?? new JsonArray();
            settings["items"] = items;

            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (items[i] is not JsonObject item) continue;
                var oldName = item["name"]?.GetValue<string>();
                if (oldName is null || !names.TryGetValue(oldName, out var newName))
                {
                    items.RemoveAt(i);
                    continue;
                }
                item["name"] = newName;
                item["source_uuid"] = uuidMap[oldName];
                if (oldName == "FloodTuber Avatar") item["visible"] = avatarMode == AvatarMode.PngAvatar;
                if (oldName == "VTube Studio Avatar") item["visible"] = avatarMode == AvatarMode.VTubeStudio;
            }

            settings["id_counter"] = items.OfType<JsonObject>().Select(x => x["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty().Max();
            dst.Add(scene);
        }

        AddScene("Game Clean Vertical", "Vertical Live");
        AddScene("Starting Soon", "Vertical Starting Soon");
        AddScene("BRB", "Vertical BRB");

        var order = horizontal["scene_order"]?.AsArray();
        if (order is not null)
        {
            for (var i = order.Count - 1; i >= 0; i--)
            {
                var name = order[i]?["name"]?.GetValue<string>();
                if (name is not null && sceneNames.Contains(name, StringComparer.Ordinal)) order.RemoveAt(i);
            }
            foreach (var name in sceneNames) order.Add(new JsonObject { ["name"] = name });
        }
        File.WriteAllText(horizontalFile, horizontal.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static void SetSceneItemBox(JsonObject item, double x, double y, double width, double height)
    {
        item["pos"] = new JsonObject { ["x"] = x, ["y"] = y };
        item["scale"] = new JsonObject { ["x"] = 1.0, ["y"] = 1.0 };
        item["align"] = 5;
        item["bounds_type"] = 2;
        item["bounds_align"] = 5;
        item["bounds_crop"] = false;
        item["bounds"] = new JsonObject { ["x"] = width, ["y"] = height };
        item["scale_filter"] = "lanczos";
    }

    private static JsonObject? FindSource(JsonArray sources, string name) => sources.OfType<JsonObject>()
        .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), name, StringComparison.Ordinal));

    private static void PrepareGenericCapture(GameTarget game, bool vertical)
    {
        var configRoot = AppPaths.ObsConfigRoot() ?? throw new InvalidOperationException("Portable OBS config path could not be resolved.");
        var file = Path.Combine(configRoot, "basic", "scenes", vertical ? "BPSR_TikTok_Vertical.json" : "BPSR_Horizontal.json");
        if (!File.Exists(file)) throw new FileNotFoundException("The clean-game scene has not been prepared yet. Use Advanced settings → Repair once.", file);
        var root = JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? throw new InvalidDataException("OBS scene collection could not be read.");
        var sources = root["sources"]?.AsArray() ?? throw new InvalidDataException("OBS scene collection has no sources array.");
        var source = FindSource(sources, "Selected Game + Audio") ?? throw new InvalidOperationException("The clean-game capture source is missing. Use Repair once.");
        var settings = source["settings"]?.AsObject() ?? new JsonObject();
        source["settings"] = settings;
        settings["capture_mode"] = "window";
        settings["capture_audio"] = true;
        settings["capture_cursor"] = false;
        settings["window"] = game.ObsWindowString;
        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static void AddSelection(ProcessStartInfo info, string profile, string collection, string scene)
    {
        info.ArgumentList.Add("--profile"); info.ArgumentList.Add(profile);
        info.ArgumentList.Add("--collection"); info.ArgumentList.Add(collection);
        info.ArgumentList.Add("--scene"); info.ArgumentList.Add(scene);
    }
}
