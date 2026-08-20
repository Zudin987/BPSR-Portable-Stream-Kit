namespace BPSRStreamKit.Models;

public sealed record DetectionState(
    bool ObsReady,
    bool GameRunning,
    bool ResonanceLogsRunning,
    bool AvatarReady,
    bool AudioIsolationReady,
    string? ObsPath = null,
    string? GamePath = null,
    string? ResonanceLogsPath = null);
