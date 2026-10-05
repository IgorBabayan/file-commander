using Avalonia.Media.Imaging;

namespace File.Commander.Presentation.Controls;

/// <summary>
/// Decodes small thumbnails for <see cref="EntryIcon"/>, a few at a time so a folder full of photos
/// doesn't take every core. Skia decodes straight to the requested width, so a 50 MP photo never
/// becomes a full-size bitmap in memory.
/// </summary>
internal static class ThumbnailLoader
{
    // Decoders Avalonia (Skia) ships with, same as the info panel. SVG would need another package.
    private static readonly HashSet<string> Extensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico" };

    private const long MaxBytes = 64L * 1024 * 1024;

    // Scrolling realizes and recycles rows quickly: don't decode for the ones only flown past
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(80);

    private static readonly SemaphoreSlim Gate = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));

    /// <summary>By extension only: cheap enough to call for every row.</summary>
    public static bool CanLoad(string path) => Extensions.Contains(IOPath.GetExtension(path));

    /// <summary>
    /// Null when cancelled or when the file isn't a decodable picture. Never throws.
    /// The caller owns the returned bitmap, even if it was cancelled meanwhile.
    /// </summary>
    public static async Task<Bitmap?> LoadAsync(string path, int pixelWidth, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SettleDelay, cancellationToken);
            await Gate.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(() => Decode(path, pixelWidth), cancellationToken);
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static Bitmap? Decode(string path, int pixelWidth)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxBytes)
                return null;

            using var stream = IOFile.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, pixelWidth, BitmapInterpolationMode.MediumQuality);
        }
        catch (Exception ex)
        {
            // Not really an image, truncated, unsupported variant, gone…: keep the icon
            Trace.WriteLine($"No thumbnail for '{path}': {ex.Message}");
            return null;
        }
    }
}
