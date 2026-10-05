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

    public static OperationTitles Compress { get; } = new(
        "Compressing",
        "Compressed",
        "The archive is missing some items",
        "Stopped compressing",
        failed => failed == 1
            ? "1 item couldn't be added to the archive. Check that you're allowed to read it."
            : $"{failed:N0} items couldn't be added to the archive. Check that you're allowed to read them.");

    public string For(OperationState state) => state switch
    {
        OperationState.Succeeded => Succeeded,
        OperationState.Failed => Failed,
        OperationState.Canceled => Canceled,
        _ => Running,
    };
}
