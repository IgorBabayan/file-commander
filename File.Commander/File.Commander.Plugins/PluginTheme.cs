using Avalonia.Media;

namespace File.Commander.Plugins;

/// <summary>A color theme a plugin adds to the Theme drop-down of Settings → Basic → Appearance.</summary>
/// <param name="Id">
/// Unique within the plugin and stable: the pick is stored in settings.json as "&lt;plugin id&gt;/&lt;theme id&gt;".
/// Never rename it, or users who picked the theme fall back to a built-in one.
/// </param>
/// <param name="Title">What the drop-down shows, e.g. "Nord".</param>
/// <param name="IsDark">
/// Picks the base look of the stock controls (Fluent's Dark or Light) and how the host derives the colors
/// that aren't in <see cref="Palette"/>: text on the accent and the backdrop behind dialogs.
/// </param>
/// <param name="Palette">The colors. The host builds every brush of the app from them.</param>
public sealed record PluginTheme(string Id, string Title, bool IsDark, ThemePalette Palette);

/// <summary>
/// The 26 colors of a theme, named after the Catppuccin palette slots the app is styled with. Map your theme's
/// colors by role rather than by name: <see cref="Blue"/> is the accent (selection, checked boxes, links),
/// <see cref="Lavender"/> its hover and <see cref="Sapphire"/> its pressed state; <see cref="Base"/>,
/// <see cref="Mantle"/> and <see cref="Crust"/> are the backgrounds, from the content down to the darkest
/// (lightest-to-darker on a light theme); <see cref="Surface0"/>…<see cref="Surface2"/> are cards and
/// hover/pressed fills; <see cref="Overlay0"/>…<see cref="Overlay2"/> muted glyphs and hints;
/// <see cref="Subtext0"/>, <see cref="Subtext1"/> and <see cref="Text"/> the text, from dimmest to brightest.
/// </summary>
/// <remarks>Use <c>Color.Parse("#88c0d0")</c> to write the colors as hex.</remarks>
public sealed record ThemePalette
{
    public required Color Rosewater { get; init; }
    public required Color Flamingo { get; init; }
    public required Color Pink { get; init; }
    public required Color Mauve { get; init; }
    public required Color Red { get; init; }
    public required Color Maroon { get; init; }
    public required Color Peach { get; init; }
    public required Color Yellow { get; init; }
    public required Color Green { get; init; }
    public required Color Teal { get; init; }
    public required Color Sky { get; init; }
    public required Color Sapphire { get; init; }
    public required Color Blue { get; init; }
    public required Color Lavender { get; init; }

    public required Color Text { get; init; }
    public required Color Subtext1 { get; init; }
    public required Color Subtext0 { get; init; }

    public required Color Overlay2 { get; init; }
    public required Color Overlay1 { get; init; }
    public required Color Overlay0 { get; init; }

    public required Color Surface2 { get; init; }
    public required Color Surface1 { get; init; }
    public required Color Surface0 { get; init; }

    public required Color Base { get; init; }
    public required Color Mantle { get; init; }
    public required Color Crust { get; init; }
}
