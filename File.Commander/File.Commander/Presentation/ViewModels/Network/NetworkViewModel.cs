using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace File.Commander.Presentation.ViewModels.Network;

/// <summary>
/// The Network page: shares mounted on this computer, file servers that announce themselves on the local network,
/// and Connect to server for an address. Connecting mounts the share through GVfs and opens its folder in this view.
/// </summary>
public sealed partial class NetworkViewModel : PageViewModel, IGioMountHandler
{
    // What the last search found: shown at once on the next visit, while the network is searched again
    private static IReadOnlyList<NetworkService> _lastFound = [];

    private readonly INavigator _navigator;
    private readonly IDialogService _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _search;

    // Named in the login dialog and the questions of the server being connected to
    private string _server = string.Empty;

    public NetworkViewModel(INavigator navigator, IDialogService dialogs)
    {
        _navigator = navigator;
        _dialogs = dialogs;

        ReloadMounts();
        Services = ToCards(_lastFound);
        _ = SearchAsync(); // never throws
    }

    public override string Location => Locations.Network;

    public override string Title => "Network";

    /// <summary>Shares mounted now: opened with a click, disconnected with the eject button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMounts), nameof(StatusText))]
    public partial IReadOnlyList<NetworkMountCardViewModel> Mounts { get; set; } = [];

    /// <summary>Servers found on the local network.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNothingFound), nameof(StatusText))]
    public partial IReadOnlyList<NetworkServiceCardViewModel> Services { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNothingFound), nameof(StatusText))]
    public partial bool IsSearching { get; set; }

    /// <summary>What is typed into Connect to server.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial string ServerAddress { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool IsConnecting { get; set; }

    [ObservableProperty]
    public partial string ConnectingText { get; set; } = string.Empty;

    /// <summary>The shares of a Windows server that was connected to without naming a share. Null: none shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShares))]
    public partial ServerSharesViewModel? Shares { get; set; }

    public bool HasMounts => Mounts.Count > 0;

    public bool HasShares => Shares is not null;

    public bool IsNothingFound => !IsSearching && Services.Count == 0;

    /// <summary>Connecting needs GVfs; mounted shares are listed without it.</summary>
    public bool CanMount { get; } = GioMount.IsAvailable;

    public override string StatusText
    {
        get
        {
            if (IsConnecting)
                return ConnectingText;

            var text = $"{Mounts.Count} connected · {Services.Count} on this network";
            return IsSearching ? $"{text} · Searching…" : text;
        }
    }

    /// <summary>
    /// Mounts <paramref name="address"/> and opens it here. A bare name is taken as a Windows server; a Windows server
    /// without a share lists its shares instead. Also called for an address typed into the address bar.
    /// </summary>
    public async Task ConnectAsync(string address)
    {
        if (IsConnecting)
            return;

        var text = address.Trim();
        if (text.Length == 0)
            return;

        if (!text.Contains("://", StringComparison.Ordinal))
            text = "smb://" + text;

        if (!NetworkLocations.TryParseUri(text, out var uri))
        {
            await NoticeAsync("Can't connect",
                $"“{address.Trim()}” isn't a server address. Addresses start with smb://, sftp://, ftp://, dav:// " +
                "or nfs://, for example smb://nas/media or sftp://me@server.");
            return;
        }

        if (!CanMount)
        {
            await NoticeAsync("Can't connect",
                "Connecting to servers needs GVfs. Install the gvfs package, and gvfs-smb for Windows shares.");
            return;
        }

        ServerAddress = text;
        _server = uri.Host;
        ConnectingText = $"Connecting to {uri.Host}…";
        IsConnecting = true;
        try
        {
            var result = await GioMount.MountAsync(text, this, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || result.Cancelled)
                return;

            if (!result.Success)
            {
                await NoticeAsync($"Can't connect to {uri.Host}", result.Error ?? "The server couldn't be mounted.");
                return;
            }

            // A Windows server, not one of its shares: list them, a click connects to one
            if (NetworkLocations.ProtocolOf(uri.Scheme) == NetworkProtocol.Smb && uri.AbsolutePath.Trim('/').Length == 0)
            {
                await ShowSharesAsync(text, uri.Host);
                return;
            }

            var path = await GioMount.LocalPathAsync(text, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
                return;

            ReloadMounts();
            if (path is null)
            {
                await NoticeAsync($"Connected to {uri.Host}",
                    "The share is mounted, but it has no folder to open: GVfs's FUSE daemon (gvfsd-fuse) isn't running.");
                return;
            }

            _navigator.Navigate(path); // Disposes this page
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
                IsConnecting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task Connect() => ConnectAsync(ServerAddress);

    private bool CanConnect() => !IsConnecting && !string.IsNullOrWhiteSpace(ServerAddress);

    /// <summary>The refresh button of "On this network": reads the mounts again and searches the network again.</summary>
    [RelayCommand]
    private void Refresh()
    {
        ReloadMounts();
        _ = SearchAsync();
    }

    private async Task ShowSharesAsync(string serverUri, string server)
    {
        var names = await GioMount.ListSharesAsync(serverUri, _lifetime.Token);
        if (_lifetime.IsCancellationRequested)
            return;

        var root = serverUri.TrimEnd('/');
        var shares = names
            .Select(name => new NetworkShareCardViewModel(name, $"{root}/{Uri.EscapeDataString(name)}/", ConnectAsync))
            .ToList();
        Shares = new ServerSharesViewModel(server, shares, () => Shares = null);
    }

    private async Task DisconnectAsync(NetworkMount mount)
    {
        var result = await GioMount.UnmountAsync(mount, _lifetime.Token);
        if (_lifetime.IsCancellationRequested)
            return;

        ReloadMounts();
        if (!result.Success && !result.Cancelled)
            await NoticeAsync($"Can't disconnect “{mount.Name}”", result.Error ?? "The share couldn't be unmounted.");
    }

    private void ReloadMounts()
        => Mounts = NetworkLocations.GetMounts()
            .Select(mount => new NetworkMountCardViewModel(mount, _navigator, DisconnectAsync))
            .ToList();

    /// <summary>Asks the network for servers. Another search (Refresh) or leaving the page cancels it.</summary>
    private async Task SearchAsync()
    {
        _search?.Cancel();
        var search = _search = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

        IsSearching = true;
        try
        {
            var found = await Task.Run(() => NetworkDiscovery.BrowseAsync(search.Token), search.Token);
            _lastFound = found;
            Services = ToCards(found);
        }
        catch (OperationCanceledException)
        {
            // Replaced by another search, or the page is gone
        }
        finally
        {
            if (!search.IsCancellationRequested)
                IsSearching = false;
        }
    }

    private IReadOnlyList<NetworkServiceCardViewModel> ToCards(IReadOnlyList<NetworkService> services)
        => services.Select(service => new NetworkServiceCardViewModel(service, ConnectAsync)).ToList();

    private async Task NoticeAsync(string title, string message)
    {
        using var notice = PromptViewModel.ForNotice(title, message);
        await _dialogs.ShowDialogAsync<PromptViewModel, bool>(notice);
    }

    async Task<GioCredentials?> IGioMountHandler.AskLoginAsync(GioLoginRequest request)
    {
        using var login = new NetworkLoginViewModel(_server, request);
        return await _dialogs.ShowDialogAsync<NetworkLoginViewModel, bool>(login) ? login.ToCredentials() : null;
    }

    async Task<int?> IGioMountHandler.AskQuestionAsync(GioQuestion question)
    {
        // The first choice is the one that goes on ("Log In Anyway"); the last one cancels
        var accept = question.Choices.Count > 0 ? question.Choices[0] : "OK";
        using var prompt = PromptViewModel.ForConfirmation($"Connect to {_server}", question.Message, accept);
        return await _dialogs.ShowDialogAsync<PromptViewModel, bool>(prompt) ? 0 : null;
    }

    protected override void OnDispose()
    {
        // Stops a search, and a mount that is still waiting for the server
        // (not disposed: the awaits that were waiting still read their tokens as they unwind)
        _lifetime.Cancel();
        base.OnDispose();
    }
}

/// <summary>A share mounted on this computer.</summary>
public sealed class NetworkMountCardViewModel
{
    public NetworkMountCardViewModel(NetworkMount mount, INavigator navigator, Func<NetworkMount, Task> disconnect)
    {
        Name = mount.Name;
        MountPoint = mount.MountPoint;
        Details = mount.Server.Length > 0 && mount.Server != mount.Name
            ? $"{NetworkLocations.NameOf(mount.Protocol)} · {mount.Server}"
            : NetworkLocations.NameOf(mount.Protocol);
        Icon = LocationIcons.ForNetwork(mount.Protocol);
        OpenCommand = new RelayCommand(() => navigator.Navigate(mount.MountPoint));
        DisconnectCommand = new AsyncRelayCommand(() => disconnect(mount));
    }

    public string Name { get; }

    public string MountPoint { get; }

    public string Details { get; }

    public MaterialIconKind Icon { get; }

    public IRelayCommand OpenCommand { get; }

    public IAsyncRelayCommand DisconnectCommand { get; }
}

/// <summary>A server found on the local network: a click connects to it.</summary>
public sealed class NetworkServiceCardViewModel
{
    public NetworkServiceCardViewModel(NetworkService service, Func<string, Task> connect)
    {
        Name = service.Name;
        Uri = service.Uri;
        Details = $"{NetworkLocations.NameOf(service.Protocol)} · {service.Host}";
        Icon = LocationIcons.ForNetwork(service.Protocol);
        ConnectCommand = new AsyncRelayCommand(() => connect(service.Uri));
    }

    public string Name { get; }

    public string Uri { get; }

    public string Details { get; }

    public MaterialIconKind Icon { get; }

    public IAsyncRelayCommand ConnectCommand { get; }
}

/// <summary>The shares of one Windows server.</summary>
public sealed class ServerSharesViewModel
{
    public ServerSharesViewModel(string server, IReadOnlyList<NetworkShareCardViewModel> shares, Action close)
    {
        Title = $"Shares on {server}";
        Items = shares;
        CloseCommand = new RelayCommand(close);
    }

    public string Title { get; }

    public IReadOnlyList<NetworkShareCardViewModel> Items { get; }

    public bool IsEmpty => Items.Count == 0;

    public IRelayCommand CloseCommand { get; }
}

public sealed class NetworkShareCardViewModel
{
    public NetworkShareCardViewModel(string name, string uri, Func<string, Task> connect)
    {
        Name = name;
        Uri = uri;
        ConnectCommand = new AsyncRelayCommand(() => connect(uri));
    }

    public string Name { get; }

    public string Uri { get; }

    public IAsyncRelayCommand ConnectCommand { get; }
}
