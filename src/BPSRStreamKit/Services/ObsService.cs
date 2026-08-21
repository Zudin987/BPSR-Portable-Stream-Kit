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

    private static void ApplyTheme(StreamTheme theme)
    {
        var configRoot = AppPaths.ObsConfigRoot() ?? throw new InvalidOperationException("Portable OBS config path could not be resolved.");
        var sceneRoot = Path.Combine(configRoot, "basic", "scenes");

        string avatarDirectory;
        string horizontalFrame;
        string verticalFrame;
        var showDpsPanel = theme == StreamTheme.ProfileA;

        if (theme == StreamTheme.ProfileB)
        {
            var themeRoot = Path.Combine(AppPaths.AssetsDirectory, "Themes", "Profile_B_Doctor");
            avatarDirectory = Path.Combine(themeRoot, "Avatar");
            horizontalFrame = Path.Combine(themeRoot, "Frames", "Discord_1080p.png");
            verticalFrame = Path.Combine(themeRoot, "Frames", "TikTok_1080x1920.png");
        }
        else
        {
            avatarDirectory = Path.Combine(AppPaths.AssetsDirectory, "MyAvatar");
            horizontalFrame = Path.Combine(AppPaths.AssetsDirectory, "Frames", "01_Minimal_Thin_1080p.png");
            verticalFrame = Path.Combine(AppPaths.AssetsDirectory, "Frames", "05_TikTok_Minimal_1080x1920.png");
        }

        var required = new[]
        {
            Path.Combine(avatarDirectory, "idle.png"),
            Path.Combine(avatarDirectory, "blink.png"),
            Path.Combine(avatarDirectory, "action.png"),
            Path.Combine(avatarDirectory, "talk_a.png"),
            horizontalFrame,
            verticalFrame
        };

        var missing = required.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
            throw new FileNotFoundException("The selected theme is incomplete. Download the latest complete StreamKit release ZIP or use Advanced → Repair.", missing);

        ApplyThemeToCollection(
            Path.Combine(sceneRoot, "BPSR_Horizontal.json"),
            "Minimal Stream Frame",
            avatarDirectory,
            horizontalFrame,
            showDpsPanel);

        ApplyThemeToCollection(
            Path.Combine(sceneRoot, "BPSR_TikTok_Vertical.json"),
            "TikTok Minimal Frame",
            avatarDirectory,
            verticalFrame,
            showDpsPanel);
    }

    private static void ApplyThemeToCollection(
        string file,
        string frameSourceName,
        string avatarDirectory,
        string frameFile,
        bool showDpsPanel)
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

        foreach (var scene in sources.OfType<JsonObject>().Where(x => string.Equals(x["id"]?.GetValue<string>(), "scene", StringComparison.Ordinal)))
        {
            var items = scene["settings"]?["items"]?.AsArray();
            if (items is null) continue;

            foreach (var item in items.OfType<JsonObject>().Where(x => string.Equals(x["name"]?.GetValue<string>(), "DPS Panel", StringComparison.Ordinal)))
                item["visible"] = showDpsPanel;
        }

        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
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
