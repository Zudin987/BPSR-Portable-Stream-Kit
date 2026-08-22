using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit.Services;

public sealed class ObsCrashRecoveryService
{
    private IntPtr _lastDialog;
    private DateTime _lastActionUtc = DateTime.MinValue;

    public bool TryContinuePortableObsNormally()
    {
        var handled = false;
        try
        {
            var expectedExe = AppPaths.FindObsExe();
            if (string.IsNullOrWhiteSpace(expectedExe)) return false;
            var expectedPath = Path.GetFullPath(expectedExe);

            EnumWindows((hwnd, unused) =>
            {
                _ = unused;
                try
                {
                    if (!IsWindowVisible(hwnd)) return true;
                    var length = GetWindowTextLength(hwnd);
                    if (length <= 0) return true;

                    var title = new StringBuilder(length + 1);
                    _ = GetWindowText(hwnd, title, title.Capacity);
                    if (!title.ToString().Contains("OBS Studio Crash Detected", StringComparison.OrdinalIgnoreCase))
                        return true;

                    _ = GetWindowThreadProcessId(hwnd, out var pid);
                    using var process = Process.GetProcessById((int)pid);
                    string? actualPath = null;
                    try { actualPath = process.MainModule?.FileName; } catch { }
                    if (string.IsNullOrWhiteSpace(actualPath)
                        || !Path.GetFullPath(actualPath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (hwnd == _lastDialog && DateTime.UtcNow - _lastActionUtc < TimeSpan.FromSeconds(3))
                        return false;

                    _lastDialog = hwnd;
                    _lastActionUtc = DateTime.UtcNow;
                    _ = SetForegroundWindow(hwnd);
                    _ = PostMessage(hwnd, WmKeyDown, new IntPtr(VkReturn), IntPtr.Zero);
                    _ = PostMessage(hwnd, WmKeyUp, new IntPtr(VkReturn), IntPtr.Zero);
                    handled = true;
                    return false;
                }
                catch { return true; }
            }, IntPtr.Zero);
        }
        catch { }
        return handled;
    }

    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const int VkReturn = 0x0D;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
