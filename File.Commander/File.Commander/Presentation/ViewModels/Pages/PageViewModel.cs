using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Pages;

/// <summary>Implemented by the shell. Pages call it to open another location.</summary>
public interface INavigator
{
    void Navigate(string location);
}

/// <summary>What the content area shows. One instance per visit: it is disposed when you navigate away.</summary>
public abstract class PageViewModel : ViewModelBase
{
    /// <summary>A normalized filesystem path or a virtual location (see <see cref="Services.Locations"/>).</summary>
    public abstract string Location { get; }

    /// <summary>Shown in the title bar.</summary>
    public abstract string Title { get; }

    /// <summary>Shown in the status bar.</summary>
    public virtual string StatusText => string.Empty;
}

/// <summary>For virtual locations that aren't implemented yet.</summary>
public sealed class PlaceholderPageViewModel(string location, string title, MaterialIconKind icon) : PageViewModel
{
    public override string Location { get; } = location;
    public override string Title { get; } = title;
    public MaterialIconKind Icon { get; } = icon;
    public string Message => $"{Title} isn't available yet.";
}
