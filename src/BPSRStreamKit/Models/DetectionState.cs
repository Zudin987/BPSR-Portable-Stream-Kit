namespace BPSRStreamKit.Models;

public sealed record DetectionState(
    bool ObsReady,
    bool GameRunning,
    bool ResonanceLogsRunning,
    bool AvatarReady,
    bool AudioIsolationReady,
    bool VTubeStudioRunning,
    bool AitumReady,
    string? ObsPath = null,
    string? GamePath = null,
    string? ResonanceLogsPath = null);
