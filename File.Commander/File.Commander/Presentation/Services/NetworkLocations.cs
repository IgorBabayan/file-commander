using System.Diagnostics.CodeAnalysis;

namespace File.Commander.Presentation.Services;

public enum NetworkProtocol { Smb, Sftp, Ftp, WebDav, Nfs, Other }

/// <summary>Who mounted a network share, which decides how it is disconnected.</summary>
public enum NetworkMountKind
{
    /// <summary>GVfs (Connect to server here or in another file manager): a folder under its FUSE root.</summary>
    Gvfs,

    /// <summary>A FUSE filesystem of the user's (sshfs, rclone…): fusermount unmounts it.</summary>
    Fuse,

    /// <summary>A kernel mount (cifs, nfs…), usually from fstab: umount, which may need root.</summary>
    Kernel,
}

/// <summary>A network share that is mounted on this computer and can be browsed like a folder.</summary>
public sealed record NetworkMount(string Name, string Server, NetworkProtocol Protocol, string MountPoint,
    NetworkMountKind Kind);

/// <summary>
/// The network shares mounted on this computer (GVfs mounts and network filesystems of the mount table),
/// and the server addresses Connect to server accepts.
/// </summary>
public static class NetworkLocations
{
    private const string GvfsFormat = "fuse.gvfsd-fuse";

    private static readonly HashSet<string> NetworkFormats = new(StringComparer.Ordinal)
    {
        "cifs", "smb3", "smbfs", "nfs", "nfs4", "9p", "ceph", "afs", "davfs", "glusterfs",
        "fuse.sshfs", "fuse.rclone", "fuse.s3fs", "fuse.glusterfs", "fuse.curlftpfs", "fuse.smbnetfs", "fuse.gcsfuse",
    };

    // The schemes GVfs can mount as a folder
    private static readonly Dictionary<string, NetworkProtocol> Schemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["smb"] = NetworkProtocol.Smb,
        ["sftp"] = NetworkProtocol.Sftp,
        ["ssh"] = NetworkProtocol.Sftp,
        ["ftp"] = NetworkProtocol.Ftp,
        ["ftps"] = NetworkProtocol.Ftp,
        ["dav"] = NetworkProtocol.WebDav,
        ["davs"] = NetworkProtocol.WebDav,
        ["nfs"] = NetworkProtocol.Nfs,
        ["afp"] = NetworkProtocol.Other,
    };

    /// <summary>"smb://nas/media", "sftp://me@server"…: an address Connect to server can mount.</summary>
    public static bool IsRemoteUri(string text) => TryParseUri(text, out _);

    public static bool TryParseUri(string text, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed)
            || !Schemes.ContainsKey(parsed.Scheme)
            || string.IsNullOrEmpty(parsed.Host))
            return false;

        uri = parsed;
        return true;
    }

    public static NetworkProtocol ProtocolOf(string scheme)
        => Schemes.TryGetValue(scheme, out var protocol) ? protocol : NetworkProtocol.Other;

    /// <summary>How the Network page names a protocol.</summary>
    public static string NameOf(NetworkProtocol protocol) => protocol switch
    {
        NetworkProtocol.Smb => "Windows share",
        NetworkProtocol.Sftp => "SFTP",
        NetworkProtocol.Ftp => "FTP",
        NetworkProtocol.WebDav => "WebDAV",
        NetworkProtocol.Nfs => "NFS",
        _ => "Network",
    };

    /// <summary>Every network share mounted now, by name. Read on every visit of the Network page.</summary>
    public static IReadOnlyList<NetworkMount> GetMounts()
    {
        var mounts = new List<NetworkMount>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (device, mountPoint, format) in SystemLocations.GetMountTable())
        {
            if (format == GvfsFormat)
            {
                foreach (var mount in ReadGvfsMounts(mountPoint))
                {
                    if (seen.Add(mount.MountPoint))
                        mounts.Add(mount);
                }

                continue;
            }

            if (IsNetworkFormat(device, format) && seen.Add(mountPoint))
                mounts.Add(FromKernelMount(device, mountPoint, format));
        }

        return mounts
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The network share <paramref name="location"/> is in (the innermost one), or null. Only reads the GVfs folder's
    /// name, not the folder, so it is cheap enough for every navigation.
    /// </summary>
    public static NetworkMount? FindMount(string location)
    {
        if (Locations.IsVirtual(location))
            return null;

        location = Locations.Normalize(location);
        NetworkMount? best = null;

        foreach (var (device, mountPoint, format) in SystemLocations.GetMountTable())
        {
            NetworkMount? candidate = null;
            if (format == GvfsFormat)
            {
                // The GVfs root itself isn't a share: each folder in it is one
                if (!IsUnder(location, mountPoint) || location == mountPoint)
                    continue;

                var entry = location[(mountPoint.Length + 1)..].Split('/', 2)[0];
                candidate = FromGvfsEntry(mountPoint, entry);
            }
            else if (IsNetworkFormat(device, format) && IsUnder(location, mountPoint))
            {
                candidate = FromKernelMount(device, mountPoint, format);
            }

            if (candidate is not null && (best is null || candidate.MountPoint.Length > best.MountPoint.Length))
                best = candidate;
        }

        return best;
    }

    private static bool IsNetworkFormat(string device, string format)
        => NetworkFormats.Contains(format)
           || (format.StartsWith("fuse", StringComparison.Ordinal) && device.Contains("://", StringComparison.Ordinal));

    private static IEnumerable<NetworkMount> ReadGvfsMounts(string root)
    {
        List<string> entries;
        try
        {
            entries = Directory.EnumerateDirectories(root).Select(IOPath.GetFileName).OfType<string>().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            if (FromGvfsEntry(root, entry) is { } mount)
                yield return mount;
        }
    }

    /// <summary>
    /// GVfs names its folders after the mount: "smb-share:server=nas,share=media", "sftp:host=server,user=me".
    /// Null for what isn't a network share (phones, cameras, archives…).
    /// </summary>
    private static NetworkMount? FromGvfsEntry(string root, string entry)
    {
        var colon = entry.IndexOf(':');
        if (colon <= 0)
            return null;

        var type = entry[..colon];
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in entry[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0)
                keys[pair[..eq]] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }

        var host = Get("host");
        var path = IOPath.Combine(root, entry);

        return type switch
        {
            "smb-share" => Mount(On(Get("share"), Get("server")), Get("server"), NetworkProtocol.Smb),
            "sftp" => Mount(UserAt(Get("user"), host), host, NetworkProtocol.Sftp),
            "ftp" or "ftps" => Mount(UserAt(Get("user"), host), host, NetworkProtocol.Ftp),
            "dav" or "davs" => Mount(On(LastSegment(Get("prefix")), host), host, NetworkProtocol.WebDav),
            "nfs" => Mount(On(LastSegment(Get("prefix")), host), host, NetworkProtocol.Nfs),
            "afp-volume" => Mount(On(Get("volume"), host), host, NetworkProtocol.Other),
            "google-drive" or "onedrive" => Mount(UserAt(Get("user"), host), host, NetworkProtocol.Other),
            _ => null,
        };

        string Get(string key) => keys.TryGetValue(key, out var value) ? value : string.Empty;

        NetworkMount Mount(string name, string server, NetworkProtocol protocol)
            => new(name.Length > 0 ? name : entry, server, protocol, path, NetworkMountKind.Gvfs);
    }

    private static NetworkMount FromKernelMount(string device, string mountPoint, string format)
    {
        var kind = format.StartsWith("fuse", StringComparison.Ordinal) ? NetworkMountKind.Fuse : NetworkMountKind.Kernel;
        var folderName = IOPath.GetFileName(mountPoint) is { Length: > 0 } name ? name : mountPoint;

        switch (format)
        {
            case "cifs" or "smb3" or "smbfs":
            {
                // "//server/share[/folder]"
                var parts = device.TrimStart('/').Split('/', 2);
                var share = parts.Length > 1 ? parts[1].TrimEnd('/') : string.Empty;
                return new NetworkMount(On(share, parts[0]), parts[0], NetworkProtocol.Smb, mountPoint, kind);
            }
            case "nfs" or "nfs4":
            {
                // "server:/export", "[fd00::1]:/export"
                var split = device.IndexOf(":/", StringComparison.Ordinal);
                if (split > 0)
                {
                    var server = device[..split].Trim('[', ']');
                    return new NetworkMount(On(LastSegment(device[(split + 1)..]), server), server,
                        NetworkProtocol.Nfs, mountPoint, kind);
                }

                break;
            }
            case "fuse.sshfs":
            {
                // "me@server:/path", "server:"
                var split = device.LastIndexOf(':');
                if (split > 0)
                {
                    var login = device[..split];
                    var server = login[(login.IndexOf('@') + 1)..];
                    var folder = LastSegment(device[(split + 1)..]);
                    return new NetworkMount(folder.Length > 0 ? On(folder, server) : login, server,
                        NetworkProtocol.Sftp, mountPoint, kind);
                }

                break;
            }
            case "fuse.rclone":
            {
                // "remote:path": the remote is named in rclone's config
                var remote = device.Split(':', 2)[0];
                return new NetworkMount(On(LastSegment(device[(remote.Length + 1)..]), remote), remote,
                    NetworkProtocol.Other, mountPoint, kind);
            }
        }

        // davfs2 and other URL-mounted filesystems: "https://cloud.example.com/remote.php/webdav"
        if (Uri.TryCreate(device, UriKind.Absolute, out var url) && url.Host.Length > 0)
        {
            var protocol = url.Scheme is "http" or "https" ? NetworkProtocol.WebDav : ProtocolOf(url.Scheme);
            return new NetworkMount(On(LastSegment(url.AbsolutePath), url.Host), url.Host, protocol, mountPoint, kind);
        }

        return new NetworkMount(folderName, device, NetworkProtocol.Other, mountPoint, kind);
    }

    /// <summary>"media on nas", like other file managers name a share; just the server when there's no share.</summary>
    private static string On(string share, string server)
        => share.Length == 0 ? server : server.Length == 0 ? share : $"{share} on {server}";

    private static string UserAt(string user, string host) => user.Length == 0 ? host : $"{user}@{host}";

    private static string LastSegment(string path) => IOPath.GetFileName(path.TrimEnd('/'));

    private static bool IsUnder(string path, string root)
        => path == root || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal);
}
