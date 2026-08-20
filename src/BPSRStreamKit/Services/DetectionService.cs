using System.Diagnostics;
using BPSRStreamKit.Infrastructure;
using BPSRStreamKit.Models;

namespace BPSRStreamKit.Services;

public sealed class DetectionService
{
    public Task<DetectionState> DetectAsync(GameTarget? selectedGame = null)
    {
        return Task.Run(() =>
        {
            var obsPath = AppPaths.FindObsExe();
            var processName = selectedGame?.ProcessName ?? "StarSEA";
            var game = FindProcess(processName);
            var logs = FindProcess("resonance-logs-cn");

            var obsRoot = AppPaths.FindObsRoot();
            var avatarReady = obsRoot is not null
                && File.Exists(Path.Combine(obsRoot, "obs-plugins", "64bit", "flood-tuber.dll"))
                && File.Exists(Path.Combine(AppPaths.AssetsDirectory, "MyAvatar", "idle.png"))
                && File.Exists(Path.Combine(AppPaths.AssetsDirectory, "MyAvatar", "talk_a.png"));

            var audioReady = obsPath is not null
                && File.Exists(Path.Combine(AppPaths.TemplatesDirectory, "basic", "profiles", "Discord Share", "basic.ini"));

            return new DetectionState(
                ObsReady: obsPath is not null,
                GameRunning: game is not null,
                ResonanceLogsRunning: logs is not null,
                AvatarReady: avatarReady,
                AudioIsolationReady: audioReady,
                ObsPath: obsPath,
                GamePath: SafeProcessPath(game),
                ResonanceLogsPath: SafeProcessPath(logs));
        });
    }

    private static Process? FindProcess(string processName)
    {
        try { return Process.GetProcessesByName(processName).FirstOrDefault(); }
        catch { return null; }
    }

    private static string? SafeProcessPath(Process? process)
    {
        if (process is null) return null;
        try { return process.MainModule?.FileName; }
        catch { return null; }
        finally { process.Dispose(); }
    }
}
