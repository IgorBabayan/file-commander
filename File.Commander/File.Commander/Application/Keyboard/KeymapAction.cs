using Avalonia.Input;

namespace File.Commander.Application.Keyboard;

/// <summary>Something a shortcut can do. Listed in Settings → Basic → Keymap under <see cref="Group"/>.</summary>
/// <param name="Id">Key in settings.json. Never rename it: stored bindings would be lost.</param>
/// <param name="WorksWhileTyping">
/// False: ignored while a text box has focus (Backspace, Alt+Left… belong to the text box there).
/// Even when true, a chord that types text (no Ctrl/Alt/Super) is left to the text box.
/// </param>
public sealed record KeymapAction(
    string Id,
    string Group,
    string Title,
    IReadOnlyList<KeyChord> Defaults,
    bool WorksWhileTyping = true);

/// <summary>Every action a shortcut can be bound to, with its default chords.</summary>
public static class KeymapActions
{
    public const string ActionsGroup = "Actions";
    public const string NavigationGroup = "Navigation";
    public const string ViewGroup = "View";
    public const string AppearanceGroup = "Appearance";

    public const string ToggleHiddenFiles = "actions.toggle-hidden-files";
    public const string Refresh = "actions.refresh";
    public const string OpenSettings = "actions.open-settings";
    public const string ToggleInfoPanel = "actions.toggle-info-panel";

    public const string EditPath = "navigation.edit-path";
    public const string GoBack = "navigation.back";
    public const string GoForward = "navigation.forward";
    public const string GoUp = "navigation.up";
    public const string GoComputer = "navigation.computer";

    public const string GridView = "view.grid";
    public const string ListView = "view.list";
    public const string TreeView = "view.tree";

    public const string ThemeLatte = "appearance.theme-latte";
    public const string ThemeFrappe = "appearance.theme-frappe";
    public const string ThemeMacchiato = "appearance.theme-macchiato";
    public const string ThemeMocha = "appearance.theme-mocha";

    /// <summary>The order the groups are shown in.</summary>
    public static IReadOnlyList<string> Groups { get; } =
        [ActionsGroup, NavigationGroup, ViewGroup, AppearanceGroup];

    public static IReadOnlyList<KeymapAction> All { get; } =
    [
        new(ToggleHiddenFiles, ActionsGroup, "Show hidden files",
            [new(Key.H, KeyModifiers.Control)], WorksWhileTyping: false),
        new(Refresh, ActionsGroup, "Refresh", [new(Key.F5)]),
        new(OpenSettings, ActionsGroup, "Open settings", [new(Key.OemComma, KeyModifiers.Control)]),
        // Like Finder's Quick Look. Never while typing: Space belongs to the text box there.
        new(ToggleInfoPanel, ActionsGroup, "Show info panel", [new(Key.Space)], WorksWhileTyping: false),

        new(EditPath, NavigationGroup, "Edit path",
            [new(Key.L, KeyModifiers.Control), new(Key.D, KeyModifiers.Alt)]),
        new(GoBack, NavigationGroup, "Go back", [new(Key.Left, KeyModifiers.Alt)], WorksWhileTyping: false),
        new(GoForward, NavigationGroup, "Go forward", [new(Key.Right, KeyModifiers.Alt)], WorksWhileTyping: false),
        new(GoUp, NavigationGroup, "Go up",
            [new(Key.Up, KeyModifiers.Alt), new(Key.Back)], WorksWhileTyping: false),
        new(GoComputer, NavigationGroup, "Open Computer", [], WorksWhileTyping: false),

        new(GridView, ViewGroup, "Icons view", [new(Key.D1, KeyModifiers.Control)]),
        new(ListView, ViewGroup, "List view", [new(Key.D2, KeyModifiers.Control)]),
        new(TreeView, ViewGroup, "Tree view", [new(Key.D3, KeyModifiers.Control)]),

        new(ThemeLatte, AppearanceGroup, "Select theme Latte",
            [new(Key.D1, KeyModifiers.Control | KeyModifiers.Alt)]),
        new(ThemeFrappe, AppearanceGroup, "Select theme Frappé",
            [new(Key.D2, KeyModifiers.Control | KeyModifiers.Alt)]),
        new(ThemeMacchiato, AppearanceGroup, "Select theme Macchiato",
            [new(Key.D3, KeyModifiers.Control | KeyModifiers.Alt)]),
        new(ThemeMocha, AppearanceGroup, "Select theme Mocha",
            [new(Key.D4, KeyModifiers.Control | KeyModifiers.Alt)]),
    ];

    private static readonly Dictionary<string, KeymapAction> ById = All.ToDictionary(a => a.Id);

    public static KeymapAction? Find(string id) => ById.GetValueOrDefault(id);
}
