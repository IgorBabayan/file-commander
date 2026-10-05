using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace File.Commander.Presentation.ViewModels.Dialogs;

/// <summary>
/// A small question on a dimmed backdrop: a confirmation, a notice with one button, or a name to type.
/// The dialog returns true when the main button was pressed (or Enter), false on Cancel, Esc or a click outside.
/// </summary>
public sealed partial class PromptViewModel : ViewModelBase
{
    private PromptViewModel(string title, string message, string acceptText, string? cancelText, bool isDestructive,
        string? input)
    {
        Title = title;
        Message = message;
        AcceptText = acceptText;
        CancelText = cancelText ?? string.Empty;
        HasCancel = cancelText is not null;
        IsDestructive = isDestructive;
        HasInput = input is not null;
        InputText = input ?? string.Empty;
    }

    /// <summary>Raised with the dialog's result. The window closes itself.</summary>
    public event Action<bool>? CloseRequested;

    public string Title { get; }

    public string Message { get; }

    public string AcceptText { get; }

    public string CancelText { get; }

    public bool HasCancel { get; }

    /// <summary>The main button is red: it deletes something for good.</summary>
    public bool IsDestructive { get; }

    public bool HasInput { get; }

    /// <summary>What was typed. Only used when <see cref="HasInput"/>.</summary>
    [ObservableProperty]
    public partial string InputText { get; set; }

    public static PromptViewModel ForConfirmation(string title, string message, string acceptText, bool destructive = false)
        => new(title, message, acceptText, "Cancel", destructive, input: null);

    public static PromptViewModel ForNotice(string title, string message)
        => new(title, message, "OK", cancelText: null, isDestructive: false, input: null);

    /// <param name="text">Shown in the box, selected, when the dialog opens.</param>
    public static PromptViewModel ForInput(string title, string message, string text, string acceptText)
        => new(title, message, acceptText, "Cancel", isDestructive: false, input: text);

    [RelayCommand]
    private void Accept() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
