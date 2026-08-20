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

public sealed class ObsService
{
    public void Launch(StreamTarget target, GameTarget? game = null)
    {
        var obsExe = AppPaths.FindObsExe() ?? throw new FileNotFoundException("Portable OBS is not ready yet.");
        var workingDirectory = Path.GetDirectoryName(obsExe)!;

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
