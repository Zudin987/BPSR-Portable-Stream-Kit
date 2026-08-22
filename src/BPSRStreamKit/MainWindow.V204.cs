using System.IO;
using System.Windows;

namespace BPSRStreamKit;

public partial class MainWindow
{
    private async void CheckAvatarV204_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _selectedAvatar != AvatarMode.VTubeStudio) return;
        if (_streamActive)
        {
            ShowProblem("Avatar check is unavailable while live", "Stop the current share/stream before running the avatar test. Your live OBS session will not be restarted.");
            return;
        }

        try
        {
            HideProblem();
            SetBusy(true);
            await _setup.EnsureReadyAsync(needSpout: true);
            await _vTubeStudio.LaunchAndWaitAsync();

            // Avatar validation should not require a game to be running. The preview launches the
            // StreamKit horizontal collection directly and tests only the VTube Studio Spout source.
            _obs.LaunchAvatarPreview(_selectedTheme);
            await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(35));
            await Task.Delay(1000);
            MinimizePortableObsWindow();

            if (!await CheckAvatarConnectionAsync(TimeSpan.FromSeconds(25)))
            {
                try { if (File.Exists(AvatarVerifiedFile)) File.Delete(AvatarVerifiedFile); } catch { }
                throw new InvalidOperationException("Your avatar is not visible yet. Re-check the VTube Studio avatar steps and try again.");
            }

            AvatarCheckText.Text = "Avatar connected ✓  You can start sharing now.";
            FooterStatus.Text = "Avatar connected · ready to start";
        }
        catch (Exception ex)
        {
            try { if (File.Exists(AvatarVerifiedFile)) File.Delete(AvatarVerifiedFile); } catch { }
            ShowProblem("Avatar not connected yet", FriendlyError(ex));
        }
        finally
        {
            _obs.Stop();
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            SetBusy(false);
            await RefreshStatusAsync();
        }
    }
}
