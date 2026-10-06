using Avalonia.Input;

namespace File.Commander.Presentation.Views.Browser;

/// <summary>
/// What a drag out of a folder view carries: the full paths of the dragged entries. Only understood
/// inside this app (e.g. by the sidebar, which turns dropped folders into favorites).
/// </summary>
public static class FileDragData
{
    // NUL can't appear in a path, so it separates them safely
    private const char Separator = '\0';

    private static readonly DataFormat<string> PathsFormat =
        DataFormat.CreateStringApplicationFormat("file-commander-paths");

    public static DataTransfer Create(IEnumerable<string> paths)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(PathsFormat, string.Join(Separator, paths)));
        return data;
    }

    /// <summary>The dragged paths, or null when the drag didn't start in a folder view of this app.</summary>
    public static IReadOnlyList<string>? TryGetPaths(IDataTransfer data)
    {
        if (!data.Contains(PathsFormat) || data.TryGetValue(PathsFormat) is not { } text)
            return null;

        return text.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
    }
}
