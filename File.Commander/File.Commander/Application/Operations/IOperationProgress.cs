namespace File.Commander.Application.Operations;

/// <summary>
/// What a long-running action reports to the Action center while it runs. Every member is safe to call
/// from any thread: the center reads the counters a few times a second on the UI thread.
/// </summary>
public interface IOperationProgress
{
    /// <summary>Canceled by the item's Cancel button, Cancel all, or closing the window. Checked between items.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>How much there is to do. -1: unknown. Bytes, when known, drive the percentage and the speed.</summary>
    void SetTotal(long items, long bytes = -1);

    /// <summary>The item being worked on, shown under the progress bar.</summary>
    void Begin(string item);

    /// <summary>Items (failed ones included) and bytes that are done.</summary>
    void Advance(long items = 1, long bytes = 0);

    /// <summary>An item that couldn't be processed. The action goes on and ends as failed.</summary>
    void Fail(string item, string reason);
}
