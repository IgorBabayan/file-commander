using Avalonia.Input;

namespace File.Commander.Application.Keyboard;

/// <summary>
/// One key with its modifiers, e.g. Ctrl+Shift+V. Stored in settings.json as its <see cref="ToString"/> text.
/// Num pad digits count as the digit keys, so Ctrl+1 also fires on Ctrl+Num 1.
/// </summary>
public readonly record struct KeyChord
{
    private const KeyModifiers SupportedModifiers =
        KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta;

    // Key has several names per value (Enter/Return, PageUp/Prior…): only one of each is listed here
    private static readonly Dictionary<Key, string> DisplayNames = BuildDisplayNames();

    private static readonly Dictionary<string, Key> KeysByName = BuildKeysByName();

    public KeyChord(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Key = Normalize(key);
        Modifiers = modifiers & SupportedModifiers;
    }

    public Key Key { get; }

    public KeyModifiers Modifiers { get; }

    /// <summary>
    /// Has Ctrl, Alt or Super, or is a function key: such a chord doesn't type text,
    /// so it may fire while a text box has focus.
    /// </summary>
    public bool IsCommandChord =>
        (Modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0
        || Key is >= Key.F1 and <= Key.F24;

    /// <summary>The chord of a key press, or null for a modifier alone (Ctrl, Shift…).</summary>
    public static KeyChord? FromKeyPress(Key key, KeyModifiers modifiers)
        => IsModifierOrLock(key) ? null : new KeyChord(key, modifiers);

    /// <summary>Modifiers first, then the key: ["Ctrl", "Shift", "V"]. One key cap each in the UI.</summary>
    public string[] Parts()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Super");
        parts.Add(NameOf(Key));
        return parts.ToArray();
    }

    public override string ToString() => string.Join("+", Parts());

    /// <summary>Reads <see cref="ToString"/> text. Case-insensitive; also takes Control, Meta, Win and Key enum names.</summary>
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var tokens = text.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Any(string.IsNullOrEmpty))
            return false;

        var modifiers = KeyModifiers.None;
        foreach (var token in tokens[..^1])
        {
            var modifier = token.ToLowerInvariant() switch
            {
                "ctrl" or "control" => KeyModifiers.Control,
                "alt" => KeyModifiers.Alt,
                "shift" => KeyModifiers.Shift,
                "super" or "meta" or "win" or "cmd" => KeyModifiers.Meta,
                _ => (KeyModifiers?)null,
            };

            if (modifier is null)
                return false;

            modifiers |= modifier.Value;
        }

        if (!TryParseKey(tokens[^1], out var key) || IsModifierOrLock(key))
            return false;

        chord = new KeyChord(key, modifiers);
        return true;
    }

    private static bool TryParseKey(string name, out Key key)
    {
        if (KeysByName.TryGetValue(name, out key))
            return true;

        // Enum.TryParse takes "1" as the value 1 (Key.Cancel): only names are accepted
        return !name.All(char.IsDigit)
               && Enum.TryParse(name, ignoreCase: true, out key)
               && Enum.IsDefined(key);
    }

    private static string NameOf(Key key) => DisplayNames.TryGetValue(key, out var name) ? name : key.ToString();

    private static Key Normalize(Key key) => key is >= Key.NumPad0 and <= Key.NumPad9
        ? Key.D0 + (key - Key.NumPad0)
        : key;

    private static bool IsModifierOrLock(Key key) => key is Key.None
        or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin
        or Key.CapsLock or Key.NumLock or Key.Scroll;

    private static Dictionary<Key, string> BuildDisplayNames()
    {
        var names = new Dictionary<Key, string>
        {
            [Key.Enter] = "Enter",
            [Key.Escape] = "Esc",
            [Key.Space] = "Space",
            [Key.Back] = "Backspace",
            [Key.Tab] = "Tab",
            [Key.Delete] = "Delete",
            [Key.Insert] = "Insert",
            [Key.Home] = "Home",
            [Key.End] = "End",
            [Key.PageUp] = "PageUp",
            [Key.PageDown] = "PageDown",
            [Key.Left] = "Left",
            [Key.Right] = "Right",
            [Key.Up] = "Up",
            [Key.Down] = "Down",
            // No "+" in a name: it separates the parts
            [Key.OemPlus] = "=",
            [Key.OemMinus] = "-",
            [Key.OemComma] = ",",
            [Key.OemPeriod] = ".",
            [Key.OemQuestion] = "/",
            [Key.OemSemicolon] = ";",
            [Key.OemQuotes] = "'",
            [Key.OemOpenBrackets] = "[",
            [Key.OemCloseBrackets] = "]",
            [Key.OemPipe] = "\\",
            [Key.OemTilde] = "`",
            [Key.Add] = "NumPlus",
            [Key.Subtract] = "NumMinus",
            [Key.Multiply] = "NumMultiply",
            [Key.Divide] = "NumDivide",
            [Key.Decimal] = "NumDecimal",
        };

        for (var digit = 0; digit <= 9; digit++)
            names[Key.D0 + digit] = digit.ToString();

        return names;
    }

    private static Dictionary<string, Key> BuildKeysByName()
    {
        var keys = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, name) in DisplayNames)
            keys[name] = key;

        keys["Return"] = Key.Enter;
        keys["Escape"] = Key.Escape;
        keys["Del"] = Key.Delete;
        keys["Prior"] = Key.PageUp;
        keys["Next"] = Key.PageDown;
        return keys;
    }
}
