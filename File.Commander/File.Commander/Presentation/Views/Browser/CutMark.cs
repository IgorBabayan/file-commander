using Avalonia;
using Avalonia.Controls;
using File.Commander.Presentation.Services;

namespace File.Commander.Presentation.Views.Browser;

/// <summary>
/// Gives an item container of a view the <c>cut</c> class while its file is cut (Ctrl+X) and not pasted yet,
/// so the views draw it with a lighter background. A style sets <see cref="PathProperty"/> on the containers;
/// containers in a window follow <see cref="FileClipboard.CutChanged"/>, recycled ones their new path.
/// </summary>
public static class CutMark
{
    public const string CutClass = "cut";

    /// <summary>The full path of the container's file. Null: never marked (placeholders).</summary>
    public static readonly AttachedProperty<string?> PathProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Path", typeof(CutMark));

    // Containers with a path that are in a window: the ones to update when the cut files change
    private static readonly HashSet<Control> Tracked = [];

    static CutMark()
    {
        PathProperty.Changed.AddClassHandler<Control>(OnPathChanged);
        FileClipboard.CutChanged += (_, _) =>
        {
            foreach (var control in Tracked)
                Update(control);
        };
    }

    public static string? GetPath(Control control) => control.GetValue(PathProperty);

    public static void SetPath(Control control, string? value) => control.SetValue(PathProperty, value);

    private static void OnPathChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is null && e.NewValue is not null)
        {
            control.AttachedToVisualTree += OnAttached;
            control.DetachedFromVisualTree += OnDetached;
            if (TopLevel.GetTopLevel(control) is not null)
                Tracked.Add(control);
        }
        else if (e.OldValue is not null && e.NewValue is null)
        {
            control.AttachedToVisualTree -= OnAttached;
            control.DetachedFromVisualTree -= OnDetached;
            Tracked.Remove(control);
        }

        Update(control);
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control)
            return;

        Tracked.Add(control);

        // Cut or pasted while it was out of the window
        Update(control);
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
            Tracked.Remove(control);
    }

    private static void Update(Control control)
        => control.Classes.Set(CutClass, FileClipboard.IsCut(GetPath(control)));
}
