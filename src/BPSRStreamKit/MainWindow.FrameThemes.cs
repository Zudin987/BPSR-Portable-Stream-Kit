using System.Windows;
using System.Windows.Controls;
using BPSRStreamKit.Services;

namespace BPSRStreamKit;

public partial class MainWindow
{
    private bool _frameThemeCatalogInitialized;
    private string _activeFrameThemeKey = "sakura";
    private IReadOnlyList<ThemeChoice> _expandedThemeChoices = Array.Empty<ThemeChoice>();

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(FrameThemeWindowLoaded));
    }

    private static void FrameThemeWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.InitializeExpandedFrameThemes();
    }

    private void InitializeExpandedFrameThemes()
    {
        if (_frameThemeCatalogInitialized) return;
        _frameThemeCatalogInitialized = true;

        _expandedThemeChoices = FrameThemeService.Definitions
            .Select(x => new ThemeChoice(x.LegacyTheme, x.DisplayName, x.Detail))
            .ToArray();

        var desired = FrameThemeService.Find(FrameThemeService.ReadSelectionKey()) ?? FrameThemeService.Default;
        Exception? activationError = null;
        try
        {
            FrameThemeService.Activate(desired);
        }
        catch (Exception ex)
        {
            activationError = ex;
            desired = FrameThemeService.Default;
            try { FrameThemeService.Activate(desired); } catch { }
        }

        _loadingTheme = true;
        try
        {
            ThemeCombo.ItemsSource = _expandedThemeChoices;
            var selected = _expandedThemeChoices.FirstOrDefault(x =>
                               string.Equals(x.DisplayName, desired.DisplayName, StringComparison.Ordinal))
                           ?? _expandedThemeChoices[0];
            _selectedTheme = selected.Theme;
            ThemeCombo.SelectedItem = selected;
            _activeFrameThemeKey = desired.Key;
        }
        finally
        {
            _loadingTheme = false;
        }

        ThemeCombo.SelectionChanged += ExpandedFrameTheme_SelectionChanged;
        SavePreferences();
        RefreshQuickLaunch();

        if (activationError is not null)
        {
            ShowProblem(
                "Frame style fell back to Sakura",
                "The saved frame style could not be prepared. Sakura is still usable. Extract the complete release ZIP or use Fix setup if this keeps happening.",
                "Fix setup",
                RepairAsync);
        }
    }

    private void ExpandedFrameTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTheme || _streamActive || _recoveryPending || _busy) return;
        if (ThemeCombo.SelectedItem is not ThemeChoice choice) return;

        var selected = FrameThemeService.FindByDisplayName(choice.DisplayName);
        if (selected is null || string.Equals(selected.Key, _activeFrameThemeKey, StringComparison.OrdinalIgnoreCase)) return;

        var previous = FrameThemeService.Find(_activeFrameThemeKey) ?? FrameThemeService.Default;
        try
        {
            FrameThemeService.Activate(selected);
            _activeFrameThemeKey = selected.Key;
            _selectedTheme = selected.LegacyTheme;
            SavePreferences();
            HideProblem();
            FooterStatus.Text = $"Frame style saved · {selected.DisplayName}";
            RefreshQuickLaunch();
        }
        catch (Exception ex)
        {
            _loadingTheme = true;
            try
            {
                var previousChoice = _expandedThemeChoices.FirstOrDefault(x =>
                    string.Equals(x.DisplayName, previous.DisplayName, StringComparison.Ordinal));
                if (previousChoice is not null)
                {
                    ThemeCombo.SelectedItem = previousChoice;
                    _selectedTheme = previousChoice.Theme;
                }
            }
            finally
            {
                _loadingTheme = false;
            }

            SavePreferences();
            ShowProblem(
                "Couldn’t switch frame style",
                $"{selected.DisplayName} could not be generated or saved. StreamKit kept {previous.DisplayName} active. {ex.Message}",
                "Fix setup",
                RepairAsync);
        }
    }
}
