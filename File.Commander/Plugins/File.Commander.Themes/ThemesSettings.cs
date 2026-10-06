namespace File.Commander.Themes;

/// <summary>Stored as JSON in the plugin's data folder.</summary>
public sealed record ThemesSettings
{
    /// <summary>Ids of the themes left out of the Theme drop-down. New themes are listed until hidden.</summary>
    public string[] Hidden { get; init; } = [];
}
