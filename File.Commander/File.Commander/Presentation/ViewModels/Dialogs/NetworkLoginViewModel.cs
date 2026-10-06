using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace File.Commander.Presentation.ViewModels.Dialogs;

/// <summary>
/// The login a server asks for while it is connected to. The dialog returns true for Connect (or Enter) and for
/// Connect as guest, false on Cancel, Esc or a click outside.
/// </summary>
public sealed partial class NetworkLoginViewModel : ViewModelBase
{
    public NetworkLoginViewModel(string server, GioLoginRequest request)
    {
        Title = $"Connect to {server}";
        Message = string.IsNullOrWhiteSpace(request.Message)
            ? $"{server} asks for a password."
            : request.Message;
        HasUser = request.NeedsUser;
        HasDomain = request.NeedsDomain;
        IsRetry = request.IsRetry;

        // Servers that ask for a user may let a guest in (anonymous FTP, public Windows shares)
        CanConnectAsGuest = request.NeedsUser;

        User = request.DefaultUser ?? string.Empty;
        Domain = request.DefaultDomain ?? string.Empty;
    }

    /// <summary>Raised with the dialog's result. The window closes itself.</summary>
    public event Action<bool>? CloseRequested;

    public string Title { get; }

    public string Message { get; }

    public bool HasUser { get; }

    public bool HasDomain { get; }

    /// <summary>The last login was refused: says so above the fields.</summary>
    public bool IsRetry { get; }

    public bool CanConnectAsGuest { get; }

    /// <summary>Connect as guest was pressed.</summary>
    public bool IsGuest { get; private set; }

    [ObservableProperty]
    public partial string User { get; set; }

    [ObservableProperty]
    public partial string Domain { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    public GioCredentials ToCredentials() => new(User.Trim(), Domain.Trim(), Password, IsGuest);

    [RelayCommand]
    private void Accept() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void ConnectAsGuest()
    {
        IsGuest = true;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
