namespace BPSRStreamKit.Models;

public sealed record GameTarget(
    string DisplayName,
    string ProcessName,
    string ExecutableName,
    string WindowTitle,
    string WindowClass,
    bool IsBpsr = false,
    bool IsRunning = true)
{
    public string LayoutLabel => IsBpsr
        ? "Full layout · DPS + Dungeon HUD"
        : "Clean layout · Game + Avatar + Frame";

    public string ObsWindowString => $"{Escape(WindowTitle)}:{WindowClass}:{ExecutableName}";

    private static string Escape(string value) => value.Replace(":", "#3A", StringComparison.Ordinal);
}
