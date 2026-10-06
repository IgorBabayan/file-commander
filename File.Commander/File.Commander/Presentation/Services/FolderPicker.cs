using Avalonia.Platform.Storage;

namespace File.Commander.Presentation.Services;

/// <summary>The system's folder chooser, for Move to… and Copy to…. Never throws.</summary>
public static class FolderPicker
{
    public static async Task<string?> PickAsync(string title, string? startFolder)
    {
        try
        {
            var provider = Utils.GetTopWindow().StorageProvider;
            if (!provider.CanPickFolder)
                return null;

            var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
            if (startFolder is not null && Directory.Exists(startFolder))
                options.SuggestedStartLocation = await provider.TryGetFolderFromPathAsync(startFolder);

            var picked = await provider.OpenFolderPickerAsync(options);
            return picked.Count > 0 && picked[0].TryGetLocalPath() is { } path ? Locations.Normalize(path) : null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Can't pick a folder: {ex.Message}");
            return null;
        }
    }
}
