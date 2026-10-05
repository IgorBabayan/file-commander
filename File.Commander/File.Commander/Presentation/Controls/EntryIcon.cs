using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Material.Icons;
using Material.Icons.Avalonia;

namespace File.Commander.Presentation.Controls;

/// <summary>
/// A file or folder icon that turns into a thumbnail of the picture itself once it is decoded.
/// The icon inside is a <c>MaterialIcon.entry-icon</c> (plus <c>.folder</c>), so the views' icon styles
/// keep applying. Decoding runs in the background, at the size the icon is drawn at, and stops when
/// the control leaves the screen or is recycled for another entry. What can't be decoded keeps its icon.
/// </summary>
public sealed class EntryIcon : Panel
{
    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<EntryIcon, string?>(nameof(FilePath));

    public static readonly StyledProperty<MaterialIconKind> KindProperty =
        AvaloniaProperty.Register<EntryIcon, MaterialIconKind>(nameof(Kind), MaterialIconKind.FileOutline);

    public static readonly StyledProperty<bool> IsFolderProperty =
        AvaloniaProperty.Register<EntryIcon, bool>(nameof(IsFolder));

    // Used when neither Width nor Height is set
    private const double DefaultSize = 48;

    private readonly MaterialIcon _icon = new();
    private readonly Image _image = new() { Stretch = Stretch.Uniform, IsVisible = false };

    // Not disposed: a load may still be between awaits holding the token
    private CancellationTokenSource? _cts;
    private Bitmap? _thumbnail;

    public EntryIcon()
    {
        _icon.Classes.Add("entry-icon");
        _icon.Kind = Kind;
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.HighQuality);

        Children.Add(_icon);
        Children.Add(_image);
    }

    /// <summary>The file to preview. Null (tree placeholders) or anything that isn't a picture: the icon only.</summary>
    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    /// <summary>Shown until the thumbnail is ready, and instead of it when there is none.</summary>
    public MaterialIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Folders are never previewed, even when named "*.png".</summary>
    public bool IsFolder
    {
        get => GetValue(IsFolderProperty);
        set => SetValue(IsFolderProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Reload();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Clear();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == KindProperty)
        {
            _icon.Kind = Kind;
        }
        else if (change.Property == IsFolderProperty)
        {
            _icon.Classes.Set("folder", IsFolder);
            Reload();
        }
        else if (change.Property == FilePathProperty)
        {
            // Virtualized lists reuse the control for another entry
            Reload();
        }
        else if (change.Property == WidthProperty || change.Property == HeightProperty)
        {
            _icon.Width = Width;
            _icon.Height = Height;
            Reload();
        }
    }

    private void Reload()
    {
        Clear();

        if (VisualRoot is null || IsFolder || FilePath is not { } path || !ThumbnailLoader.CanLoad(path))
            return;

        var size = Math.Max(double.IsNaN(Width) ? 0 : Width, double.IsNaN(Height) ? 0 : Height);
        if (size <= 0)
            size = DefaultSize;

        // Sharp on HiDPI screens
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(size * scaling));

        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = LoadAsync(path, pixelWidth, cts); // never throws
    }

    /// <summary>UI thread. Swaps the icon for the thumbnail, unless the control moved on meanwhile.</summary>
    private async Task LoadAsync(string path, int pixelWidth, CancellationTokenSource cts)
    {
        var bitmap = await ThumbnailLoader.LoadAsync(path, pixelWidth, cts.Token);
        if (bitmap is null)
            return;

        if (cts.IsCancellationRequested || !ReferenceEquals(cts, _cts))
        {
            bitmap.Dispose();
            return;
        }

        _thumbnail = bitmap;
        _image.Source = bitmap;
        _image.IsVisible = true;
        _icon.IsVisible = false;
    }

    private void Clear()
    {
        _cts?.Cancel();
        _cts = null;

        _image.Source = null;
        _image.IsVisible = false;
        _icon.IsVisible = true;

        _thumbnail?.Dispose();
        _thumbnail = null;
    }
}
