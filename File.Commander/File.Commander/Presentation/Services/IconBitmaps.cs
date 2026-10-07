using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Svg.Skia;

namespace File.Commander.Presentation.Services;

/// <summary>
/// Icon files of the icon theme as bitmaps: PNG decoded by Avalonia, SVG drawn by Svg.Skia, both at the size they
/// are shown at. The icon of a MIME type is kept and shared by every entry of that type, so a folder of a thousand
/// spreadsheets decodes one icon.
/// </summary>
public static class IconBitmaps
{
    // Icon files are small; anything bigger is not an icon
    private const long MaxBytes = 8L * 1024 * 1024;

    // By type and size: few types, two or three sizes. Kept for the app's lifetime and never disposed, since any
    // number of controls may show them. Null results are kept too: the type has no theme icon.
    private static readonly ConcurrentDictionary<(string Type, int Size), Lazy<Bitmap?>> TypeIcons = new();

    // By file and size: many types share an icon file (every text/* may end up at text-x-generic)
    private static readonly ConcurrentDictionary<(string Path, int Size), Lazy<Bitmap?>> Files = new();

    /// <summary>
    /// The theme's icon for <paramref name="mimeType"/>, if it was already looked up: no file is read, so the UI
    /// thread may call it. True with a null <paramref name="bitmap"/> means the theme has none.
    /// The bitmap is shared: never dispose it.
    /// </summary>
    public static bool TryGetTypeIcon(string mimeType, int size, out Bitmap? bitmap)
    {
        if (TypeIcons.TryGetValue((mimeType, size), out var lazy) && lazy.IsValueCreated)
        {
            bitmap = lazy.Value;
            return true;
        }

        bitmap = null;
        return false;
    }

    /// <summary>
    /// The theme's icon for <paramref name="mimeType"/> at <paramref name="size"/> pixels, or null when the theme has
    /// none. Reads files the first time: call it off the UI thread. Never throws. The bitmap is shared: never dispose it.
    /// </summary>
    public static Bitmap? TypeIcon(string mimeType, int size)
        => TypeIcons.GetOrAdd((mimeType, size), key => new Lazy<Bitmap?>(() =>
            ThemeIcons.ForType(key.Type, key.Size) is { } path
                ? Files.GetOrAdd((path, key.Size), file => new Lazy<Bitmap?>(() => Decode(file.Path, file.Size))).Value
                : null)).Value;

    /// <summary>
    /// A PNG or SVG icon file drawn <paramref name="size"/> pixels wide (an SVG fits in a square of that size).
    /// Null when it can't be read. The caller owns the bitmap. Never throws.
    /// </summary>
    public static Bitmap? Decode(string path, int size)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxBytes)
                return null;

            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                return DecodeSvg(path, size);

            using var stream = IOFile.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, Math.Max(1, size), BitmapInterpolationMode.MediumQuality);
        }
        catch (Exception ex)
        {
            // A broken file in an icon theme is no reason to fail: the caller keeps its default icon
            Trace.WriteLine($"Can't decode the icon '{path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Drawn straight at the target size, so it stays sharp at any scale.</summary>
    private static Bitmap? DecodeSvg(string path, int size)
    {
        using var svg = new SKSvg();
        if (svg.Load(path) is not { } picture)
            return null;

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        var scale = Math.Max(1, size) / Math.Max(bounds.Width, bounds.Height);

        using var png = new MemoryStream();
        if (!svg.Save(png, SKColors.Transparent, SKEncodedImageFormat.Png, 100, scale, scale))
            return null;

        png.Position = 0;
        return new Bitmap(png);
    }
}
