using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BPSRStreamKit.Infrastructure;
using BPSRStreamKit.Models;

namespace BPSRStreamKit;

public partial class MainWindow
{
    private QuickLaunchCard? _quickLaunchCard;
    private FrameworkElement? _quickSetupLabel;
    private FrameworkElement? _quickSetupGrid;
    private Button? _quickReturnButton;
    private TextBlock? _quickHeaderSubtitle;
    private DispatcherTimer? _quickUiTimer;
    private bool _quickUiInitialized;
    private bool _quickReturningState;
    private bool _quickMode;

    private static string QuickLaunchMarkerFile => Path.Combine(AppPaths.Root, "user-data", "returning-user-v1.txt");

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(QuickLaunchWindowLoaded));
    }

    private static void QuickLaunchWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._quickUiInitialized) return;
        window.Dispatcher.BeginInvoke(new Action(window.InitializeQuickLaunchUi), DispatcherPriority.ContextIdle);
    }

    private void InitializeQuickLaunchUi()
    {
        if (_quickUiInitialized) return;

        var root = FindMainStackPanel();
        if (root is null) return;

        _quickSetupLabel = root.Children
            .OfType<TextBlock>()
            .FirstOrDefault(x => string.Equals(x.Text, "GET READY IN 3 STEPS", StringComparison.Ordinal));

        if (_quickSetupLabel is null) return;
        var setupIndex = root.Children.IndexOf(_quickSetupLabel);
        if (setupIndex < 0 || setupIndex + 1 >= root.Children.Count || root.Children[setupIndex + 1] is not Grid setupGrid) return;
        _quickSetupGrid = setupGrid;

        _quickLaunchCard = new QuickLaunchCard();
        _quickLaunchCard.StartRequested += async (_, _) => await StartAsync();
        _quickLaunchCard.CustomizeRequested += (_, _) =>
        {
            _quickMode = false;
            ApplyQuickLaunchLayout();
        };
        root.Children.Insert(setupIndex, _quickLaunchCard);

        if (root.Children.Count > 0 && root.Children[0] is Grid header)
        {
            var headerButtons = header.Children
                .OfType<StackPanel>()
                .FirstOrDefault(x => Grid.GetColumn(x) == 1);
            if (headerButtons is not null)
            {
                _quickReturnButton = new Button
                {
                    Content = "Quick Launch",
                    Margin = new Thickness(4, 0, 0, 0),
                    ToolTip = "Return to the compact one-click view using your saved choices."
                };
                _quickReturnButton.Style = (Style)FindResource("GhostButton");
                _quickReturnButton.Click += (_, _) =>
                {
                    _quickMode = true;
                    ApplyQuickLaunchLayout();
                };
                var insertAt = Math.Max(0, headerButtons.Children.Count - 1);
                headerButtons.Children.Insert(insertAt, _quickReturnButton);
            }
        }

        _quickHeaderSubtitle = FindDescendant<TextBlock>(root,
            x => string.Equals(x.Text, "Three steps. No streaming software knowledge needed.", StringComparison.Ordinal));

        var versionLabel = FindDescendant<TextBlock>(root,
            x => x.Text?.StartsWith("v2.", StringComparison.OrdinalIgnoreCase) == true);
        if (versionLabel is not null)
        {
            var version = typeof(MainWindow).Assembly.GetName().Version;
            versionLabel.Text = version is null ? "v2.1.0" : $"v{version.Major}.{version.Minor}.{version.Build}";
        }

        _quickReturningState = HasReturningUserState();
        _quickMode = _quickReturningState;
        _quickUiInitialized = true;

        _quickUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _quickUiTimer.Tick += (_, _) => RefreshQuickLaunchUi();
        _quickUiTimer.Start();
        Closed += (_, _) => _quickUiTimer?.Stop();

        RefreshQuickLaunchUi();
    }

    private StackPanel? FindMainStackPanel()
    {
        if (Content is not Grid rootGrid) return null;
        var scroller = rootGrid.Children.OfType<ScrollViewer>().FirstOrDefault();
        return scroller?.Content as StackPanel;
    }

    private bool HasReturningUserState()
    {
        if (File.Exists(QuickLaunchMarkerFile)) return true;

        try
        {
            var rememberedGame = _catalog.GetLastSelectedProcess();
            return !string.IsNullOrWhiteSpace(rememberedGame) && !string.IsNullOrWhiteSpace(AppPaths.FindObsExe());
        }
        catch
        {
            return false;
        }
    }

    private void RefreshQuickLaunchUi()
    {
        if (!_quickUiInitialized || _quickLaunchCard is null) return;

        if (_streamActive && !_quickReturningState)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(QuickLaunchMarkerFile)!);
                File.WriteAllText(QuickLaunchMarkerFile, "StreamKit returning-user quick launch is enabled after a successful share/stream.");
            }
            catch { }

            _quickReturningState = true;
            _quickMode = true;
        }

        ApplyQuickLaunchLayout();

        var gameName = SelectedGame?.DisplayName ?? "Choose a game";
        var status = string.IsNullOrWhiteSpace(HeroTitle.Text) ? "Your saved setup is ready." : HeroTitle.Text;
        var avatar = SelectedAvatarChoice?.DisplayName ?? "Full VTuber";
        var theme = SelectedThemeChoice?.DisplayName ?? "Sakura";
        var destination = _selectedMode == StreamMode.DiscordOnly ? "Discord" : "Discord + Twitch + TikTok";
        var actionLabel = MainActionButton.Content?.ToString() ?? GetActionLabel();
        var actionHint = string.IsNullOrWhiteSpace(ActionHint.Text)
            ? "StreamKit will reuse your saved setup."
            : ActionHint.Text;

        _quickLaunchCard.SetState(
            gameName,
            status,
            avatar,
            theme,
            destination,
            actionLabel,
            actionHint,
            !_busy && !_streamActive && !_platformSetupNeeded);
    }

    private void ApplyQuickLaunchLayout()
    {
        if (!_quickUiInitialized || _quickLaunchCard is null || _quickSetupLabel is null || _quickSetupGrid is null) return;

        var showQuick = _quickReturningState && _quickMode && !_streamActive && !_platformSetupNeeded;
        var showSetup = !_streamActive && !_platformSetupNeeded && (!_quickReturningState || !_quickMode);

        _quickLaunchCard.Visibility = showQuick ? Visibility.Visible : Visibility.Collapsed;
        _quickSetupLabel.Visibility = showSetup ? Visibility.Visible : Visibility.Collapsed;
        _quickSetupGrid.Visibility = showSetup ? Visibility.Visible : Visibility.Collapsed;

        if (_quickReturnButton is not null)
            _quickReturnButton.Visibility = _quickReturningState && !_quickMode && !_streamActive && !_platformSetupNeeded
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (_quickHeaderSubtitle is not null)
        {
            _quickHeaderSubtitle.Text = showQuick
                ? "Your saved setup is ready in one click."
                : (_quickReturningState ? "Adjust anything, then return to Quick Launch when ready." : "Three steps. No streaming software knowledge needed.");
        }
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match)) return match;
            var nested = FindDescendant(child, predicate);
            if (nested is not null) return nested;
        }
        return null;
    }
}
