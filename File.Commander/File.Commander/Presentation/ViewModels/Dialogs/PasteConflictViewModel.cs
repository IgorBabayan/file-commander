using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Presentation.Services;

namespace File.Commander.Presentation.ViewModels.Dialogs;

/// <summary>What Paste does with a pasted item whose name is taken.</summary>
public enum PasteConflictChoice
{
    Replace,
    Rename,
    Skip,
}

/// <summary>
/// Paste (Ctrl+V) found a pasted item whose name is taken in the folder: replace the item there, give the pasted one
/// another name, or skip it. Replace and Skip can be applied to the other items whose name is taken; Rename is
/// asked for each item, with the name typed in the box. The dialog returns true on Continue, false on Cancel, Esc
/// or a click outside, which cancels the whole paste.
/// </summary>
public sealed partial class PasteConflictViewModel : ViewModelBase
{
    private readonly Func<string, bool> _isNameFree;

    /// <param name="name">The pasted item's name, the one that is taken.</param>
    /// <param name="isFolder">The pasted item is a folder.</param>
    /// <param name="folder">Where it is pasted.</param>
    /// <param name="remaining">How many items after this one have a name that is taken.</param>
    /// <param name="suggestedName">Shown in the name box: a name that isn't taken.</param>
    /// <param name="isNameFree">Whether a typed name can be used in <paramref name="folder"/>.</param>
    public PasteConflictViewModel(string name, bool isFolder, string folder, int remaining, string suggestedName,
        Func<string, bool> isNameFree)
    {
        _isNameFree = isNameFree;
        Name = name;
        IsFolder = isFolder;

        var folderName = IOPath.GetFileName(folder) is { Length: > 0 } last ? last : folder;
        Title = isFolder ? "A folder with this name already exists" : "A file with this name already exists";
        Message = $"“{name}” is already in “{folderName}”. What should happen to the pasted {(isFolder ? "folder" : "file")}?";

        Remaining = remaining;
        RemainingText = remaining == 1
            ? "1 more pasted item has a name that is taken."
            : $"{remaining:N0} more pasted items have names that are taken.";

        NewName = suggestedName;
        Choice = PasteConflictChoice.Rename;
    }

    /// <summary>Raised with the dialog's result. The window closes itself.</summary>
    public event Action<bool>? CloseRequested;

    public string Title { get; }

    public string Message { get; }

    public string Name { get; }

    public bool IsFolder { get; }

    public int Remaining { get; }

    /// <summary>"Do the same for the other items" is only offered when there are others.</summary>
    public bool HasRemaining => Remaining > 0;

    public string RemainingText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReplace), nameof(IsRename), nameof(IsSkip), nameof(CanApplyToAll),
        nameof(ShowNameError))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    public partial PasteConflictChoice Choice { get; set; }

    public bool IsReplace
    {
        get => Choice == PasteConflictChoice.Replace;
        set
        {
            if (value)
                Choice = PasteConflictChoice.Replace;
        }
    }

    public bool IsRename
    {
        get => Choice == PasteConflictChoice.Rename;
        set
        {
            if (value)
                Choice = PasteConflictChoice.Rename;
        }
    }

    public bool IsSkip
    {
        get => Choice == PasteConflictChoice.Skip;
        set
        {
            if (value)
                Choice = PasteConflictChoice.Skip;
        }
    }

    /// <summary>Replace and Skip can be applied to the other items; a name is typed for each item.</summary>
    public bool CanApplyToAll => Choice != PasteConflictChoice.Rename;

    /// <summary>"Do the same for the other items". Cleared when Rename is picked.</summary>
    [ObservableProperty]
    public partial bool ApplyToAll { get; set; }

    /// <summary>The pasted item's new name. Only used for Rename.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(ShowNameError))]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    public partial string NewName { get; set; }

    /// <summary>Why <see cref="NewName"/> can't be used, or null when it can.</summary>
    public string? NameError
    {
        get
        {
            var name = NewName.Trim();
            if (FileOperations.ValidateName(name) is { } problem)
                return problem;

            return _isNameFree(name) ? null : $"An item named “{name}” already exists in this folder.";
        }
    }

    public bool ShowNameError => IsRename && NameError is not null;

    partial void OnChoiceChanged(PasteConflictChoice value)
    {
        if (value == PasteConflictChoice.Rename)
            ApplyToAll = false;
    }

    private bool CanAccept() => Choice != PasteConflictChoice.Rename || NameError is null;

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private void Accept() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
