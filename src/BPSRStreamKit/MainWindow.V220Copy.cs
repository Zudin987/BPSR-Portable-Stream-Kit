using System.Windows;
using System.Windows.Threading;
using BPSRStreamKit.Models;

namespace BPSRStreamKit;

public partial class MainWindow
{
    private bool _v220CopyHooked;
    private static readonly bool V220CopyHookRegistered = RegisterV220CopyHook();

    private static bool RegisterV220CopyHook()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(V220CopyWindowLoaded));
        return true;
    }

    private static void V220CopyWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._v220CopyHooked) return;
        window._v220CopyHooked = true;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            window.ApplyV220ActionCopy();
            window._statusTimer.Tick += (_, _) => window.ApplyV220ActionCopy();
            window.AllPlatformsSegment.Click += (_, _) =>
                window.Dispatcher.BeginInvoke(new Action(window.ApplyV220ActionCopy), DispatcherPriority.ContextIdle);
            window.DiscordOnlySegment.Click += (_, _) =>
                window.Dispatcher.BeginInvoke(new Action(window.ApplyV220ActionCopy), DispatcherPriority.ContextIdle);
        }), DispatcherPriority.ContextIdle);
    }

    private void ApplyV220ActionCopy()
    {
        if (_streamActive) return;

        if (_selectedMode == StreamMode.AllPlatforms)
        {
            var hasTikTok = _setup.HasAitumStreamOutput();
            MainActionButton.Content = hasTikTok ? "Go Live Everywhere" : "Start Twitch + Discord";
            ActionHint.Text = hasTikTok
                ? "StreamKit keeps Discord, Twitch and the vertical TikTok layout together."
                : "TikTok is optional. Start Twitch + Discord now, then add your TikTok key later.";
        }
        else
        {
            MainActionButton.Content = "Start Discord Share";
            ActionHint.Text = "StreamKit opens one clean window. Share that window in Discord with sound.";
        }
    }
}
