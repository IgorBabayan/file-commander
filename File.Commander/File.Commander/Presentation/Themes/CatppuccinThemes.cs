using Avalonia.Styling;

namespace File.Commander.Presentation.Themes;

/// <summary>
/// One theme variant per Catppuccin flavor. Each is a key of App.axaml's ThemeDictionaries
/// (Presentation/Styles/Theme*.axaml) and inherits Light or Dark, so Fluent's own controls
/// pick the matching base look.
/// </summary>
public static class CatppuccinThemes
{
    public static ThemeVariant Latte { get; } = new("CatppuccinLatte", ThemeVariant.Light);
    public static ThemeVariant Frappe { get; } = new("CatppuccinFrappe", ThemeVariant.Dark);
    public static ThemeVariant Macchiato { get; } = new("CatppuccinMacchiato", ThemeVariant.Dark);
    public static ThemeVariant Mocha { get; } = new("CatppuccinMocha", ThemeVariant.Dark);

    public static ThemeVariant For(AppTheme theme) => theme switch
    {
        AppTheme.Latte => Latte,
        AppTheme.Frappe => Frappe,
        AppTheme.Macchiato => Macchiato,
        _ => Mocha,
    };
}
