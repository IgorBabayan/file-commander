using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace File.Commander.Application.FileSystem;

/// <summary>What the current user may do with a file, as access(2) reports it.</summary>
public readonly record struct EffectiveAccess(bool CanRead, bool CanWrite);

/// <summary>
/// Owner, group and effective access of a file: things .NET doesn't expose on Linux.
/// Every method returns null instead of throwing when libc or the file can't tell.
/// </summary>
public static class UnixFileAccess
{
    // statx(2): its struct has the same layout on every architecture, unlike stat(2)
    private const int AtFdCwd = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const uint StatxUid = 0x8;
    private const uint StatxGid = 0x10;
    private const int StatxSize = 256;
    private const int UidOffset = 20;
    private const int GidOffset = 24;

    private const int ReadOk = 4;
    private const int WriteOk = 2;

    private static readonly ConcurrentDictionary<uint, string> UserNames = new();
    private static readonly ConcurrentDictionary<uint, string> GroupNames = new();

    // getpwuid/getgrgid return a static buffer shared by all threads
    private static readonly Lock AccountsLock = new();

    private static bool _unavailable = !OperatingSystem.IsLinux();

    /// <summary>The names of the owning user and group, e.g. ("john", "staff"). Doesn't follow symlinks.</summary>
    public static (string Owner, string Group)? GetOwnership(string path)
    {
        if (_unavailable)
            return null;

        try
        {
            var buffer = new byte[StatxSize];
            if (statx(AtFdCwd, path, AtSymlinkNoFollow, StatxUid | StatxGid, buffer) != 0)
                return null;

            var uid = BitConverter.ToUInt32(buffer, UidOffset);
            var gid = BitConverter.ToUInt32(buffer, GidOffset);
            return (UserName(uid), GroupName(gid));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // musl without statx, or not a libc we know: stop trying
            _unavailable = true;
            return null;
        }
    }

    /// <summary>Whether the current user can read and write it. Follows symlinks, as opening the file would.</summary>
    public static EffectiveAccess? GetEffectiveAccess(string path)
    {
        if (_unavailable)
            return null;

        try
        {
            return new EffectiveAccess(access(path, ReadOk) == 0, access(path, WriteOk) == 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true;
            return null;
        }
    }

    private static string UserName(uint uid) => UserNames.GetOrAdd(uid, static id =>
    {
        lock (AccountsLock)
        {
            // struct passwd starts with char* pw_name on every libc
            var entry = getpwuid(id);
            return entry == IntPtr.Zero ? id.ToString() : Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(entry)) ?? id.ToString();
        }
    });

    private static string GroupName(uint gid) => GroupNames.GetOrAdd(gid, static id =>
    {
        lock (AccountsLock)
        {
            // struct group starts with char* gr_name on every libc
            var entry = getgrgid(id);
            return entry == IntPtr.Zero ? id.ToString() : Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(entry)) ?? id.ToString();
        }
    });

#pragma warning disable SYSLIB1054 // byte[] and string marshalling is all we need here
    [DllImport("libc", SetLastError = true)]
    private static extern int statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask,
        byte[] buffer);

    [DllImport("libc", SetLastError = true)]
    private static extern int access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);

    [DllImport("libc")]
    private static extern IntPtr getpwuid(uint uid);

    [DllImport("libc")]
    private static extern IntPtr getgrgid(uint gid);
#pragma warning restore SYSLIB1054
}
