using System.Windows;
using System.Windows.Controls;

namespace BPSRStreamKit;

public partial class MainWindow
{
    // Register a Button class handler before MainWindow instances are constructed.
    // WPF invokes class handlers before XAML instance Click handlers, so this can
    // safely replace the fragile async-void "I'm finished" path without changing
    // the rest of the existing setup screen.
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

        var label = button.Content?.ToString();
        if (!string.Equals(label, "I’m finished", StringComparison.Ordinal)
            && !string.Equals(label, "I'm finished", StringComparison.Ordinal))
            return;

        if (Window.GetWindow(button) is not MainWindow window) return;

        // Prevent the older XAML async-void handler from also running.
        e.Handled = true;
        await window.FinishPlatformSetupSafelyAsync();
    }

    private async Task FinishPlatformSetupSafelyAsync()
    {
        if (_busy) return;

        try
        {
            HideProblem();
            SetBusy(true);
            FooterStatus.Text = "Checking Twitch / TikTok setup…";

            // Check while OBS is still open. If setup is incomplete, leave it open
            // so the user can fix the output immediately instead of reopening it.
            await Task.Delay(400);

            if (!_setup.HasAitumStreamOutput())
            {
                _platformSetupNeeded = true;
                PlatformSetupPanel.Visibility = Visibility.Visible;
                ShowProblem(
                    "TikTok setup is not finished yet",
                    "StreamKit still can’t find the TikTok Vertical output. Leave the account setup window open, finish the TikTok Vertical output, then click I’m finished again.");
                FooterStatus.Text = "Waiting for the TikTok Vertical output";
                RestorePortableObsWindow();
                return;
            }

            // Only close OBS after the configured output has been confirmed.
            try { _obs.Stop(); } catch { }
            await Task.Delay(500);

            _platformSetupNeeded = false;
            PlatformSetupPanel.Visibility = Visibility.Collapsed;
            HideProblem();
            FooterStatus.Text = "Accounts saved · press Go Live Everywhere when ready";
        }
        catch (Exception ex)
        {
            _platformSetupNeeded = true;
            PlatformSetupPanel.Visibility = Visibility.Visible;
            ShowProblem("Couldn’t check the account setup", FriendlyError(ex));
            FooterStatus.Text = "Account setup check failed · your settings were kept";
            try { RestorePortableObsWindow(); } catch { }
        }
        finally
        {
            try { SetBusy(false); } catch { }
            try { await RefreshStatusAsync(); } catch { }
        }
    }
}
