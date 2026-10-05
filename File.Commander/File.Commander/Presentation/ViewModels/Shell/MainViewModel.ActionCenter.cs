using File.Commander.Presentation.ViewModels.ActionCenter;
using File.Commander.Presentation.ViewModels.Dialogs;

namespace File.Commander.Presentation.ViewModels.Shell;

/// <summary>The Action center: the button between split view and Search, and what runs in it.</summary>
public partial class MainViewModel
{
    /// <summary>Background actions (emptying the trash…), with their progress. Shared by every tab.</summary>
    public ActionCenterViewModel ActionCenter { get; }

    /// <summary>
    /// Closing the window while actions run: asks first. True: close, the actions get canceled.
    /// </summary>
    public async Task<bool> ConfirmCloseWithRunningActionsAsync()
    {
        var running = ActionCenter.RunningCount;
        if (running == 0)
            return true;

        using var confirm = PromptViewModel.ForConfirmation(
            running == 1 ? "An action is still running" : $"{running} actions are still running",
            running == 1
                ? "Closing File Commander stops it before it's finished. What's already done stays done."
                : "Closing File Commander stops them before they're finished. What's already done stays done.",
            "Stop and close",
            destructive: true);

        return await _dialogService.ShowDialogAsync<PromptViewModel, bool>(confirm);
    }
}
