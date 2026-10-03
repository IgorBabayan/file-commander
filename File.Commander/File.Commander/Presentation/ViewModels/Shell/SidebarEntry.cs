using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Shell;

public abstract class SidebarEntry
{
    protected SidebarEntry(string title) => Title = title;
 
    public string Title { get; }
 
    /// <summary>Bound to ListBoxItem.IsEnabled so headers can't be selected or focused.</summary>
    public abstract bool IsSelectable { get; }
}
 
public sealed class SidebarHeader : SidebarEntry
{
    public SidebarHeader(string title) : base(title) { }
 
    public override bool IsSelectable => false;
}
 
public sealed class SidebarItem : SidebarEntry
{
    public SidebarItem(string title, MaterialIconKind icon, string location, bool canEject = false)
        : base(title)
    {
        Icon = icon;
        Location = location;
        CanEject = canEject;
    }
 
    public MaterialIconKind Icon { get; }
 
    /// <summary>A filesystem path, or a virtual location such as "computer://", "recent://", "trash://".</summary>
    public string Location { get; }
 
    public bool CanEject { get; }
 
    public override bool IsSelectable => true;
}