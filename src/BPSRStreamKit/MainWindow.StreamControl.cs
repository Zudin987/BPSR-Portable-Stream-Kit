using System.Windows;

namespace BPSRStreamKit;

public partial class MainWindow
{
    private async void StopStream_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        try
        {
            SetBusy(true);
            FooterStatus.Text = _obs.Stop()
                ? "Portable OBS closed"
                : "Portable OBS is not running";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"StreamKit couldn't close portable OBS.\n\n{ex.Message}",
                "Stop Stream", MessageBoxButton.OK, MessageBoxImage.Warning);
            FooterStatus.Text = "Could not close portable OBS";
        }
        finally
        {
            SetBusy(false);
            await RefreshStatusAsync();
        }
    }
}
