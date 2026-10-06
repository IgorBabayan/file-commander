namespace File.Commander.Domain.Config;

/// <summary>
/// The items of the title bar's toolbar, as set with Customize Toolbar… (a right click on the title bar).
/// Replace the list, never mutate it: the shell compares settings by reference.
/// </summary>
public sealed record ToolbarSettings
{
    /// <summary>
    /// Item ids left to right ("back", "address", "flexible-space"…). Null: the built-in layout.
    /// Unknown ids are ignored; the address bar is always shown, at its default place when it isn't listed.
    /// </summary>
    public IReadOnlyList<string>? Items { get; init; }
}
