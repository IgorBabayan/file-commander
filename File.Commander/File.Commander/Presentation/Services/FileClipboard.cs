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
