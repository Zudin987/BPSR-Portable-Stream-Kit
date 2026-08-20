using System.Diagnostics;
using BPSRStreamKit.Infrastructure;
using BPSRStreamKit.Models;

namespace BPSRStreamKit.Services;

public sealed class DetectionService
{
    public Task<DetectionState> DetectAsync()
    {
        return Task.Run(() =>
        {
            var obsPath = AppPaths.FindObsExe();
            var game = FindProcess("StarSEA");
            var logs = FindProcess("resonance-logs-cn");

            return new DetectionState(
                ObsReady: obsPath is not null,
                GameRunning: game is not null,
                ResonanceLogsRunning: logs is not null,
                ObsPath: obsPath,
                GamePath: SafeProcessPath(game),
                ResonanceLogsPath: SafeProcessPath(logs));
        });
    }

    private static Process? FindProcess(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeProcessPath(Process? process)
    {
        if (process is null) return null;
        try { return process.MainModule?.FileName; }
        catch { return null; }
        finally { process.Dispose(); }
    }
}
