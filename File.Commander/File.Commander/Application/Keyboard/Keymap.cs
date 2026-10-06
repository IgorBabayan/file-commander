namespace File.Commander.Application.Keyboard;

/// <summary>
/// The bindings in effect: every action's defaults, replaced by what settings.json overrides.
/// Immutable; build a new one when the settings change.
/// </summary>
public sealed class Keymap
{
    private readonly Dictionary<string, IReadOnlyList<KeyChord>> _byAction;
    private readonly Dictionary<KeyChord, KeymapAction> _byChord = new();

    private Keymap(Dictionary<string, IReadOnlyList<KeyChord>> byAction)
    {
        _byAction = byAction;

        // A hand-edited file may bind one chord twice: the action listed first wins
        foreach (var action in KeymapActions.All)
        {
            foreach (var chord in byAction[action.Id])
                _byChord.TryAdd(chord, action);
        }
    }

    public static Keymap Default { get; } = From(null);

    /// <param name="overrides">
    /// Settings → Keymap: action id → its chords, replacing the defaults. An empty list unbinds the action.
    /// Unknown ids and unreadable chords are ignored.
    /// </param>
    public static Keymap From(IReadOnlyDictionary<string, List<string>>? overrides)
    {
        var byAction = new Dictionary<string, IReadOnlyList<KeyChord>>();
        foreach (var action in KeymapActions.All)
        {
            byAction[action.Id] = overrides?.TryGetValue(action.Id, out var stored) == true
                ? Parse(stored)
                : action.Defaults;
        }

        return new Keymap(byAction);
    }

    public IReadOnlyList<KeyChord> For(string actionId) => _byAction.GetValueOrDefault(actionId) ?? [];

    public KeymapAction? Find(KeyChord chord) => _byChord.GetValueOrDefault(chord);

    private static IReadOnlyList<KeyChord> Parse(IEnumerable<string> stored)
    {
        var chords = new List<KeyChord>();
        foreach (var text in stored)
        {
            if (!KeyChord.TryParse(text, out var chord))
                Trace.WriteLine($"Keymap: ignoring unreadable shortcut '{text}'");
            else if (!chords.Contains(chord))
                chords.Add(chord);
        }

        return chords;
    }
}

/// <summary>The keymap of the stored settings, rebuilt only when Settings → Keymap changes.</summary>
public interface IKeymapService
{
    Keymap Current { get; }
}

sealed class KeymapService(ISettingsService settings) : IKeymapService
{
    private Dictionary<string, List<string>>? _source;
    private Keymap? _keymap;

    // UI thread only, like every reader of the settings
    public Keymap Current
    {
        get
        {
            var source = settings.Current.Keymap;
            if (_keymap is null || !ReferenceEquals(source, _source))
            {
                _keymap = Keymap.From(source);
                _source = source;
            }

            return _keymap;
        }
    }
}
