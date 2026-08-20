namespace BPSRStreamKit.Models;

public sealed record DetectionState(
    bool ObsReady,
    bool GameRunning,
    bool ResonanceLogsRunning,
    string? ObsPath = null,
    string? GamePath = null,
    string? ResonanceLogsPath = null);
