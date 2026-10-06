using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace File.Commander.Themes;

/// <summary>
/// The settings page: one check box per theme. Built in code, so the plugin needs no XAML compiler. It takes the
/// Settings window's own classes ("keymap-hint", "setting"), so it looks like the rest of the window in any theme.
/// DataContext: <see cref="ThemesSettingsViewModel"/>.
/// </summary>
public sealed class ThemesSettingsView : UserControl
{
    private readonly StackPanel _list = new() { Spacing = 2 };

    public ThemesSettingsView()
    {
        var hint = new TextBlock { Text = "Themes to list under Basic → Appearance → Theme:" };
        hint.Classes.Add("keymap-hint");

        Content = new StackPanel { Children = { hint, _list } };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _list.Children.Clear();
        if (DataContext is not ThemesSettingsViewModel viewModel)
            return;

        foreach (var toggle in viewModel.Themes)
            _list.Children.Add(CheckBoxFor(toggle));
    }

    /// <summary>Kept in step both ways by hand: the host loads the values after the view is created.</summary>
    private static CheckBox CheckBoxFor(ThemeToggle toggle)
    {
        var checkBox = new CheckBox
        {
            Content = $"{toggle.Title} ({(toggle.IsDark ? "dark" : "light")})",
            IsChecked = toggle.IsShown,
        };
        checkBox.Classes.Add("setting");

        checkBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                toggle.IsShown = checkBox.IsChecked == true;
        };

        toggle.PropertyChanged += (_, _) =>
        {
            if (checkBox.IsChecked != toggle.IsShown)
                checkBox.IsChecked = toggle.IsShown;
        };

        return checkBox;
    }
}
