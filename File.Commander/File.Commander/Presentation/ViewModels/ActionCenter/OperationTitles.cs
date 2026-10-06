namespace File.Commander.Presentation.ViewModels.ActionCenter;

/// <summary>The headline of an action in each state, and how it words items that couldn't be processed.</summary>
/// <param name="FailureMessage">The line under a failed action, given how many items failed.</param>
public sealed record OperationTitles(
    string Running,
    string Succeeded,
    string Failed,
    string Canceled,
    Func<long, string> FailureMessage)
{
    public static OperationTitles EmptyTrash { get; } = new(
        "Emptying the trash",
        "Emptied the trash",
        "The trash wasn't emptied completely",
        "Stopped emptying the trash",
        failed => failed == 1
            ? "1 item couldn't be deleted. Check that you're allowed to delete it."
            : $"{failed:N0} items couldn't be deleted. Check that you're allowed to delete them.");

    public static OperationTitles Copy { get; } = new(
        "Copying",
        "Copied",
        "Not everything was copied",
        "Stopped copying",
        failed => failed == 1
            ? "1 item couldn't be copied. Check that you're allowed to read it and to write to the destination."
            : $"{failed:N0} items couldn't be copied. Check that you're allowed to read them and to write to the destination.");

    public static OperationTitles Move { get; } = new(
        "Moving",
        "Moved",
        "Not everything was moved",
        "Stopped moving",
        failed => failed == 1
            ? "1 item couldn't be moved. Check that you're allowed to move it."
            : $"{failed:N0} items couldn't be moved. Check that you're allowed to move them.");

    public static OperationTitles MoveToTrash { get; } = new(
        "Moving to the trash",
        "Moved to the trash",
        "Not everything was moved to the trash",
        "Stopped moving to the trash",
        failed => failed == 1
            ? "1 item couldn't be moved to the trash. Check that you're allowed to delete it."
            : $"{failed:N0} items couldn't be moved to the trash. Check that you're allowed to delete them.");

    public static OperationTitles DeletePermanently { get; } = new(
        "Deleting",
        "Deleted",
        "Not everything was deleted",
        "Stopped deleting",
        failed => failed == 1
            ? "1 item couldn't be deleted. Check that you're allowed to delete it."
            : $"{failed:N0} items couldn't be deleted. Check that you're allowed to delete them.");

    public static OperationTitles Compress { get; } = new(
        "Compressing",
        "Compressed",
        "The archive is missing some items",
        "Stopped compressing",
        failed => failed == 1
            ? "1 item couldn't be added to the archive. Check that you're allowed to read it."
            : $"{failed:N0} items couldn't be added to the archive. Check that you're allowed to read them.");

    public static OperationTitles Extract { get; } = new(
        "Extracting",
        "Extracted",
        "Not everything was extracted",
        "Stopped extracting",
        failed => failed == 1
            ? "1 item couldn't be extracted. The archive may be damaged, or you may not be allowed to write to this folder."
            : $"{failed:N0} items couldn't be extracted. The archive may be damaged, or you may not be allowed to write to this folder.");

    /// <param name="version">The version being downloaded, e.g. "1.0.42".</param>
    public static OperationTitles DownloadUpdate(string version) => new(
        $"Downloading File Commander {version}",
        $"Downloaded File Commander {version}",
        "The update wasn't downloaded",
        "Stopped downloading the update",
        _ => "The update couldn't be downloaded. Check your internet connection.");

    /// <param name="version">The version being installed, e.g. "1.0.42".</param>
    public static OperationTitles InstallUpdate(string version) => new(
        $"Installing File Commander {version}",
        $"Installed File Commander {version}",
        "The update wasn't installed",
        "Stopped installing the update",
        _ => "The update couldn't be installed. Check that you're allowed to replace the AppImage.");

    public string For(OperationState state) => state switch
    {
        OperationState.Succeeded => Succeeded,
        OperationState.Failed => Failed,
        OperationState.Canceled => Canceled,
        _ => Running,
    };
}
