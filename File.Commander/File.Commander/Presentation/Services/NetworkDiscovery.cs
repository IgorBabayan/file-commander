using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace File.Commander.Presentation.Services;

/// <summary>A file server that announces itself on the local network.</summary>
/// <param name="Name">What it calls itself ("NAS", "Igor's MacBook").</param>
/// <param name="Host">Its host name ("nas.local"), shown under the name.</param>
/// <param name="Uri">What Connect to server mounts: "smb://192.168.1.5/", "sftp://nas.local:2222/".</param>
public sealed record NetworkService(string Name, string Host, NetworkProtocol Protocol, string Uri);

/// <summary>
/// Finds file servers on the local network with multicast DNS service discovery (what Avahi and Bonjour announce),
/// without needing avahi-daemon or avahi-utils. Queries are sent from a random port, so responders answer
/// straight back (RFC 6762 §6.7) and a running avahi-daemon keeps port 5353 for itself.
/// </summary>
public static class NetworkDiscovery
{
    private const ushort TypeA = 1;
    private const ushort TypePtr = 12;
    private const ushort TypeTxt = 16;
    private const ushort TypeSrv = 33;

    private static readonly IPEndPoint Group = new(IPAddress.Parse("224.0.0.251"), 5353);

    // Answers to the first queries; then twice the time for the follow-up questions about what they named
    private static readonly TimeSpan BrowseWait = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan ResolveWait = TimeSpan.FromMilliseconds(700);

    // _sftp-ssh before _ssh: a host announcing both is listed once
    private static readonly ServiceType[] ServiceTypes =
    [
        new("_smb._tcp.local", NetworkProtocol.Smb, "smb", 445),
        new("_sftp-ssh._tcp.local", NetworkProtocol.Sftp, "sftp", 22),
        new("_ssh._tcp.local", NetworkProtocol.Sftp, "sftp", 22),
        new("_ftp._tcp.local", NetworkProtocol.Ftp, "ftp", 21),
        new("_webdav._tcp.local", NetworkProtocol.WebDav, "dav", 80),
        new("_webdavs._tcp.local", NetworkProtocol.WebDav, "davs", 443),
        new("_nfs._tcp.local", NetworkProtocol.Nfs, "nfs", 2049),
    ];

    /// <summary>Asks the network for file servers; takes about 2.5 seconds. Empty when there is no network.</summary>
    public static async Task<IReadOnlyList<NetworkService>> BrowseAsync(CancellationToken token)
    {
        var clients = OpenClients();
        if (clients.Count == 0)
            return [];

        var cache = new RecordCache();
        using var listening = CancellationTokenSource.CreateLinkedTokenSource(token);
        var receivers = clients.Select(client => ReceiveAsync(client, cache, listening.Token)).ToList();

        try
        {
            await SendAsync(clients, ServiceTypes.Select(type => (type.Name, TypePtr))).ConfigureAwait(false);
            await Task.Delay(BrowseWait, token).ConfigureAwait(false);

            // Servers named in a PTR answer: where they are (SRV, TXT), then their addresses (A)
            await SendAsync(clients, cache.MissingQuestions()).ConfigureAwait(false);
            await Task.Delay(ResolveWait, token).ConfigureAwait(false);
            await SendAsync(clients, cache.MissingQuestions()).ConfigureAwait(false);
            await Task.Delay(ResolveWait, token).ConfigureAwait(false);
        }
        finally
        {
            await listening.CancelAsync().ConfigureAwait(false);
            foreach (var client in clients)
                client.Dispose();

            await Task.WhenAll(receivers).ConfigureAwait(false); // never throw
        }

        return cache.Build();
    }

    /// <summary>One socket per network interface: a multicast query only leaves through one.</summary>
    private static List<UdpClient> OpenClients()
    {
        var clients = new List<UdpClient>();
        foreach (var address in InterfaceAddresses())
        {
            if (TryOpen(address) is { } client)
                clients.Add(client);
        }

        if (clients.Count == 0 && TryOpen(null) is { } fallback)
            clients.Add(fallback);

        return clients;
    }

    private static UdpClient? TryOpen(IPAddress? address)
    {
        UdpClient? client = null;
        try
        {
            client = new UdpClient(new IPEndPoint(address ?? IPAddress.Any, 0));
            if (address is not null)
            {
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                    address.GetAddressBytes());
            }

            client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            return client;
        }
        catch (SocketException ex)
        {
            Trace.WriteLine($"Can't open an mDNS socket on {address}: {ex.Message}");
            client?.Dispose();
            return null;
        }
    }

    private static List<IPAddress> InterfaceAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                              && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
                              && nic.SupportsMulticast)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException ex)
        {
            Trace.WriteLine($"Can't list network interfaces: {ex.Message}");
            return [];
        }
    }

    private static async Task SendAsync(List<UdpClient> clients, IEnumerable<(string Name, ushort Type)> questions)
    {
        foreach (var (name, type) in questions)
        {
            var packet = Query(name, type);
            foreach (var client in clients)
            {
                try
                {
                    await client.SendAsync(packet, packet.Length, Group).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    // An interface without a route to the group: the others still ask
                }
            }
        }
    }

    private static async Task ReceiveAsync(UdpClient client, RecordCache cache, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await client.ReceiveAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (token.IsCancellationRequested)
                    return;

                continue;
            }

            cache.Add(result.Buffer, result.RemoteEndPoint.Address);
        }
    }

    /// <summary>A DNS query with one question, class IN.</summary>
    private static byte[] Query(string name, ushort type)
    {
        var packet = new List<byte>(64)
        {
            0, 0, // ID
            0, 0, // Flags: a standard query
            0, 1, // One question
            0, 0, 0, 0, 0, 0,
        };

        foreach (var label in SplitName(name))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            packet.Add((byte)Math.Min(bytes.Length, 63));
            packet.AddRange(bytes.Take(63));
        }

        packet.Add(0);
        packet.Add((byte)(type >> 8));
        packet.Add((byte)type);
        packet.Add(0);
        packet.Add(1); // IN
        return packet.ToArray();
    }

    // Names are kept as text, labels joined by "."; a "." or "\" inside a label (an instance like "Disk v1.2") is escaped
    private static string EscapeLabel(string label) => label.Replace("\\", "\\\\").Replace(".", "\\.");

    private static List<string> SplitName(string name)
    {
        var labels = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '\\' && i + 1 < name.Length)
            {
                current.Append(name[++i]);
            }
            else if (name[i] == '.')
            {
                labels.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(name[i]);
            }
        }

        if (current.Length > 0)
            labels.Add(current.ToString());

        return labels;
    }

    private sealed record ServiceType(string Name, NetworkProtocol Protocol, string Scheme, int DefaultPort);

    /// <summary>Every record heard so far. Filled by the receivers of all interfaces at once.</summary>
    private sealed class RecordCache
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<string, HashSet<string>> _instances = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (string Target, int Port)> _servers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> _texts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _addresses = new(StringComparer.OrdinalIgnoreCase);

        // Who answered about an instance: its address when no A record comes
        private readonly Dictionary<string, string> _senders = new(StringComparer.OrdinalIgnoreCase);

        public void Add(byte[] data, IPAddress sender)
        {
            lock (_lock)
            {
                try
                {
                    Parse(data, sender.ToString());
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or FormatException)
                {
                    // A truncated or malformed packet: what was read before it stays
                }
            }
        }

        /// <summary>SRV and TXT of instances named without them, A of hosts named without an address.</summary>
        public List<(string Name, ushort Type)> MissingQuestions()
        {
            lock (_lock)
            {
                var questions = new List<(string, ushort)>();
                foreach (var instance in _instances.Values.SelectMany(set => set).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!_servers.ContainsKey(instance))
                    {
                        questions.Add((instance, TypeSrv));
                        questions.Add((instance, TypeTxt));
                    }
                }

                foreach (var (target, _) in _servers.Values)
                {
                    if (!_addresses.ContainsKey(target))
                        questions.Add((target, TypeA));
                }

                return questions.Distinct().ToList();
            }
        }

        public IReadOnlyList<NetworkService> Build()
        {
            lock (_lock)
            {
                var services = new List<NetworkService>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var type in ServiceTypes)
                {
                    if (!_instances.TryGetValue(type.Name, out var instances))
                        continue;

                    foreach (var instance in instances)
                    {
                        if (!_servers.TryGetValue(instance, out var server))
                            continue;

                        var host = server.Target.TrimEnd('.');
                        var address = _addresses.GetValueOrDefault(server.Target)
                                      ?? _senders.GetValueOrDefault(instance)
                                      ?? host;

                        // The same server under _sftp-ssh and _ssh, or heard on two interfaces
                        if (!seen.Add($"{type.Protocol}|{address}|{server.Port}"))
                            continue;

                        var text = _texts.GetValueOrDefault(instance);
                        services.Add(new NetworkService(DisplayName(instance, type.Name), host, type.Protocol,
                            UriOf(type, address, server.Port, text)));
                    }
                }

                return services
                    .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(s => s.Protocol)
                    .ToList();
            }
        }

        private void Parse(byte[] data, string sender)
        {
            if (data.Length < 12 || (data[2] & 0x80) == 0) // A query, not an answer
                return;

            var questions = ReadUInt16(data, 4);
            var records = ReadUInt16(data, 6) + ReadUInt16(data, 8) + ReadUInt16(data, 10);
            var offset = 12;

            for (var i = 0; i < questions; i++)
            {
                ReadName(data, ref offset);
                offset += 4;
            }

            for (var i = 0; i < records; i++)
            {
                var name = ReadName(data, ref offset);
                var type = ReadUInt16(data, offset);
                var ttl = (data[offset + 4] << 24) | (data[offset + 5] << 16) | (data[offset + 6] << 8) | data[offset + 7];
                var length = ReadUInt16(data, offset + 8);
                var start = offset + 10;
                offset = start + length;
                if (offset > data.Length)
                    throw new FormatException("A record runs past the packet.");

                // TTL 0: a goodbye, the service is going away
                if (ttl == 0)
                    continue;

                switch (type)
                {
                    case TypePtr:
                    {
                        var position = start;
                        var instance = ReadName(data, ref position);
                        if (!_instances.TryGetValue(name, out var set))
                            _instances[name] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        set.Add(instance);
                        _senders.TryAdd(instance, sender);
                        break;
                    }
                    case TypeSrv when length >= 7:
                    {
                        var port = ReadUInt16(data, start + 4);
                        var position = start + 6;
                        _servers[name] = (ReadName(data, ref position), port);
                        _senders.TryAdd(name, sender);
                        break;
                    }
                    case TypeTxt:
                        _texts[name] = ReadText(data, start, length);
                        break;
                    case TypeA when length == 4:
                        _addresses[name] = new IPAddress(data.AsSpan(start, 4)).ToString();
                        break;
                }
            }
        }

        private static string DisplayName(string instance, string type)
        {
            var suffix = "." + type;
            var label = instance.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? instance[..^suffix.Length]
                : instance;
            return string.Join('.', SplitName(label));
        }

        private static string UriOf(ServiceType type, string host, int port, Dictionary<string, string>? text)
        {
            var authority = port == type.DefaultPort || port == 0 ? host : $"{host}:{port}";

            // WebDAV, FTP and NFS servers may say which folder they share
            var path = type.Protocol is NetworkProtocol.WebDav or NetworkProtocol.Ftp or NetworkProtocol.Nfs
                       && text?.GetValueOrDefault("path") is { Length: > 0 } announced
                ? announced
                : "/";

            if (!path.StartsWith('/'))
                path = "/" + path;

            return $"{type.Scheme}://{authority}{path}";
        }

        private static Dictionary<string, string> ReadText(byte[] data, int start, int length)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var position = start;
            while (position < start + length)
            {
                var size = data[position++];
                var entry = Encoding.UTF8.GetString(data, position, size);
                position += size;

                var eq = entry.IndexOf('=');
                if (eq > 0)
                    values.TryAdd(entry[..eq], entry[(eq + 1)..]);
            }

            return values;
        }

        private static string ReadName(byte[] data, ref int offset)
        {
            var labels = new List<string>();
            var position = offset;
            var jumped = false;
            var jumps = 0;

            while (true)
            {
                var length = data[position];
                if (length == 0)
                {
                    position++;
                    break;
                }

                // Compression: the rest of the name is elsewhere in the packet
                if ((length & 0xC0) == 0xC0)
                {
                    if (!jumped)
                        offset = position + 2;

                    jumped = true;
                    if (++jumps > 64)
                        throw new FormatException("A name points in a loop.");

                    position = ((length & 0x3F) << 8) | data[position + 1];
                    continue;
                }

                position++;
                labels.Add(EscapeLabel(Encoding.UTF8.GetString(data, position, length)));
                position += length;
            }

            if (!jumped)
                offset = position;

            return string.Join('.', labels);
        }

        private static int ReadUInt16(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];
    }
}
