namespace File.Commander.Presentation.Services;

/// <summary>What a copy or move does when the name an item would get in the target folder is taken.</summary>
public enum NameConflict
{
    /// <summary>It gets a number: "report (2).pdf". Nothing is overwritten.</summary>
    KeepBoth,

    /// <summary>The item that has the name is replaced.</summary>
    Replace,

    /// <summary>It isn't copied or moved; it stays where it is.</summary>
    Skip,
}

/// <summary>One item of a copy or move done by <see cref="FileOperations"/>.</summary>
/// <param name="Source">The file or folder to copy or move.</param>
/// <param name="Name">Its name in the target folder. Null: its own.</param>
/// <param name="OnConflict">What happens when that name is taken.</param>
public sealed record TransferItem(string Source, string? Name = null, NameConflict OnConflict = NameConflict.KeepBoth);
