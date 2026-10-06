using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace File.Commander.Presentation.Themes;

/// <summary>
/// The resources of a plugin theme: the same keys as Presentation/Styles/Theme*.axaml, built from its palette.
/// Keep both in step: a key missing here leaves that brush unset while a plugin theme is shown.
/// </summary>
internal static class PluginThemeResources
{
    public static ResourceDictionary Build(ThemePalette p, bool isDark)
    {
        var resources = new ResourceDictionary();

        // ===== The palette: each color, and a brush of it =====
        void Add(string name, Color color)
        {
            resources[name] = color;
            resources[name + "Brush"] = new SolidColorBrush(color);
        }

        Add("Rosewater", p.Rosewater);
        Add("Flamingo", p.Flamingo);
        Add("Pink", p.Pink);
        Add("Mauve", p.Mauve);
        Add("Red", p.Red);
        Add("Maroon", p.Maroon);
        Add("Peach", p.Peach);
        Add("Yellow", p.Yellow);
        Add("Green", p.Green);
        Add("Teal", p.Teal);
        Add("Sky", p.Sky);
        Add("Sapphire", p.Sapphire);
        Add("Blue", p.Blue);
        Add("Lavender", p.Lavender);
        Add("Text", p.Text);
        Add("Subtext1", p.Subtext1);
        Add("Subtext0", p.Subtext0);
        Add("Overlay2", p.Overlay2);
        Add("Overlay1", p.Overlay1);
        Add("Overlay0", p.Overlay0);
        Add("Surface2", p.Surface2);
        Add("Surface1", p.Surface1);
        Add("Surface0", p.Surface0);
        Add("Base", p.Base);
        Add("Mantle", p.Mantle);
        Add("Crust", p.Crust);

        // ===== Semantic aliases, mapped as in the Catppuccin themes =====
        // Text on the accent: the darkest background on a dark theme, the lightest on a light one (as Latte)
        var onAccent = isDark ? p.Crust : p.Base;

        void Brush(string key, Color color, double opacity = 1) => resources[key] = new SolidColorBrush(color, opacity);

        Brush("FcTitleBarBrush", p.Mantle);
        Brush("FcSidebarBrush", p.Mantle);
        Brush("FcContentBrush", p.Base);
        Brush("FcCardBrush", p.Surface0);
        Brush("FcCardHoverBrush", p.Surface1);
        Brush("FcCardPressedBrush", p.Surface2);
        Brush("FcAccentBrush", p.Blue);
        Brush("FcAccentHoverBrush", p.Lavender);
        Brush("FcAccentPressedBrush", p.Sapphire);
        Brush("FcOnAccentBrush", onAccent);
        Brush("FcTextBrush", p.Text);
        Brush("FcSubtleTextBrush", p.Subtext0);
        Brush("FcHeaderTextBrush", p.Overlay1);
        Brush("FcDisabledTextBrush", p.Overlay0);
        Brush("FcDividerBrush", p.Surface0);
        Brush("FcTrackBrush", p.Surface1);
        Brush("FcNavGroupBrush", p.Surface0);
        Brush("FcHoverBrush", p.Surface1);
        Brush("FcPressedBrush", p.Surface2);
        Brush("FcCutBrush", p.Blue, 0.35);
        Brush("FcOverlayHoverBrush", Color.FromArgb(0x26, 0, 0, 0));
        Brush("FcOverlayPressedBrush", Color.FromArgb(0x40, 0, 0, 0));
        // Dims the main window behind the Settings card: the darkest background, or the text on a light theme
        Brush("FcBackdropBrush", isDark ? WithAlpha(p.Crust, 0x8C) : WithAlpha(p.Text, 0x73));

        resources["FcFolderBodyBrush"] = Vertical(p.Sapphire, p.Blue);
        Brush("FcFolderTabBrush", p.Lavender);
        Brush("FcFolderBadgeBrush", p.Base);

        resources["FcUsageNormalBrush"] = Horizontal(p.Sapphire, p.Blue);
        resources["FcUsageWarningBrush"] = Horizontal(p.Peach, p.Yellow);

        resources["FcDiskBodyBrush"] = Vertical(p.Green, p.Teal);
        resources["FcDiskBodyRemovableBrush"] = Vertical(p.Lavender, p.Mauve);
        resources["FcOpticalDiscBrush"] = new ConicGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(p.Sky, 0.00),
                new GradientStop(p.Pink, 0.25),
                new GradientStop(p.Rosewater, 0.50),
                new GradientStop(p.Lavender, 0.75),
                new GradientStop(p.Sky, 1.00),
            },
        };

        // ===== Fluent check boxes in the palette's accent instead of the system accent =====
        Brush("CheckBoxCheckBackgroundFillChecked", p.Blue);
        Brush("CheckBoxCheckBackgroundFillCheckedPointerOver", p.Lavender);
        Brush("CheckBoxCheckBackgroundFillCheckedPressed", p.Sapphire);
        Brush("CheckBoxCheckBackgroundStrokeChecked", p.Blue);
        Brush("CheckBoxCheckBackgroundStrokeCheckedPointerOver", p.Lavender);
        Brush("CheckBoxCheckBackgroundStrokeCheckedPressed", p.Sapphire);
        Brush("CheckBoxCheckGlyphForegroundChecked", onAccent);
        Brush("CheckBoxCheckGlyphForegroundCheckedPointerOver", onAccent);
        Brush("CheckBoxCheckGlyphForegroundCheckedPressed", onAccent);

        return resources;
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static LinearGradientBrush Vertical(Color top, Color bottom) => Gradient(top, bottom, new RelativePoint(0, 1, RelativeUnit.Relative));

    private static LinearGradientBrush Horizontal(Color left, Color right) => Gradient(left, right, new RelativePoint(1, 0, RelativeUnit.Relative));

    private static LinearGradientBrush Gradient(Color from, Color to, RelativePoint end) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = end,
        GradientStops =
        {
            new GradientStop(from, 0),
            new GradientStop(to, 1),
        },
    };
}
