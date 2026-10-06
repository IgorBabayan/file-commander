using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace File.Commander.Presentation.Views.Settings;

/// <summary>
/// Picking a navigation entry scrolls to its heading; scrolling selects the entry
/// whose heading is at the top. DataContext: <see cref="SettingsViewModel"/>.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>A heading this close below the top already counts as "at the top".</summary>
    private const double AnchorSlack = 12;

    private Dictionary<string, Control>? _anchors;
    private bool _scrollingToAnchor;
    private bool _syncingNavigation;

    public SettingsWindow()
    {
        InitializeComponent();

        Navigation.SelectionChanged += OnNavigationSelectionChanged;
        ContentScroll.ScrollChanged += OnContentScrollChanged;

        // Tunnel: a recording keymap row gets the keys before Esc closes the window or Tab moves focus
        AddHandler(KeyDownEvent, OnRecordingKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        KeymapGroups.AddHandler(PointerPressedEvent, OnKeymapPointerPressed, RoutingStrategies.Tunnel);
        KeymapGroups.AddHandler(DoubleTappedEvent, OnKeymapDoubleTapped);
        // Tunnel: the first Esc clears the search before the window sees it and closes
        KeymapSearch.AddHandler(KeyDownEvent, OnKeymapSearchKeyDown, RoutingStrategies.Tunnel);
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    /// <summary>
    /// Opened at a section (e.g. "Trash settings" on the sidebar): once the content is laid out, scrolls to it
    /// and selects it again, in case the first layout's scroll event moved the selection back to the top.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (ViewModel?.InitialNavItem is not { } item)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (ViewModel is not { } vm)
                return;

            ScrollTo(item);

            _syncingNavigation = true;
            try
            {
                vm.SelectedNavItem = item;
            }
            finally
            {
                _syncingNavigation = false;
            }
        }, DispatcherPriority.Loaded);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled)
            return;

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>While a keymap row records, every key goes to it: Esc cancels, anything else is the new shortcut.</summary>
    private void OnRecordingKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel?.Keymap is not { IsRecording: true } keymap)
            return;

        e.Handled = true;

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
            keymap.CancelRecording();
        else
            keymap.Capture(e.Key, e.KeyModifiers);
    }

    /// <summary>Esc in a non-empty search box clears it; Esc in an empty one closes the window as usual.</summary>
    private void OnKeymapSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None
            || ViewModel?.Keymap is not { } keymap || string.IsNullOrEmpty(keymap.SearchText))
            return;

        keymap.ClearSearchCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>A click anywhere stops recording; the context menu starts it only after its own click.</summary>
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
        => ViewModel?.Keymap.CancelRecording();

    /// <summary>Left or right click selects the row, so the context menu visibly acts on it.</summary>
    private void OnKeymapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm && KeymapItemAt(e.Source) is { } item)
            vm.Keymap.Selected = item;
    }

    private void OnKeymapDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (KeymapItemAt(e.Source) is not { } item)
            return;

        item.EditCommand.Execute(null);
        e.Handled = true;
    }

    // Every element of a row inherits the row's DataContext; the key caps' own is the key text
    private static KeymapItemViewModel? KeymapItemAt(object? source)
        => (source as Visual)?.GetSelfAndVisualAncestors()
            .OfType<StyledElement>()
            .Select(element => element.DataContext)
            .OfType<KeymapItemViewModel>()
            .FirstOrDefault();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) => Close();

    private void OnNavigationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // The selection followed the scroll position: scrolling back would fight the user
        if (_syncingNavigation || Navigation.SelectedItem is not SettingsNavItem item)
            return;

        ScrollTo(item);
    }

    /// <summary>Puts the heading of <paramref name="item"/> at the top, or as close to it as the content allows.</summary>
    private void ScrollTo(SettingsNavItem item)
    {
        if (TopOf(item.Key) is not { } top)
            return;

        var max = Math.Max(0, ContentScroll.Extent.Height - ContentScroll.Viewport.Height);

        // The last headings can't reach the top; don't let the scroll handler pick another entry for them
        _scrollingToAnchor = true;
        ContentScroll.Offset = new Vector(0, Math.Clamp(top, 0, max));
        Dispatcher.UIThread.Post(() => _scrollingToAnchor = false, DispatcherPriority.Background);
    }

    private void OnContentScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollingToAnchor || ViewModel is not { } vm)
            return;

        var offset = ContentScroll.Offset.Y;
        var max = ContentScroll.Extent.Height - ContentScroll.Viewport.Height;

        SettingsNavItem? current = null;
        if (offset > 0 && offset >= max - 1)
        {
            // Scrolled to the end: the last section is the one being read
            current = vm.NavItems[^1];
        }
        else
        {
            foreach (var item in vm.NavItems)
            {
                if (TopOf(item.Key) is { } top && top <= offset + AnchorSlack)
                    current = item;
            }
        }

        if (current is null || ReferenceEquals(current, vm.SelectedNavItem))
            return;

        _syncingNavigation = true;
        try
        {
            vm.SelectedNavItem = current;
        }
        finally
        {
            _syncingNavigation = false;
        }
    }

    /// <summary>Scroll offset that puts the heading tagged <paramref name="key"/> at the top.</summary>
    private double? TopOf(string key)
    {
        // The plugins' headings come from templates: looked up again once they exist, or after one was removed
        if (_anchors is null || !_anchors.TryGetValue(key, out var anchor) || TopLevel.GetTopLevel(anchor) is null)
        {
            _anchors = FindAnchors();
            if (!_anchors.TryGetValue(key, out anchor))
                return null;
        }

        return anchor.TranslatePoint(default, ContentHost)?.Y;
    }

    /// <summary>The group and section headings, by the Tag that is the Key of their navigation entry.</summary>
    private Dictionary<string, Control> FindAnchors()
    {
        var anchors = new Dictionary<string, Control>(StringComparer.Ordinal);
        foreach (var heading in ContentHost.GetVisualDescendants().OfType<TextBlock>())
        {
            if (heading.Tag is string key
                && (heading.Classes.Contains("group-title") || heading.Classes.Contains("section-title")))
                anchors.TryAdd(key, heading);
        }

        return anchors;
    }
}
