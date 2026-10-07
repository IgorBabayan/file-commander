using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Material.Icons;
using Material.Icons.Avalonia;

namespace File.Commander.Presentation.Controls;

/// <summary>
/// A file or folder icon, as the desktop draws it: the icon theme's icon for the entry's MIME type, and for a picture a
/// thumbnail of the picture itself once it is decoded. When the theme has no icon for the type, the Material glyph
/// <see cref="Kind"/> stays; that glyph is a <c>MaterialIcon.entry-icon</c> (plus <c>.folder</c>), so the views' icon
/// styles keep applying. Loading runs in the background, at the size the icon is drawn at, and stops when the control
/// leaves the screen or is recycled for another entry.
/// </summary>
public sealed class EntryIcon : Panel
{
    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<EntryIcon, string?>(nameof(FilePath));

    public static readonly StyledProperty<string?> MimeTypeProperty =
        AvaloniaProperty.Register<EntryIcon, string?>(nameof(MimeType));

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

    // A thumbnail is this control's own and is disposed with it; a type icon is shared (IconBitmaps) and is not
    private Bitmap? _ownedBitmap;

    public EntryIcon()
    {
        _icon.Classes.Add("entry-icon");
        _icon.Kind = Kind;
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.HighQuality);

        Children.Add(_icon);
        Children.Add(_image);
    }

    /// <summary>The file to preview. Null (tree placeholders) or anything that isn't a picture: no thumbnail.</summary>
    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    /// <summary>The entry's MIME type: its icon theme icon is shown. Null: <see cref="Kind"/> only.</summary>
    public string? MimeType
    {
        get => GetValue(MimeTypeProperty);
        set => SetValue(MimeTypeProperty, value);
    }

    /// <summary>Shown until the theme icon or thumbnail is ready, and instead of them when there is none.</summary>
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
        else if (change.Property == FilePathProperty || change.Property == MimeTypeProperty)
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

    /// <summary>
    /// The type icon at once when it was already looked up (no flicker while scrolling a folder of known types), then
    /// in the background whatever is still missing: the thumbnail, else the type icon.
    /// </summary>
    private void Reload()
    {
        Clear();

        if (VisualRoot is null)
            return;

        var size = Math.Max(double.IsNaN(Width) ? 0 : Width, double.IsNaN(Height) ? 0 : Height);
        if (size <= 0)
            size = DefaultSize;

        // Sharp on HiDPI screens
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(size * scaling));

        var type = MimeType;
        if (type is not null && IconBitmaps.TryGetTypeIcon(type, pixelWidth, out var cached))
        {
            if (cached is not null)
                Show(cached, owned: false);

            // Looked up: nothing more to ask the theme
            type = null;
        }

        var thumbnailPath = !IsFolder && FilePath is { } path && ThumbnailLoader.CanLoad(path) ? path : null;
        if (thumbnailPath is null && type is null)
            return;

        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = LoadAsync(thumbnailPath, type, pixelWidth, cts); // never throws
    }

    /// <summary>UI thread. Shows what was loaded, unless the control moved on meanwhile.</summary>
    private async Task LoadAsync(string? thumbnailPath, string? mimeType, int pixelWidth, CancellationTokenSource cts)
    {
        if (thumbnailPath is not null)
        {
            var thumbnail = await ThumbnailLoader.LoadAsync(thumbnailPath, pixelWidth, cts.Token);
            if (thumbnail is not null)
            {
                if (IsCurrent(cts))
                    Show(thumbnail, owned: true);
                else
                    thumbnail.Dispose();

                return;
            }
        }

        if (mimeType is null || !IsCurrent(cts))
            return;

        var icon = await ThumbnailLoader.LoadTypeIconAsync(mimeType, pixelWidth, cts.Token);
        if (icon is not null && IsCurrent(cts))
            Show(icon, owned: false);
    }

    private bool IsCurrent(CancellationTokenSource cts) => !cts.IsCancellationRequested && ReferenceEquals(cts, _cts);

    private void Show(Bitmap bitmap, bool owned)
    {
        ReleaseBitmap();

        if (owned)
            _ownedBitmap = bitmap;

        _image.Source = bitmap;
        _image.IsVisible = true;
        _icon.IsVisible = false;
    }

    private void Clear()
    {
        _cts?.Cancel();
        _cts = null;

        ReleaseBitmap();
        _image.IsVisible = false;
        _icon.IsVisible = true;
    }

    private void ReleaseBitmap()
    {
        _image.Source = null;
        _ownedBitmap?.Dispose();
        _ownedBitmap = null;
    }
}
