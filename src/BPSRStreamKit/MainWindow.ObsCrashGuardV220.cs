using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit;

public partial class MainWindow
{
    private DispatcherTimer? _obsCrashGuardV220;
    private IntPtr _lastObsCrashDialogV220;
    private DateTime _lastObsCrashDialogActionUtcV220 = DateTime.MinValue;
    private static readonly bool ObsCrashGuardHookRegisteredV220 = RegisterObsCrashGuardHookV220();

    private static bool RegisterObsCrashGuardHookV220()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ObsCrashGuardWindowLoadedV220));
        return true;
    }

    private static void ObsCrashGuardWindowLoadedV220(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._obsCrashGuardV220 is not null) return;

        window._obsCrashGuardV220 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        window._obsCrashGuardV220.Tick += (_, _) => window.TryContinuePortableObsNormallyV220();
        window._obsCrashGuardV220.Start();
        window.Closed += (_, _) => window._obsCrashGuardV220?.Stop();
    }

    private void TryContinuePortableObsNormallyV220()
    {
        try
        {
            var expectedExe = AppPaths.FindObsExe();
            if (string.IsNullOrWhiteSpace(expectedExe)) return;
            var expectedPath = Path.GetFullPath(expectedExe);

            EnumWindows((hwnd, _) =>
            {
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

                    // OBS 32 shows this prompt after an unclean close and its default button is
                    // "Run in Normal Mode". StreamKit requires third-party plugins + WebSockets,
                    // so Safe Mode cannot provide a working StreamKit session.
                    var recentlyHandled = hwnd == _lastObsCrashDialogV220
                                          && DateTime.UtcNow - _lastObsCrashDialogActionUtcV220 < TimeSpan.FromSeconds(3);
                    if (recentlyHandled) return false;

                    _lastObsCrashDialogV220 = hwnd;
                    _lastObsCrashDialogActionUtcV220 = DateTime.UtcNow;
                    _ = SetForegroundWindow(hwnd);
                    _ = PostMessage(hwnd, WmKeyDown, new IntPtr(VkReturn), IntPtr.Zero);
                    _ = PostMessage(hwnd, WmKeyUp, new IntPtr(VkReturn), IntPtr.Zero);
                    FooterStatus.Text = "OBS recovered in Normal Mode · continuing setup automatically";
                    return false;
                }
                catch { return true; }
            }, IntPtr.Zero);
        }
        catch { }
    }

    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const int VkReturn = 0x0D;

    private delegate bool ObsCrashEnumWindowsProcV220(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(ObsCrashEnumWindowsProcV220 lpEnumFunc, IntPtr lParam);

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
