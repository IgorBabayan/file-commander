using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>
/// The search bar (Ctrl+F): under the Search button of the title bar, or in a dialog while the button isn't on the
/// toolbar. What is typed and the options stay for the session; where to look is set to the active view's folder
/// each time it opens. Search opens the results in the active view (<see cref="SearchRequested"/>).
/// </summary>
public sealed partial class SearchViewModel : ViewModelBase
{
    private readonly Func<PageViewModel> _currentPage;
    private readonly Func<bool> _contentsByDefault;

    /// <param name="currentPage">The active view's page: the default place to look in.</param>
    /// <param name="contentsByDefault">Settings → Search → Full-text search: File contents starts checked.</param>
    public SearchViewModel(Func<PageViewModel> currentPage, Func<bool> contentsByDefault)
    {
        _currentPage = currentPage;
        _contentsByDefault = contentsByDefault;
        InContents = contentsByDefault();
        Folder = SystemLocations.HomeDirectory;

        Created.PropertyChanged += OnDateFilterChanged;
        Modified.PropertyChanged += OnDateFilterChanged;
    }

    /// <summary>Search was pressed: the shell shows the results in the active view.</summary>
    public event EventHandler<SearchQuery>? SearchRequested;

    /// <summary>The bar or the dialog should close: a search started, or Esc.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// A folder was picked with Browse…: the system's chooser took the focus, which may have closed the bar under the
    /// Search button, so it should show again (without being reset to the active view's folder).
    /// </summary>
    public event EventHandler? ReopenRequested;

    /// <summary>Looked for in names and/or file contents. Wildcards: * and ?.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool InNames { get; set; } = true;

    [ObservableProperty]
    public partial bool InContents { get; set; }

    /// <summary>"pdf, docx": only files with one of these extensions.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string Extensions { get; set; } = string.Empty;

    public SearchDateFilterViewModel Created { get; } = new();

    public SearchDateFilterViewModel Modified { get; } = new();

    /// <summary>Where to look. Always a real folder.</summary>
    [ObservableProperty]
    public partial string Folder { get; set; } = "/";

    [ObservableProperty]
    public partial bool IncludeSubfolders { get; set; } = true;

    /// <summary>
    /// Called each time the bar opens. In a folder: looks there. On the results of a search: shows that search, so it
    /// can be refined. Elsewhere (Recent, Trash…): in Home, or everywhere from the Computer page.
    /// </summary>
    public void Prepare()
    {
        var page = _currentPage();
        if (page is DirectoryViewModel { Search: { } search })
        {
            Load(search);
            return;
        }

        Folder = page.Location switch
        {
            Locations.Computer => "/",
            var location when Locations.IsVirtual(location) => SystemLocations.HomeDirectory,
            var location => location,
        };
    }

    /// <summary>What the options ask for, from <see cref="Folder"/>.</summary>
    public SearchQuery ToQuery() => new(
        Text.Trim(),
        InNames,
        InContents,
        SearchQuery.ParseExtensions(Extensions),
        Created.ToFilter(),
        Modified.ToFilter(),
        Folder,
        IncludeSubfolders);

    /// <summary>Enter in the bar. Needs something to look for: a text, an extension or a date.</summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private void Search()
    {
        var query = ToQuery();
        CloseRequested?.Invoke(this, EventArgs.Empty);
        SearchRequested?.Invoke(this, query);
    }

    private bool CanSearch() => ToQuery().HasCriteria;

    /// <summary>Esc.</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Puts every option back; keeps the text and the folder.</summary>
    [RelayCommand]
    private void ResetOptions()
    {
        InNames = true;
        InContents = _contentsByDefault();
        Extensions = string.Empty;
        Created.Reset();
        Modified.Reset();
        IncludeSubfolders = true;
    }

    [RelayCommand]
    private void UseCurrentFolder() => Prepare();

    [RelayCommand]
    private void UseHome() => Folder = SystemLocations.HomeDirectory;

    /// <summary>Everywhere: from the root of the filesystem.</summary>
    [RelayCommand]
    private void UseComputer() => Folder = "/";

    [RelayCommand]
    private async Task BrowseFolder()
    {
        var picked = await FolderPicker.PickAsync("Search in", Folder);
        if (picked is null)
            return;

        Folder = picked;
        ReopenRequested?.Invoke(this, EventArgs.Empty);
    }

    // At least one of them: unchecking the last one checks the other, as there would be nothing to match the text to
    partial void OnInNamesChanged(bool value)
    {
        if (!value && !InContents)
            InContents = true;
    }

    partial void OnInContentsChanged(bool value)
    {
        if (!value && !InNames)
            InNames = true;
    }

    private void Load(SearchQuery query)
    {
        Text = query.Text;
        InNames = true;
        InContents = query.InContents;
        InNames = query.InNames;
        Extensions = string.Join(", ", query.Extensions);
        Created.Load(query.Created);
        Modified.Load(query.Modified);
        Folder = query.Folder;
        IncludeSubfolders = query.IncludeSubfolders;
    }

    private void OnDateFilterChanged(object? sender, PropertyChangedEventArgs e)
        => SearchCommand.NotifyCanExecuteChanged();

    protected override void OnDispose()
    {
        Created.PropertyChanged -= OnDateFilterChanged;
        Modified.PropertyChanged -= OnDateFilterChanged;
        base.OnDispose();
    }
}

/// <summary>Created or Modified of the search bar: a range picked from a list, or two dates on the calendar.</summary>
public sealed partial class SearchDateFilterViewModel : ObservableObject
{
    private static readonly IReadOnlyList<Choice<SearchDateRange>> AllOptions =
    [
        new(SearchDateRange.Any, "Any time"),
        new(SearchDateRange.Today, "Today"),
        new(SearchDateRange.Yesterday, "Yesterday"),
        new(SearchDateRange.Last7Days, "Last 7 days"),
        new(SearchDateRange.Last30Days, "Last 30 days"),
        new(SearchDateRange.LastYear, "Last year"),
        new(SearchDateRange.Custom, "Custom range…"),
    ];

    // Never null: a drop-down whose items are replaced writes null, which is ignored
    private Choice<SearchDateRange> _range = AllOptions[0];

    public IReadOnlyList<Choice<SearchDateRange>> Options => AllOptions;

    public Choice<SearchDateRange> Range
    {
        get => _range;
        set
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (value is null || !SetProperty(ref _range, value))
                return;

            OnPropertyChanged(nameof(IsCustom));
            OnPropertyChanged(nameof(IsSet));
        }
    }

    /// <summary>The From and To calendars show only for a custom range.</summary>
    public bool IsCustom => Range.Value == SearchDateRange.Custom;

    /// <summary>First day, included.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSet))]
    public partial DateTime? From { get; set; }

    /// <summary>Last day, included.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSet))]
    public partial DateTime? To { get; set; }

    /// <summary>Filters something.</summary>
    public bool IsSet => ToFilter().IsSet;

    public SearchDateFilter ToFilter() => IsCustom
        ? new SearchDateFilter(SearchDateRange.Custom, DateOf(From), DateOf(To))
        : new SearchDateFilter(Range.Value);

    public void Load(SearchDateFilter filter)
    {
        Range = AllOptions.FirstOrDefault(option => option.Value == filter.Range) ?? AllOptions[0];
        From = filter.From?.ToDateTime(TimeOnly.MinValue);
        To = filter.To?.ToDateTime(TimeOnly.MinValue);
    }

    public void Reset() => Load(SearchDateFilter.Any);

    private static DateOnly? DateOf(DateTime? date) => date is { } value ? DateOnly.FromDateTime(value) : null;
}
