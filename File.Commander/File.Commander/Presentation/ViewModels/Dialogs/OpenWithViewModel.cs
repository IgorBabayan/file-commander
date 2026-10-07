using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace File.Commander.Presentation.ViewModels.Dialogs;

/// <summary>One app of the Open with… dialog. Its icon arrives once it was read off the UI thread.</summary>
public sealed partial class OpenWithAppViewModel : ObservableObject
{
    public OpenWithAppViewModel(DesktopApp app) => App = app;

    public DesktopApp App { get; }

    public string Name => App.Name;

    /// <summary>The app's own icon, when its theme has it as a PNG or SVG; else <see cref="HasIcon"/> is false.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    public partial Bitmap? Icon { get; set; }

    public bool HasIcon => Icon is not null;
}

/// <summary>A row of the list: an app, and the title of the section it starts, if it starts one.</summary>
public sealed record OpenWithRow(OpenWithAppViewModel App, string? Section)
{
    public bool HasSection => Section is not null;
}

/// <summary>
/// Open with…, as GNOME Files has it: the app a double click uses, the apps made for the file's type, then every other
/// app, filtered by what is typed in the search box. "Always use for this file type" makes the app picked the type's
/// default. The dialog returns true with <see cref="SelectedApp"/> on Open, false on Cancel, Esc or a click outside.
/// </summary>
public sealed partial class OpenWithViewModel : ViewModelBase
{
    private const int IconPixels = 48;

    private readonly IReadOnlyList<OpenWithAppViewModel> _default;
    private readonly IReadOnlyList<OpenWithAppViewModel> _recommended;
    private readonly IReadOnlyList<OpenWithAppViewModel> _others;
    private readonly CancellationTokenSource _iconLoading = new();
    private bool _disposed;

    public OpenWithViewModel(string fileName, OpenWithChoices choices)
    {
        Message = $"Choose an app to open “{fileName}”";
        TypeDescription = choices.Description ?? choices.Type;

        _default = choices.Default is { } current ? [new OpenWithAppViewModel(current)] : [];
        _recommended = choices.Recommended.Select(app => new OpenWithAppViewModel(app)).ToList();
        _others = choices.Others.Select(app => new OpenWithAppViewModel(app)).ToList();

        Rebuild();
        SelectedRow = Rows.FirstOrDefault();
        LoadIcons();
    }

    /// <summary>Raised with the dialog's result. The window closes itself.</summary>
    public event Action<bool>? CloseRequested;

    public string Message { get; }

    /// <summary>Under "Always use for this file type": "Zip archive", or the MIME type when it has no name.</summary>
    public string TypeDescription { get; }

    public ObservableCollection<OpenWithRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial OpenWithRow? SelectedRow { get; set; }

    /// <summary>The app picked, once the dialog returned true.</summary>
    public DesktopApp? SelectedApp => SelectedRow?.App.App;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>"Always use for this file type": the app picked becomes the type's default.</summary>
    [ObservableProperty]
    public partial bool AlwaysUse { get; set; }

    /// <summary>Nothing matches what was typed.</summary>
    public bool HasNoMatches => Rows.Count == 0;

    partial void OnSearchTextChanged(string value)
    {
        var selected = SelectedRow?.App;
        Rebuild();

        // The same app stays selected while it is listed; else the first one that is
        SelectedRow = Rows.FirstOrDefault(row => row.App == selected) ?? Rows.FirstOrDefault();
    }

    /// <summary>The rows matching <see cref="SearchText"/>; each section titled on its first row.</summary>
    private void Rebuild()
    {
        var search = SearchText.Trim();
        Rows.Clear();

        AddSection("Current default", _default, search);
        AddSection("Recommended apps", _recommended, search);
        AddSection("Other apps", _others, search);

        OnPropertyChanged(nameof(HasNoMatches));
    }

    private void AddSection(string title, IReadOnlyList<OpenWithAppViewModel> apps, string search)
    {
        var first = true;
        foreach (var app in apps)
        {
            if (search.Length > 0 && !Matches(app.App, search))
                continue;

            Rows.Add(new OpenWithRow(app, first ? title : null));
            first = false;
        }
    }

    /// <summary>By the name, or the desktop file ID ("org.kde.kate" is found by "kate").</summary>
    private static bool Matches(DesktopApp app, string search)
        => app.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
           || app.Id.Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>The apps' icons, read and decoded off the UI thread, shown as they arrive: the list is usable at once.</summary>
    private void LoadIcons()
    {
        var apps = _default.Concat(_recommended).Concat(_others).Where(app => app.App.Icon is not null).ToList();
        var token = _iconLoading.Token;

        _ = Task.Run(() =>
        {
            foreach (var app in apps)
            {
                if (token.IsCancellationRequested)
                    return;

                if (Decode(app.App.Icon!) is not { } bitmap)
                    continue;

                Dispatcher.UIThread.Post(() =>
                {
                    // The dialog closed meanwhile: nobody shows it
                    if (_disposed)
                        bitmap.Dispose();
                    else
                        app.Icon = bitmap;
                });
            }
        }, token);
    }

    // PNG or SVG; a broken file in an icon theme gives null and the app keeps the default icon
    private static Bitmap? Decode(string icon)
        => ThemeIcons.Find(icon, IconPixels) is { } path ? IconBitmaps.Decode(path, IconPixels) : null;

    private bool CanOpen() => SelectedRow is not null;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    protected override void OnDispose()
    {
        _disposed = true;
        _iconLoading.Cancel();
        _iconLoading.Dispose();

        foreach (var app in _default.Concat(_recommended).Concat(_others))
        {
            app.Icon?.Dispose();
            app.Icon = null;
        }
    }
}
