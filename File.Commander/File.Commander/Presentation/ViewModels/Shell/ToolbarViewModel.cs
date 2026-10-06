using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Settings;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>An item the title bar's toolbar can show.</summary>
/// <param name="Id">Stored in <see cref="ToolbarSettings.Items"/>; the Tag of its control in MainWindow's ToolbarPool.</param>
/// <param name="Label">Shown under the item in the palette while customizing.</param>
/// <param name="Icon">Shown in the palette.</param>
/// <param name="IsRemovable">False: can be moved on the toolbar, never dragged off it (the address bar).</param>
/// <param name="Flex">Above 0: takes a share of the width the other items leave, by weight.</param>
/// <param name="MinWidth">Narrowest a flexible item gets.</param>
/// <param name="Group">Neighbors of the same group touch, forming one pill (back, forward, up).</param>
public sealed record ToolbarItemInfo(
    string Id,
    string Label,
    MaterialIconKind Icon,
    bool IsRemovable = true,
    double Flex = 0,
    double MinWidth = 0,
    string? Group = null);

/// <summary>One item on the toolbar. Compared by reference: the toolbar can show several flexible spaces.</summary>
public sealed class ToolbarEntry(ToolbarItemInfo info)
{
    public ToolbarItemInfo Info { get; } = info;

    public string Id => Info.Id;
}

/// <summary>
/// The title bar's items and Customize Toolbar…, as in Firefox: a right click on the title bar opens it, the
/// window's content gives way to a palette of the items that aren't on the toolbar, and items are dragged
/// between the two (MainWindow.Toolbar.cs). Every drop is stored; Restore Defaults puts back the built-in
/// layout and offers Undo until the next change; Done (or Esc) closes it.
/// </summary>
public sealed partial class ToolbarViewModel : ObservableObject, IDisposable
{
    public const string FlexibleSpaceId = "flexible-space";

    private const string NavigationGroup = "navigation";

    /// <summary>Every item, in palette order. Flexible Space is always in the palette: there can be any number.</summary>
    public static IReadOnlyList<ToolbarItemInfo> Catalog { get; } =
    [
        new("back", "Back", MaterialIconKind.ChevronLeft, Group: NavigationGroup),
        new("forward", "Forward", MaterialIconKind.ChevronRight, Group: NavigationGroup),
        new("up", "Up", MaterialIconKind.ArrowUp, Group: NavigationGroup),
        new("file-actions", "File actions", MaterialIconKind.ContentCopy),
        new("computer", "Computer", MaterialIconKind.Monitor),
        new("address", "Address bar", MaterialIconKind.FormTextbox, IsRemovable: false, Flex: 3, MinWidth: 200),
        new("sort", "Sort", MaterialIconKind.Sort),
        new("view-mode", "View mode", MaterialIconKind.ViewGridOutline),
        new("action-center", "Action center", MaterialIconKind.ProgressClock),
        new("search", "Search", MaterialIconKind.Magnify),
        new("refresh", "Refresh", MaterialIconKind.Refresh),
        new("new-tab", "New tab", MaterialIconKind.TabPlus),
        new("info-panel", "Info panel", MaterialIconKind.InformationOutline),
        new("hidden-files", "Hidden files", MaterialIconKind.EyeOutline),
        new(FlexibleSpaceId, "Flexible Space", MaterialIconKind.ArrowExpandHorizontal, Flex: 1),
    ];

    /// <summary>The built-in layout: the title bar as it was before it could be customized.</summary>
    public static IReadOnlyList<string> DefaultItems { get; } =
        ["back", "forward", "up", "file-actions", "computer", "address", "sort", "view-mode", "action-center", "search"];

    private readonly ISettingsService _settings;

    // Replaced on every change, never mutated: a drag keeps the list it started from
    private IReadOnlyList<ToolbarEntry> _entries;

    // What settings.json holds, so a drop that changes nothing isn't saved
    private IReadOnlyList<string> _savedItems;

    // The layout before Restore Defaults, for Undo
    private IReadOnlyList<ToolbarEntry>? _beforeRestore;

    public ToolbarViewModel(ISettingsService settings)
    {
        _settings = settings;
        _entries = Parse(settings.Current.Toolbar?.Items);
        _savedItems = IdsOf(_entries);
        UpdatePalette();

        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>The toolbar shows other items, or the same in another order.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>The items on the toolbar, left to right.</summary>
    public IReadOnlyList<ToolbarEntry> Entries => _entries;

    /// <summary>What the palette offers: the removable items that aren't on the toolbar, and Flexible Space.</summary>
    public ObservableCollection<ToolbarItemInfo> PaletteItems { get; } = [];

    /// <summary>Customize Toolbar… is open: the palette shows, the toolbar's items can be dragged.</summary>
    [ObservableProperty]
    public partial bool IsCustomizing { get; set; }

    /// <summary>Right after Restore Defaults: Undo takes its place until the next change.</summary>
    [ObservableProperty]
    public partial bool CanUndoRestore { get; set; }

    /// <summary>The title bar's context menu.</summary>
    [RelayCommand]
    private void Customize() => IsCustomizing = true;

    /// <summary>The Done button, or Esc.</summary>
    [RelayCommand]
    private void Done()
    {
        if (!IsCustomizing)
            return;

        Save();
        ClearUndo();
        IsCustomizing = false;
    }

    /// <summary>Puts back the built-in layout. Disabled while the toolbar already shows it.</summary>
    [RelayCommand(CanExecute = nameof(CanRestoreDefaults))]
    private void RestoreDefaults()
    {
        var before = _entries;
        SetEntries(Parse(null));
        Save();

        _beforeRestore = before;
        CanUndoRestore = true;
    }

    private bool CanRestoreDefaults() => !IdsOf(_entries).SequenceEqual(DefaultItems);

    /// <summary>Takes back Restore Defaults.</summary>
    [RelayCommand]
    private void UndoRestore()
    {
        if (_beforeRestore is not { } before)
            return;

        ClearUndo();
        SetEntries(before);
        Save();
    }

    /// <summary>Shows <paramref name="entries"/> while an item is dragged. Stored by <see cref="Commit"/>.</summary>
    public void Preview(IReadOnlyList<ToolbarEntry> entries) => SetEntries(entries);

    /// <summary>A drag ended on the toolbar or the palette: stores the layout. Undo of Restore Defaults is gone.</summary>
    public void Commit()
    {
        ClearUndo();
        Save();
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void SetEntries(IReadOnlyList<ToolbarEntry> entries)
    {
        if (entries.SequenceEqual(_entries))
            return;

        _entries = entries.ToList();
        UpdatePalette();
        RestoreDefaultsCommand.NotifyCanExecuteChanged();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearUndo()
    {
        _beforeRestore = null;
        CanUndoRestore = false;
    }

    private void UpdatePalette()
    {
        var shown = _entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        var items = Catalog
            .Where(info => info.IsRemovable && (info.Id == FlexibleSpaceId || !shown.Contains(info.Id)))
            .ToList();

        if (items.SequenceEqual(PaletteItems))
            return;

        PaletteItems.Clear();
        foreach (var info in items)
            PaletteItems.Add(info);
    }

    private void Save()
    {
        var items = IdsOf(_entries);
        if (items.SequenceEqual(_savedItems))
            return;

        _savedItems = items;
        var toolbar = new ToolbarSettings { Items = items.SequenceEqual(DefaultItems) ? null : items };
        _ = SaveAsync(_settings.Current with { Toolbar = toolbar });
    }

    private async Task SaveAsync(AppSettings settings)
    {
        try
        {
            await _settings.SaveAsync(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Can't save settings: {ex.Message}");
        }
    }

    /// <summary>Another window changed its toolbar: this one follows, unless it is being customized.</summary>
    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnSettingsChanged(sender, settings));
            return;
        }

        var stored = Parse(settings.Toolbar?.Items);
        var items = IdsOf(stored);
        if (items.SequenceEqual(_savedItems))
            return;

        _savedItems = items;
        if (!IsCustomizing)
            SetEntries(stored);
    }

    /// <summary>
    /// The stored ids as entries: unknown ids and repeats (other than flexible spaces) are dropped, and an item
    /// that can't be removed is put back at its default place.
    /// </summary>
    private static List<ToolbarEntry> Parse(IReadOnlyList<string>? items)
    {
        var result = new List<ToolbarEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in items ?? DefaultItems)
        {
            if (Find(id) is not { } info || (id != FlexibleSpaceId && !seen.Add(id)))
                continue;

            result.Add(new ToolbarEntry(info));
        }

        foreach (var info in Catalog.Where(info => !info.IsRemovable && !seen.Contains(info.Id)))
        {
            var at = 0;
            while (at < DefaultItems.Count && DefaultItems[at] != info.Id)
                at++;

            result.Insert(Math.Min(at, result.Count), new ToolbarEntry(info));
        }

        return result;
    }

    private static ToolbarItemInfo? Find(string id) => Catalog.FirstOrDefault(info => info.Id == id);

    private static List<string> IdsOf(IEnumerable<ToolbarEntry> entries) => entries.Select(entry => entry.Id).ToList();
}
