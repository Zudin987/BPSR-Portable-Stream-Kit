namespace BPSRStreamKit.Services;

/// <summary>
/// Compatibility facade for the main window. All WebSocket protocol handling lives in
/// ObsAutomationService so StreamKit has one authentication/request implementation.
/// </summary>
public sealed class ObsControlService
{
    private readonly ObsAutomationService _automation = new();

    public Task<bool> IsMainStreamActiveAsync() => _automation.IsMainStreamActiveAsync();
    public Task StartMainStreamAsync() => _automation.StartMainStreamAsync();
    public Task StopMainStreamAsync() => _automation.StopMainStreamAsync();
    public Task SyncSceneItemTransformAsync(string sourceScene, string destinationScene, string sourceName) =>
        _automation.SyncSceneItemTransformAsync(sourceScene, destinationScene, sourceName);
}
