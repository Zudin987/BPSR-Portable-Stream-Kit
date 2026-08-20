using System.Diagnostics;
using BPSRStreamKit.Infrastructure;

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
    public void Launch(StreamTarget target)
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
            case StreamTarget.PlainObs:
                break;
        }

        Process.Start(startInfo);
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
