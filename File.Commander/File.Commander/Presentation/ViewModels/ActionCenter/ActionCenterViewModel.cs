using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Operations;

namespace File.Commander.Presentation.ViewModels.ActionCenter;

/// <summary>
/// The Action center: the button between split view and Search, and the panel it opens.
/// Like the status center of the Files app, it lists what runs in the background (newest first) with
/// progress, speed and time left, lets each action be canceled, and keeps finished ones until dismissed.
/// The button shows a ring with the overall progress and a count while something runs, and a dot
/// for finished actions that haven't been looked at yet.
/// </summary>
public sealed partial class ActionCenterViewModel : ViewModelBase
{
    // Finished actions kept in the list; older ones go first
    private const int MaxFinished = 50;

    private readonly DispatcherTimer _timer;
    private bool _isOpen;

    public ActionCenterViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => RefreshRunning();
        Operations.CollectionChanged += (_, _) => UpdateSummary();
    }

    /// <summary>An action ended as failed. The view opens the panel, so it doesn't go unnoticed.</summary>
    public event EventHandler<OperationViewModel>? OperationFailed;

    /// <summary>Newest first.</summary>
    public ObservableCollection<OperationViewModel> Operations { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRunning), nameof(ShowFailureDot), nameof(ShowSuccessDot), nameof(ToolTipText))]
    [NotifyCanExecuteChangedFor(nameof(CancelAllCommand))]
    public partial int RunningCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoOperations))]
    public partial bool HasOperations { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCompletedCommand))]
    public partial bool HasFinished { get; private set; }

    /// <summary>0–1, the average of the running actions that know how much they have to do.</summary>
    [ObservableProperty]
    public partial double OverallProgress { get; private set; }

    /// <summary>No running action knows how much it has to do: the ring spins.</summary>
    [ObservableProperty]
    public partial bool IsOverallIndeterminate { get; private set; }

    /// <summary>"2 running", "Everything is done"… under the panel's title.</summary>
    [ObservableProperty]
    public partial string SummaryText { get; private set; } = "Nothing is running";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFailureDot), nameof(ShowSuccessDot), nameof(ToolTipText))]
    public partial int UnseenFailures { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSuccessDot), nameof(ToolTipText))]
    public partial int UnseenSuccesses { get; private set; }

    public bool HasRunning => RunningCount > 0;

    public bool HasNoOperations => !HasOperations;

    /// <summary>A red dot on the button: something failed since the panel was last open.</summary>
    public bool ShowFailureDot => !HasRunning && UnseenFailures > 0;

    /// <summary>An accent dot on the button: something finished since the panel was last open.</summary>
    public bool ShowSuccessDot => !HasRunning && UnseenFailures == 0 && UnseenSuccesses > 0;

    public string ToolTipText => RunningCount switch
    {
        1 => "Action center: 1 action running",
        > 1 => $"Action center: {RunningCount} actions running",
        _ when UnseenFailures > 0 => "Action center: an action failed",
        _ when UnseenSuccesses > 0 => "Action center: actions finished",
        _ => "Action center",
    };

    /// <summary>Set by the view while the panel is open: what finishes meanwhile counts as seen.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value)
                return;

            _isOpen = value;
            if (value)
                MarkAllSeen();
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a background thread and lists it in the Action center until it is
    /// dismissed. Call it on the UI thread. Never throws: what went wrong ends up in the returned action.
    /// </summary>
    /// <param name="details">Where it happens, shown under the title.</param>
    public async Task<OperationViewModel> RunAsync(OperationKind kind, OperationTitles titles, string? details,
        Func<IOperationProgress, Task> work)
    {
        Dispatcher.UIThread.VerifyAccess();

        var operation = new OperationViewModel(kind, titles, details);
        operation.DismissRequested += OnDismissRequested;
        Operations.Insert(0, operation);
        _timer.Start();

        OperationState state;
        string? error = null;
        try
        {
            await Task.Run(() => work(operation), CancellationToken.None);
            state = operation.CancellationToken.IsCancellationRequested ? OperationState.Canceled
                : operation.FailedCount > 0 ? OperationState.Failed
                : OperationState.Succeeded;
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            state = OperationState.Canceled;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Action '{titles.Running}' failed: {ex}");
            state = OperationState.Failed;
            error = ex.Message;
        }

        operation.Finish(state, error);
        operation.IsSeen = _isOpen;
        TrimFinished();
        UpdateSummary();

        if (state == OperationState.Failed)
            OperationFailed?.Invoke(this, operation);

        return operation;
    }

    /// <inheritdoc cref="RunAsync(OperationKind, OperationTitles, string?, Func{IOperationProgress, Task})"/>
    public Task<OperationViewModel> RunAsync(OperationKind kind, OperationTitles titles, string? details,
        Action<IOperationProgress> work)
        => RunAsync(kind, titles, details, progress =>
        {
            work(progress);
            return Task.CompletedTask;
        });

    /// <summary>Cancels every running action. Each stops at its next item.</summary>
    [RelayCommand(CanExecute = nameof(HasRunning))]
    public void CancelAll()
    {
        foreach (var operation in Operations.Where(operation => operation.IsRunning).ToList())
            operation.Cancel();
    }

    /// <summary>Removes every finished action from the list.</summary>
    [RelayCommand(CanExecute = nameof(HasFinished))]
    private void ClearCompleted()
    {
        foreach (var operation in Operations.Where(operation => operation.IsFinished).ToList())
            Remove(operation);
    }

    protected override void OnDispose()
    {
        _timer.Stop();
        foreach (var operation in Operations.ToList())
            Remove(operation);

        base.OnDispose();
    }

    private void RefreshRunning()
    {
        var anyRunning = false;
        foreach (var operation in Operations)
        {
            if (!operation.IsRunning)
                continue;

            operation.Refresh();
            anyRunning = true;
        }

        if (!anyRunning)
            _timer.Stop();

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var running = 0;
        var finished = 0;
        var determinate = 0;
        var progress = 0.0;
        var unseenFailures = 0;
        var unseenSuccesses = 0;

        foreach (var operation in Operations)
        {
            if (operation.IsRunning)
            {
                running++;
                if (!operation.IsIndeterminate)
                {
                    determinate++;
                    progress += operation.Percent / 100;
                }

                continue;
            }

            finished++;
            if (operation.IsSeen)
                continue;

            if (operation.IsFailed)
                unseenFailures++;
            else
                unseenSuccesses++;
        }

        RunningCount = running;
        HasOperations = Operations.Count > 0;
        HasFinished = finished > 0;
        IsOverallIndeterminate = running > 0 && determinate == 0;
        OverallProgress = determinate > 0 ? progress / determinate : 0;
        UnseenFailures = unseenFailures;
        UnseenSuccesses = unseenSuccesses;
        SummaryText = running switch
        {
            1 => "1 action running",
            > 1 => $"{running} actions running",
            _ when finished > 0 => "Everything is done",
            _ => "Nothing is running",
        };
    }

    private void MarkAllSeen()
    {
        foreach (var operation in Operations)
        {
            if (operation.IsFinished)
                operation.IsSeen = true;
        }

        UpdateSummary();
    }

    private void TrimFinished()
    {
        var finished = Operations.Where(operation => operation.IsFinished).ToList();
        for (var i = MaxFinished; i < finished.Count; i++)
            Remove(finished[i]);
    }

    private void OnDismissRequested(object? sender, EventArgs e)
    {
        if (sender is OperationViewModel operation)
            Remove(operation);
    }

    private void Remove(OperationViewModel operation)
    {
        operation.DismissRequested -= OnDismissRequested;
        Operations.Remove(operation);
        operation.Dispose();
    }
}
