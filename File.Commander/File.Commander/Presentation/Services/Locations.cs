namespace File.Commander.Presentation.Services;

/// <summary>Virtual locations and path comparison used by navigation and the sidebar.</summary>
public static class Locations
{
    public const string Computer = "computer://";
    public const string Recent = "recent://";
    public const string Trash = "trash://";
    public const string Network = "network://";

    public static bool IsVirtual(string location) => location.Contains("://", StringComparison.Ordinal);

    /// <summary>"/home/user/" → "/home/user", "/" stays "/". Virtual locations are returned as is.</summary>
    public static string Normalize(string location)
    {
        if (IsVirtual(location))
            return location;

        var trimmed = location.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    public static bool AreEqual(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
}
