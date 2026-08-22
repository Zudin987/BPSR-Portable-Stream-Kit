using System.Windows;
using System.Windows.Controls;

namespace BPSRStreamKit;

public partial class QuickLaunchCard : UserControl
{
    public event RoutedEventHandler? StartRequested;
    public event RoutedEventHandler? CustomizeRequested;

    public QuickLaunchCard()
    {
        InitializeComponent();
    }

    public void SetState(
        string game,
        string status,
        string avatar,
        string theme,
        string destination,
        string actionLabel,
        string actionHint,
        bool actionEnabled)
    {
        GameText.Text = game;
        StatusText.Text = status;
        AvatarText.Text = avatar;
        ThemeText.Text = theme;
        DestinationText.Text = destination;
        StartButton.Content = actionLabel;
        ActionHint.Text = actionHint;
        StartButton.IsEnabled = actionEnabled;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) => StartRequested?.Invoke(this, e);
    private void CustomizeButton_Click(object sender, RoutedEventArgs e) => CustomizeRequested?.Invoke(this, e);
}
