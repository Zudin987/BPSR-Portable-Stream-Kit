using System.Windows;
using System.Windows.Controls;

namespace BPSRStreamKit;

public partial class MainWindow
{
    // One class-level router runs before the older XAML click handlers. This lets v2.2
    // replace the fragile start/stop/setup paths without allowing both handlers to fire.
    private static readonly bool PlatformSetupGuardRegistered = RegisterPlatformSetupGuard();

    private static bool RegisterPlatformSetupGuard()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(PlatformSetupButtonClassHandler));
        return true;
    }

    private static async void PlatformSetupButtonClassHandler(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (Window.GetWindow(button) is not MainWindow window) return;

        Func<Task>? action = null;
        string? failureTitle = null;

        if (ReferenceEquals(button, window.MainActionButton))
        {
            action = window.StartV220Async;
            failureTitle = "StreamKit couldn’t start";
        }
        else if (ReferenceEquals(button, window.StartingSoonButton))
        {
            action = () => window.SwitchSceneV220Async("Starting Soon");
            failureTitle = "Couldn’t switch scenes";
        }
        else if (ReferenceEquals(button, window.BrbButton))
        {
            action = () => window.SwitchSceneV220Async("BRB");
            failureTitle = "Couldn’t switch scenes";
        }
        else if (ReferenceEquals(button, window.LiveButton))
        {
            action = () => window.SwitchSceneV220Async("Game Clean");
            failureTitle = "Couldn’t switch scenes";
        }
        else if (window._bpsrSceneButtonV220 is not null && ReferenceEquals(button, window._bpsrSceneButtonV220))
        {
            action = () => window.SwitchSceneV220Async("BPSR");
            failureTitle = "Couldn’t switch scenes";
        }
        else if (ReferenceEquals(button, window.StopStreamButton))
        {
            action = window.StopV220Async;
            failureTitle = "Couldn’t stop cleanly";
        }
        else if (ReferenceEquals(button, window.CheckAvatarButton))
        {
            action = window.CheckAvatarV220Async;
            failureTitle = "Avatar check needs attention";
        }
        else
        {
            var label = button.Content?.ToString();
            if (string.Equals(label, "Check TikTok", StringComparison.Ordinal)
                || string.Equals(label, "I’m finished", StringComparison.Ordinal)
                || string.Equals(label, "I'm finished", StringComparison.Ordinal))
            {
                action = window.CheckTikTokV220Async;
                failureTitle = "Couldn’t check TikTok";
            }
            else if (string.Equals(label, "Open account setup", StringComparison.Ordinal)
                     || string.Equals(label, "Open streaming engine", StringComparison.Ordinal))
            {
                action = window.OpenObsSetupV220Async;
                failureTitle = "Couldn’t open the streaming engine";
            }
        }

        if (action is null) return;

        // Stop the legacy instance click handler. Only the v2.2 controller may perform the action.
        e.Handled = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            try
            {
                window.ShowProblem(failureTitle ?? "Something needs attention", FriendlyError(ex));
                window.FooterStatus.Text = "StreamKit kept the current setup safe";
                window.SetBusy(false);
                window.UpdateV220ControlsUi();
            }
            catch { }
        }
    }
}
