using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BPSRStreamKit.Infrastructure;
using BPSRStreamKit.Models;

namespace BPSRStreamKit.Services;

public enum StreamTarget
{
    Discord,
    Twitch,
    TikTok,
    PlainObs
}

public enum StreamTheme
{
    ProfileA,
    ProfileB
}

public sealed class ObsService
{
    public void Launch(StreamTarget target, GameTarget? game = null, StreamTheme theme = StreamTheme.ProfileA)
    {
        var obsExe = AppPaths.FindObsExe() ?? throw new FileNotFoundException("Portable OBS is not ready yet.");
        var workingDirectory = Path.GetDirectoryName(obsExe)!;

        ApplyTheme(theme);

        var startInfo = new ProcessStartInfo
        {
            FileName = obsExe,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        };

        startInfo.ArgumentList.Add("--portable");
        startInfo.ArgumentList.Add("--disable-shutdown-check");

        if (target != StreamTarget.PlainObs)
        {
            var selected = game ?? new GameTarget(
                "Blue Protocol: Star Resonance", "StarSEA", "StarSEA.exe",
                "Blue Protocol: Star Resonance", "UnityWndClass", true, false);

            if (selected.IsBpsr)
            {
                switch (target)
                {
                    case StreamTarget.Discord:
                        AddSelection(startInfo, "Discord Share", "BPSR Horizontal", "Discord Share");
                        break;
                    case StreamTarget.Twitch:
                        AddSelection(startInfo, "Twitch 1080p", "BPSR Horizontal", "Twitch Live");
                        break;
                    case StreamTarget.TikTok:
                        AddSelection(startInfo, "TikTok Vertical", "BPSR TikTok Vertical", "TikTok Live");
                        break;
                }
            }
            else
            {
                if (!selected.IsRunning)
                    throw new InvalidOperationException($"{selected.DisplayName} is not running. Open the game and refresh the game list first.");

                PrepareGenericCapture(selected, vertical: target == StreamTarget.TikTok);

                switch (target)
                {
                    case StreamTarget.Discord:
                        AddSelection(startInfo, "Discord Share", "BPSR Horizontal", "Game Clean");
                        break;
                    case StreamTarget.Twitch:
                        AddSelection(startInfo, "Twitch 1080p", "BPSR Horizontal", "Game Clean");
                        break;
                    case StreamTarget.TikTok:
                        AddSelection(startInfo, "TikTok Vertical", "BPSR TikTok Vertical", "Game Clean Vertical");
                        break;
                }
            }
        }

        Process.Start(startInfo);
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
                    !Path.GetFullPath(actualPath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                stoppedAny = true;
                if (!process.CloseMainWindow() || !process.WaitForExit(3000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
            catch
            {
                // Do not terminate another OBS installation when its executable path cannot be verified.
            }
            finally
            {
                process.Dispose();
            }
        }

        return stoppedAny;
    }

    private static void ApplyTheme(StreamTheme theme)
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
            Path.Combine(avatarDirectory, "idle.png"),
            Path.Combine(avatarDirectory, "blink.png"),
            Path.Combine(avatarDirectory, "action.png"),
            Path.Combine(avatarDirectory, "talk_a.png"),
            horizontalFrame,
            verticalFrame,
            horizontalStarting,
            horizontalBrb,
            verticalStarting,
            verticalBrb
        };

        var missing = required.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
            throw new FileNotFoundException("The selected theme is incomplete. Download the latest complete StreamKit release ZIP or use Advanced → Repair.", missing);

        ApplyThemeToCollection(
            Path.Combine(sceneRoot, "BPSR_Horizontal.json"),
            "Minimal Stream Frame",
            avatarDirectory,
            horizontalFrame,
            horizontalStarting,
            horizontalBrb,
            showDpsPanel,
            vertical: false);

        ApplyThemeToCollection(
            Path.Combine(sceneRoot, "BPSR_TikTok_Vertical.json"),
            "TikTok Minimal Frame",
            avatarDirectory,
            verticalFrame,
            verticalStarting,
            verticalBrb,
            showDpsPanel,
            vertical: true);
    }

    private static void ApplyThemeToCollection(
        string file,
        string frameSourceName,
        string avatarDirectory,
        string frameFile,
        string startingScreen,
        string brbScreen,
        bool showDpsPanel,
        bool vertical)
    {
        if (!File.Exists(file))
            throw new FileNotFoundException("The OBS scene collection is missing. Use Advanced → Repair once.", file);

        var root = JsonNode.Parse(File.ReadAllText(file))?.AsObject()
                   ?? throw new InvalidDataException("OBS scene collection could not be read.");
        var sources = root["sources"]?.AsArray()
                      ?? throw new InvalidDataException("OBS scene collection has no sources array.");

        var avatar = FindSource(sources, "FloodTuber Avatar")
                     ?? throw new InvalidOperationException("The FloodTuber avatar source is missing. Use Advanced → Repair once.");
        var avatarSettings = avatar["settings"]?.AsObject() ?? new JsonObject();
        avatar["settings"] = avatarSettings;

        static string ObsPath(string path) => path.Replace('\\', '/');

        avatarSettings["path_idle"] = ObsPath(Path.Combine(avatarDirectory, "idle.png"));
        avatarSettings["path_blink"] = ObsPath(Path.Combine(avatarDirectory, "blink.png"));
        avatarSettings["path_action"] = ObsPath(Path.Combine(avatarDirectory, "action.png"));
        avatarSettings["path_talk_1"] = ObsPath(Path.Combine(avatarDirectory, "talk_a.png"));
        avatarSettings["path_talk_2"] = ObsPath(Path.Combine(avatarDirectory, "talk_a.png"));
        avatarSettings["path_talk_3"] = ObsPath(Path.Combine(avatarDirectory, "talk_a.png"));
        avatarSettings["custom_avatars_path"] = ObsPath(avatarDirectory);

        var frame = FindSource(sources, frameSourceName)
                    ?? throw new InvalidOperationException($"The stream frame source '{frameSourceName}' is missing. Use Advanced → Repair once.");
        var frameSettings = frame["settings"]?.AsObject() ?? new JsonObject();
        frame["settings"] = frameSettings;
        frameSettings["file"] = ObsPath(frameFile);

        var starting = FindSource(sources, "Starting Screen")
                       ?? throw new InvalidOperationException("The Starting Soon screen source is missing. Use Advanced → Repair once.");
        var startingSettings = starting["settings"]?.AsObject() ?? new JsonObject();
        starting["settings"] = startingSettings;
        startingSettings["file"] = ObsPath(startingScreen);

        var brb = FindSource(sources, "BRB Screen")
                  ?? throw new InvalidOperationException("The BRB screen source is missing. Use Advanced → Repair once.");
        var brbSettings = brb["settings"]?.AsObject() ?? new JsonObject();
        brb["settings"] = brbSettings;
        brbSettings["file"] = ObsPath(brbScreen);

        foreach (var scene in sources.OfType<JsonObject>().Where(x => string.Equals(x["id"]?.GetValue<string>(), "scene", StringComparison.Ordinal)))
        {
            var items = scene["settings"]?["items"]?.AsArray();
            if (items is null) continue;

            foreach (var item in items.OfType<JsonObject>())
            {
                var name = item["name"]?.GetValue<string>();

                if (string.Equals(name, "DPS Panel", StringComparison.Ordinal))
                    item["visible"] = showDpsPanel;

                if (string.Equals(name, frameSourceName, StringComparison.Ordinal))
                    SetSceneItemBox(item, 0, 0, vertical ? 1080 : 1920, vertical ? 1920 : 1080);

                if (string.Equals(name, "FloodTuber Avatar", StringComparison.Ordinal))
                {
                    // A fixed bounding box makes Profile A and Profile B the same apparent size
                    // even when their underlying PNG canvases have different pixel dimensions.
                    SetSceneItemBox(item, 24, vertical ? 1380 : 580, vertical ? 430 : 420, vertical ? 520 : 490);
                }
            }
        }

        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
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

    private static JsonObject? FindSource(JsonArray sources, string name)
    {
        return sources.OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), name, StringComparison.Ordinal));
    }

    private static void PrepareGenericCapture(GameTarget game, bool vertical)
    {
        var configRoot = AppPaths.ObsConfigRoot() ?? throw new InvalidOperationException("Portable OBS config path could not be resolved.");
        var file = Path.Combine(configRoot, "basic", "scenes", vertical ? "BPSR_TikTok_Vertical.json" : "BPSR_Horizontal.json");
        if (!File.Exists(file))
            throw new FileNotFoundException("The clean-game scene has not been prepared yet. Use Advanced settings → Repair once.", file);

        var root = JsonNode.Parse(File.ReadAllText(file))?.AsObject()
                   ?? throw new InvalidDataException("OBS scene collection could not be read.");
        var sources = root["sources"]?.AsArray()
                      ?? throw new InvalidDataException("OBS scene collection has no sources array.");

        var source = sources
            .OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), "Selected Game + Audio", StringComparison.Ordinal));
        if (source is null)
            throw new InvalidOperationException("The clean-game capture source is missing. Use Advanced settings → Repair once.");

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
        info.ArgumentList.Add("--profile");
        info.ArgumentList.Add(profile);
        info.ArgumentList.Add("--collection");
        info.ArgumentList.Add(collection);
        info.ArgumentList.Add("--scene");
        info.ArgumentList.Add(scene);
    }
}
