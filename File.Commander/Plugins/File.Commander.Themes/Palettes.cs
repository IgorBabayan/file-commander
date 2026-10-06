using Avalonia.Media;
using File.Commander.Plugins;

namespace File.Commander.Themes;

/// <summary>
/// The themes, mapped onto the app's palette slots by role: Blue is the accent, Base/Mantle/Crust the backgrounds,
/// Surface0–2 cards and hover fills, Overlay0–2 muted glyphs, Subtext0/1 and Text the text.
/// </summary>
internal static class Palettes
{
    public static IReadOnlyList<PluginTheme> All { get; } =
    [
        new("nord", "Nord", true, new ThemePalette
        {
            Rosewater = C("#e5e9f0"), Flamingo = C("#d3a4a8"), Pink = C("#c895bf"), Mauve = C("#b48ead"),
            Red = C("#bf616a"), Maroon = C("#c5727a"), Peach = C("#d08770"), Yellow = C("#ebcb8b"),
            Green = C("#a3be8c"), Teal = C("#8fbcbb"), Sky = C("#93ccdc"), Sapphire = C("#81a1c1"),
            Blue = C("#88c0d0"), Lavender = C("#a3b8d9"),
            Text = C("#eceff4"), Subtext1 = C("#e5e9f0"), Subtext0 = C("#d8dee9"),
            Overlay2 = C("#aeb6c4"), Overlay1 = C("#8f98a8"), Overlay0 = C("#6f7a8d"),
            Surface2 = C("#4c566a"), Surface1 = C("#434c5e"), Surface0 = C("#3b4252"),
            Base = C("#2e3440"), Mantle = C("#292e39"), Crust = C("#242933"),
        }),
        new("dracula", "Dracula", true, new ThemePalette
        {
            Rosewater = C("#f5d0e0"), Flamingo = C("#ff9fd5"), Pink = C("#ff79c6"), Mauve = C("#caa9fa"),
            Red = C("#ff5555"), Maroon = C("#ff6e6e"), Peach = C("#ffb86c"), Yellow = C("#f1fa8c"),
            Green = C("#50fa7b"), Teal = C("#80ffea"), Sky = C("#a4f0ff"), Sapphire = C("#8be9fd"),
            Blue = C("#bd93f9"), Lavender = C("#d6c4fc"),
            Text = C("#f8f8f2"), Subtext1 = C("#e2e2dc"), Subtext0 = C("#c8c8c2"),
            Overlay2 = C("#9ea8c7"), Overlay1 = C("#7f8ab5"), Overlay0 = C("#6272a4"),
            Surface2 = C("#565a73"), Surface1 = C("#4b4e66"), Surface0 = C("#44475a"),
            Base = C("#282a36"), Mantle = C("#21222c"), Crust = C("#191a21"),
        }),
        new("gruvbox-dark", "Gruvbox Dark", true, new ThemePalette
        {
            Rosewater = C("#ebc9a8"), Flamingo = C("#e8a08a"), Pink = C("#e39bb0"), Mauve = C("#d3869b"),
            Red = C("#fb4934"), Maroon = C("#cc241d"), Peach = C("#fe8019"), Yellow = C("#fabd2f"),
            Green = C("#b8bb26"), Teal = C("#8ec07c"), Sky = C("#89b8a8"), Sapphire = C("#7daea3"),
            Blue = C("#83a598"), Lavender = C("#9cb8ae"),
            Text = C("#ebdbb2"), Subtext1 = C("#d5c4a1"), Subtext0 = C("#bdae93"),
            Overlay2 = C("#a89984"), Overlay1 = C("#928374"), Overlay0 = C("#7c6f64"),
            Surface2 = C("#665c54"), Surface1 = C("#504945"), Surface0 = C("#3c3836"),
            Base = C("#282828"), Mantle = C("#1d2021"), Crust = C("#141617"),
        }),
        new("gruvbox-light", "Gruvbox Light", false, new ThemePalette
        {
            Rosewater = C("#a5594a"), Flamingo = C("#b4545f"), Pink = C("#b16286"), Mauve = C("#8f3f71"),
            Red = C("#9d0006"), Maroon = C("#cc241d"), Peach = C("#af3a03"), Yellow = C("#b57614"),
            Green = C("#79740e"), Teal = C("#427b58"), Sky = C("#458588"), Sapphire = C("#3a7c80"),
            Blue = C("#076678"), Lavender = C("#45707a"),
            Text = C("#3c3836"), Subtext1 = C("#504945"), Subtext0 = C("#665c54"),
            Overlay2 = C("#7c6f64"), Overlay1 = C("#928374"), Overlay0 = C("#a89984"),
            Surface2 = C("#bdae93"), Surface1 = C("#d5c4a1"), Surface0 = C("#ebdbb2"),
            Base = C("#fbf1c7"), Mantle = C("#f2e5bc"), Crust = C("#e5d4a6"),
        }),
        new("tokyo-night", "Tokyo Night", true, new ThemePalette
        {
            Rosewater = C("#e8c6d0"), Flamingo = C("#f0a6b8"), Pink = C("#e58fd1"), Mauve = C("#bb9af7"),
            Red = C("#f7768e"), Maroon = C("#db4b4b"), Peach = C("#ff9e64"), Yellow = C("#e0af68"),
            Green = C("#9ece6a"), Teal = C("#73daca"), Sky = C("#7dcfff"), Sapphire = C("#2ac3de"),
            Blue = C("#7aa2f7"), Lavender = C("#b4c2f8"),
            Text = C("#c0caf5"), Subtext1 = C("#a9b1d6"), Subtext0 = C("#9aa5ce"),
            Overlay2 = C("#848cb5"), Overlay1 = C("#737aa2"), Overlay0 = C("#565f89"),
            Surface2 = C("#414868"), Surface1 = C("#343a52"), Surface0 = C("#292e42"),
            Base = C("#1a1b26"), Mantle = C("#16161e"), Crust = C("#101014"),
        }),
    ];

    private static Color C(string hex) => Color.Parse(hex);
}
