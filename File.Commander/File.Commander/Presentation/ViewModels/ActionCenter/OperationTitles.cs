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

    public string For(OperationState state) => state switch
    {
        OperationState.Succeeded => Succeeded,
        OperationState.Failed => Failed,
        OperationState.Canceled => Canceled,
        _ => Running,
    };
}
