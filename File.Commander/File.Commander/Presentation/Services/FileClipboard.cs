using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace File.Commander.Presentation.Services;

/// <summary>Files on the clipboard: their paths, and whether pasting moves them (Cut) or copies them (Copy).</summary>
public sealed record ClipboardFiles(IReadOnlyList<string> Paths, bool IsCut);

/// <summary>
/// Cut and Copy of files through the system clipboard, in the formats Linux file managers exchange:
/// x-special/gnome-copied-files (Nautilus, Nemo, Thunar…), text/uri-list with application/x-kde-cutselection
/// (Dolphin), and the plain paths as text for editors and terminals. So files cut or copied here can be pasted
/// there, and the other way round. Every member is for the UI thread and never throws.
/// </summary>
public static class FileClipboard
{
    private static readonly DataFormat<byte[]> GnomeFiles =
        DataFormat.CreateBytesPlatformFormat("x-special/gnome-copied-files");

    private static readonly DataFormat<byte[]> UriList = DataFormat.CreateBytesPlatformFormat("text/uri-list");

    private static readonly DataFormat<byte[]> KdeCut =
        DataFormat.CreateBytesPlatformFormat("application/x-kde-cutselection");

    // What this app put there last: read back as is while the clipboard still holds it
    private static DataTransfer? _placed;
    private static ClipboardFiles? _placedFiles;

    // What this app cut last, normalized: drawn lighter in the views until pasted, replaced or canceled (Esc)
    private static readonly HashSet<string> CutPaths = new(StringComparer.Ordinal);

    /// <summary>The cut files changed: cut, pasted, replaced on the clipboard, or canceled. Raised on the UI thread.</summary>
    public static event EventHandler? CutChanged;

    /// <summary>Files cut here are waiting to be pasted.</summary>
    public static bool HasCut => CutPaths.Count > 0;

    /// <summary>The item at <paramref name="path"/> was cut here and isn't pasted yet.</summary>
    public static bool IsCut(string? path)
        => path is not null && CutPaths.Count > 0 && CutPaths.Contains(Locations.Normalize(path));

    /// <returns>False when there is no clipboard or it refused the data.</returns>
    public static async Task<bool> SetAsync(IReadOnlyList<string> paths, bool cut)
    {
        if (paths.Count == 0 || Clipboard() is not { } clipboard)
            return false;

        var uris = paths.Select(ToUri).ToList();
        var item = new DataTransferItem();
        item.Set(GnomeFiles, Encoding.UTF8.GetBytes((cut ? "cut" : "copy") + "\n" + string.Join("\n", uris)));
        item.Set(UriList, Encoding.UTF8.GetBytes(string.Join("\r\n", uris) + "\r\n"));
        if (cut)
            item.Set(KdeCut, Encoding.ASCII.GetBytes("1"));
        item.Set(DataFormat.Text, string.Join("\n", paths));

        var data = new DataTransfer();
        data.Add(item);

        try
        {
            // Owned by the clipboard from now on: not disposed here
            await clipboard.SetDataAsync(data);
            _placed = data;
            _placedFiles = new ClipboardFiles(paths.ToList(), cut);

            // Copy replaces a Cut on the clipboard: those files stay where they are
            SetCut(cut ? paths : []);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Can't put files on the clipboard: {ex.Message}");
            return false;
        }
    }

    /// <summary>The files on the clipboard, put there by this app or another file manager. Null: no files.</summary>
    public static async Task<ClipboardFiles?> GetAsync()
    {
        if (Clipboard() is not { } clipboard)
            return null;

        try
        {
            if (_placed is not null && ReferenceEquals(await clipboard.TryGetInProcessDataAsync(), _placed))
                return _placedFiles;

            if (await clipboard.TryGetValueAsync(GnomeFiles) is { } gnome
                && ParseGnomeFiles(Encoding.UTF8.GetString(gnome)) is { } files)
                return files;

            if (await clipboard.TryGetValueAsync(UriList) is { } list
                && ParseUris(Encoding.UTF8.GetString(list).Split('\n')) is { Count: > 0 } paths)
            {
                var cut = await clipboard.TryGetValueAsync(KdeCut) is { } kde
                          && Encoding.ASCII.GetString(kde).Trim() == "1";
                return new ClipboardFiles(paths, cut);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Can't read the clipboard: {ex.Message}");
        }

        return null;
    }

    /// <summary>After cut files were pasted: they aren't where the clipboard says anymore.</summary>
    public static async Task ClearAsync()
    {
        if (Clipboard() is not { } clipboard)
            return;

        try
        {
            await clipboard.ClearAsync();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Can't clear the clipboard: {ex.Message}");
        }

        _placed = null;
        _placedFiles = null;
        SetCut([]);
    }

    /// <summary>
    /// Esc after Cut: the files are drawn as before and the clipboard is emptied, so Paste does nothing.
    /// The clipboard is left alone when another app has put something else there since.
    /// </summary>
    /// <returns>False: nothing cut here was waiting to be pasted.</returns>
    public static bool CancelCut()
    {
        if (!HasCut)
            return false;

        var placed = _placed;
        _placed = null;
        _placedFiles = null;
        SetCut([]);
        _ = ClearIfStillPlacedAsync(placed);
        return true;
    }

    /// <summary>
    /// The window is active again: if another app replaced what was cut here, the files aren't cut anymore.
    /// </summary>
    public static async Task ForgetReplacedCutAsync()
    {
        if (!HasCut || Clipboard() is not { } clipboard)
            return;

        var placed = _placed;
        try
        {
            if (ReferenceEquals(await clipboard.TryGetInProcessDataAsync(), placed))
                return;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Can't read the clipboard: {ex.Message}");
            return;
        }

        // Cut or copied again while reading: that is the current state
        if (!ReferenceEquals(_placed, placed))
            return;

        _placed = null;
        _placedFiles = null;
        SetCut([]);
    }

    private static async Task ClearIfStillPlacedAsync(DataTransfer? placed)
    {
        if (placed is null || Clipboard() is not { } clipboard)
            return;

        try
        {
            if (!ReferenceEquals(await clipboard.TryGetInProcessDataAsync(), placed))
                return;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Can't read the clipboard: {ex.Message}");
            return;
        }

        // Cut or copied again in the meantime: keep that
        if (_placed is null)
            await ClearAsync();
    }

    private static void SetCut(IReadOnlyList<string> paths)
    {
        if (CutPaths.Count == 0 && paths.Count == 0)
            return;

        CutPaths.Clear();
        foreach (var path in paths)
            CutPaths.Add(Locations.Normalize(path));

        CutChanged?.Invoke(null, EventArgs.Empty);
    }

    private static IClipboard? Clipboard()
    {
        try
        {
            return Utils.GetMainWindow().Clipboard;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>"cut" or "copy" on the first line, then one file:// URI per line.</summary>
    private static ClipboardFiles? ParseGnomeFiles(string text)
    {
        var lines = text.Split('\n');
        var action = lines[0].Trim();
        if (action is not ("cut" or "copy"))
            return null;

        var paths = ParseUris(lines.Skip(1));
        return paths.Count == 0 ? null : new ClipboardFiles(paths, action == "cut");
    }

    private static List<string> ParseUris(IEnumerable<string> lines)
    {
        var paths = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (FromUri(line) is { } path)
                paths.Add(path);
        }

        return paths;
    }

    /// <summary>"/home/me/My Notes.txt" → "file:///home/me/My%20Notes.txt". Each segment escaped, the slashes kept.</summary>
    private static string ToUri(string path)
        => "file://" + string.Join('/', Locations.Normalize(path).Split('/').Select(Uri.EscapeDataString));

    /// <summary>"file:///home/me/My%20Notes.txt" → "/home/me/My Notes.txt". Null for anything that isn't a local file.</summary>
    private static string? FromUri(string uri)
    {
        const string scheme = "file://";
        if (!uri.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return null;

        // file://host/path: the host (usually empty or localhost) is skipped
        var rest = uri[scheme.Length..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? null : Locations.Normalize(Uri.UnescapeDataString(rest[slash..]));
    }
}
