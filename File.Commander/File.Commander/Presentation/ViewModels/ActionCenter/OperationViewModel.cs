using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Operations;
using File.Commander.Presentation.ViewModels.Helpers;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.ActionCenter;

/// <summary>
/// One action in the Action center: a copy, a move, emptying the trash… The worker reports through
/// <see cref="IOperationProgress"/> from its own thread; <see cref="Refresh"/> turns that into the
/// title, progress bar, speed and time left on the UI thread.
/// </summary>
public sealed partial class OperationViewModel : ViewModelBase, IOperationProgress
{
    /// <summary>Speed samples kept for the graph: a minute at the Action center's 4 refreshes a second.</summary>
    public const int SampleCapacity = 240;

    // Failed items listed under "More details"; the count goes on past it
    private const int MaxListedFailures = 200;

    private readonly CancellationTokenSource _cts = new();
    private readonly CancellationToken _token;
    private readonly OperationTitles _titles;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // Written by the worker's thread, read on the UI thread
    private long _itemsTotal = -1;
    private long _bytesTotal = -1;
    private long _itemsDone;
    private long _bytesDone;
    private long _failedCount;
    private string? _currentItem;
    private readonly Lock _failuresLock = new();
    private readonly List<string> _pendingFailures = [];
    private int _listedFailures;

    // Speed, measured on the UI thread between two refreshes
    private readonly List<double> _samples = [];
    private long _lastAmount;
    private double _lastSeconds;
    private double _speed;

    private string? _error;

    public OperationViewModel(OperationKind kind, OperationTitles titles, string? details)
    {
        Kind = kind;
        _titles = titles;
        _token = _cts.Token;
        Details = details ?? string.Empty;
        StartedAt = DateTime.Now;
        Title = titles.Running;
        StatusText = "Starting…";
    }

    /// <summary>Raised by the dismiss button of a finished action.</summary>
    public event EventHandler? DismissRequested;

    public OperationKind Kind { get; }

    /// <summary>Where it happens, e.g. the source and target folders.</summary>
    public string Details { get; }

    public bool HasDetails => Details.Length > 0;

    public DateTime StartedAt { get; }

    public DateTime? FinishedAt { get; private set; }

    /// <summary>The action center was open, or has been opened, since this action finished.</summary>
    public bool IsSeen { get; set; }

    public long FailedCount => Interlocked.Read(ref _failedCount);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsFinished), nameof(IsSucceeded), nameof(IsFailed),
        nameof(IsCanceled), nameof(Icon))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand), nameof(DismissCommand))]
    public partial OperationState State { get; private set; }

    /// <summary>"Emptying the trash", then "Emptied the trash" (or what went wrong).</summary>
    [ObservableProperty]
    public partial string Title { get; private set; }

    /// <summary>0–100. Only meaningful while <see cref="IsIndeterminate"/> is false.</summary>
    [ObservableProperty]
    public partial double Percent { get; private set; }

    /// <summary>Nothing told how much there is to do yet.</summary>
    [ObservableProperty]
    public partial bool IsIndeterminate { get; private set; } = true;

    /// <summary>"45% · 12 of 30 items · 25 MB/s · About 2 min left", or the outcome once finished.</summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; }

    /// <summary>"Speed: 25 MB/s", shown next to the graph.</summary>
    [ObservableProperty]
    public partial string SpeedText { get; private set; } = string.Empty;

    /// <summary>The item being worked on, shown under "More details".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentItem))]
    public partial string CurrentItemText { get; private set; } = string.Empty;

    /// <summary>The speed over time, oldest first. A new array on every refresh, so the graph redraws.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<double> SpeedSamples { get; private set; } = [];

    /// <summary>"Started at 14:02 · took 3 s", under "More details".</summary>
    [ObservableProperty]
    public partial string TimingText { get; private set; } = string.Empty;

    /// <summary>"More details" is open: speed graph, current item and failed items.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Cancel was pressed; the worker stops at its next item.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsCancelRequested { get; private set; }

    /// <summary>"name — reason" for each item that couldn't be processed.</summary>
    public ObservableCollection<string> Failures { get; } = [];

    [ObservableProperty]
    public partial bool HasFailures { get; private set; }

    public bool HasCurrentItem => CurrentItemText.Length > 0;

    public bool IsRunning => State == OperationState.Running;

    public bool IsFinished => !IsRunning;

    public bool IsSucceeded => State == OperationState.Succeeded;

    public bool IsFailed => State == OperationState.Failed;

    public bool IsCanceled => State == OperationState.Canceled;

    public MaterialIconKind Icon => State switch
    {
        OperationState.Succeeded => MaterialIconKind.CheckCircleOutline,
        OperationState.Failed => MaterialIconKind.AlertCircleOutline,
        OperationState.Canceled => MaterialIconKind.CloseCircleOutline,
        _ => Kind switch
        {
            OperationKind.Copy => MaterialIconKind.ContentCopy,
            OperationKind.Move => MaterialIconKind.FileMoveOutline,
            OperationKind.Delete => MaterialIconKind.DeleteOutline,
            OperationKind.EmptyTrash => MaterialIconKind.DeleteSweepOutline,
            _ => MaterialIconKind.FileCogOutline,
        },
    };

    #region IOperationProgress (any thread)

    public CancellationToken CancellationToken => _token;

    public void SetTotal(long items, long bytes = -1)
    {
        Interlocked.Exchange(ref _itemsTotal, items);
        Interlocked.Exchange(ref _bytesTotal, bytes);
    }

    public void Begin(string item) => Volatile.Write(ref _currentItem, item);

    public void Advance(long items = 1, long bytes = 0)
    {
        if (items != 0)
            Interlocked.Add(ref _itemsDone, items);
        if (bytes != 0)
            Interlocked.Add(ref _bytesDone, bytes);
    }

    public void Fail(string item, string reason)
    {
        Interlocked.Increment(ref _failedCount);
        lock (_failuresLock)
            _pendingFailures.Add($"{item} — {reason}");
    }

    #endregion

    /// <summary>Cancel: the worker stops before its next item. What's done stays done.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel()
    {
        if (!CanCancel())
            return;

        IsCancelRequested = true;
        StatusText = "Stopping…";
        _cts.Cancel();
    }

    private bool CanCancel() => IsRunning && !IsCancelRequested;

    [RelayCommand(CanExecute = nameof(IsFinished))]
    private void Dismiss() => DismissRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Reads what the worker reported. UI thread, a few times a second while running.</summary>
    public void Refresh()
    {
        var itemsTotal = Interlocked.Read(ref _itemsTotal);
        var bytesTotal = Interlocked.Read(ref _bytesTotal);
        var itemsDone = Interlocked.Read(ref _itemsDone);
        var bytesDone = Interlocked.Read(ref _bytesDone);
        var byBytes = bytesTotal > 0;

        // Bytes when known: one big file among small ones would make an item count jump
        var amount = byBytes ? bytesDone : itemsDone;
        var total = byBytes ? bytesTotal : itemsTotal;

        IsIndeterminate = total <= 0;
        Percent = total > 0 ? Math.Clamp(100.0 * amount / total, 0, 100) : 0;

        MeasureSpeed(amount);
        SpeedText = _speed > 0 ? $"Speed: {FormatSpeed(_speed, byBytes)}" : string.Empty;
        CurrentItemText = Volatile.Read(ref _currentItem) ?? string.Empty;
        TimingText = $"Started at {StartedAt:T} · running for {FormatDuration(_clock.Elapsed)}";
        MoveFailures();

        if (IsCancelRequested || !IsRunning)
            return;

        var parts = new List<string>(4);
        if (total > 0)
            parts.Add($"{Percent.ToString("0", CultureInfo.CurrentCulture)}%");
        if (itemsTotal > 0)
            parts.Add($"{Math.Min(itemsDone, itemsTotal):N0} of {Items(itemsTotal)}");
        if (byBytes)
            parts.Add($"{SizeFormatter.Format(bytesDone)} of {SizeFormatter.Format(bytesTotal)}");
        if (_speed > 0)
        {
            parts.Add(FormatSpeed(_speed, byBytes));
            if (total > 0)
                parts.Add(FormatRemaining(Math.Max(0, total - amount) / _speed));
        }

        StatusText = parts.Count > 0 ? string.Join(" · ", parts) : "Working…";
    }

    /// <summary>Called once, on the UI thread, when the worker has returned.</summary>
    /// <param name="error">Why it failed as a whole, if it threw.</param>
    public void Finish(OperationState state, string? error = null)
    {
        Refresh();
        _clock.Stop();
        _error = error;
        FinishedAt = DateTime.Now;
        CurrentItemText = string.Empty;
        State = state;
        Title = _titles.For(state);

        var itemsTotal = Interlocked.Read(ref _itemsTotal);
        var itemsDone = Interlocked.Read(ref _itemsDone);
        var bytesDone = Interlocked.Read(ref _bytesDone);
        var took = FormatDuration(_clock.Elapsed);

        if (state == OperationState.Succeeded)
        {
            IsIndeterminate = false;
            Percent = 100;
        }

        StatusText = state switch
        {
            OperationState.Succeeded => itemsDone > 0
                ? $"{Items(itemsDone)}{(bytesDone > 0 ? $" ({SizeFormatter.Format(bytesDone)})" : string.Empty)} in {took}"
                : $"Done in {took}",
            OperationState.Failed => _error ?? _titles.FailureMessage(FailedCount),
            OperationState.Canceled => itemsTotal > 0
                ? $"Stopped after {Math.Min(itemsDone, itemsTotal):N0} of {Items(itemsTotal)}"
                : "Stopped before it finished",
            _ => StatusText,
        };

        TimingText = $"Started at {StartedAt:T} · finished at {FinishedAt:T} · took {took}";
    }

    protected override void OnDispose()
    {
        // A running worker may still check the token: only cancel it then
        _cts.Cancel();
        if (IsFinished)
            _cts.Dispose();

        base.OnDispose();
    }

    private void MeasureSpeed(long amount)
    {
        var seconds = _clock.Elapsed.TotalSeconds;
        var elapsed = seconds - _lastSeconds;
        if (elapsed < 0.2)
            return;

        var current = Math.Max(0, amount - _lastAmount) / elapsed;
        _lastAmount = amount;
        _lastSeconds = seconds;

        // Smoothed, so "time left" doesn't jump with every file
        _speed = _speed <= 0 ? current : 0.3 * current + 0.7 * _speed;

        _samples.Add(current);
        if (_samples.Count > SampleCapacity)
            _samples.RemoveAt(0);

        SpeedSamples = _samples.ToArray();
    }

    private void MoveFailures()
    {
        string[] pending;
        lock (_failuresLock)
        {
            if (_pendingFailures.Count == 0)
                return;

            pending = _pendingFailures.ToArray();
            _pendingFailures.Clear();
        }

        foreach (var failure in pending)
        {
            if (_listedFailures >= MaxListedFailures)
                break;

            Failures.Add(failure);
            _listedFailures++;
        }

        HasFailures = Failures.Count > 0;
    }

    private static string Items(long count) => count == 1 ? "1 item" : $"{count:N0} items";

    private static string FormatSpeed(double perSecond, bool bytes)
        => bytes
            ? $"{SizeFormatter.Format((long)perSecond)}/s"
            : $"{perSecond.ToString(perSecond < 10 ? "0.#" : "0", CultureInfo.CurrentCulture)} items/s";

    private static string FormatRemaining(double seconds) => seconds switch
    {
        < 10 => "A few seconds left",
        < 60 => $"About {Math.Ceiling(seconds / 5) * 5:0} s left",
        < 3600 => $"About {Math.Ceiling(seconds / 60):0} min left",
        _ => $"About {(seconds / 3600).ToString("0.#", CultureInfo.CurrentCulture)} h left",
    };

    private static string FormatDuration(TimeSpan time) => time.TotalSeconds switch
    {
        < 1 => "less than a second",
        < 60 => $"{time.TotalSeconds:0} s",
        < 3600 => $"{(int)time.TotalMinutes} min {time.Seconds} s",
        _ => $"{(int)time.TotalHours} h {time.Minutes} min",
    };
}
