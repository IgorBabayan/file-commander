using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using File.Commander.Application.Keyboard;

namespace File.Commander.Presentation.ViewModels.Settings;

/// <summary>
/// Settings → Basic → Keymap: every shortcut, grouped by kind. Right-click a row to change,
/// add, remove or reset it. Each change is reported through the callback and saved at once.
/// </summary>
public sealed partial class KeymapSettingsViewModel : ObservableObject
{
    private readonly Dictionary<string, List<KeyChord>> _bindings;
    private readonly IReadOnlyDictionary<string, List<string>>? _stored;
    private readonly Action _changed;

    private KeymapItemViewModel? _recording;
    private bool _recordingAdds;

    /// <param name="stored">The overrides from settings.json, see <see cref="AppSettings.Keymap"/>.</param>
    /// <param name="changed">Called after every change; the owner saves <see cref="ToOverrides"/>.</param>
    public KeymapSettingsViewModel(IReadOnlyDictionary<string, List<string>>? stored, Action changed)
    {
        _stored = stored;
        _changed = changed;

        var keymap = Keymap.From(stored);
        _bindings = KeymapActions.All.ToDictionary(a => a.Id, a => keymap.For(a.Id).ToList());

        Groups = KeymapActions.Groups.Select(title => new KeymapGroupViewModel(title)).ToList();
        Rebuild();
    }

    public IReadOnlyList<KeymapGroupViewModel> Groups { get; }

    /// <summary>The row last clicked; highlighted so it's clear which row the context menu acts on.</summary>
    [ObservableProperty]
    public partial KeymapItemViewModel? Selected { get; set; }

    /// <summary>Tells that a new shortcut was taken away from another action.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; set; }

    public bool HasNotice => Notice is not null;

    /// <summary>A row waits for a key press. The window then sends every key here instead of handling it.</summary>
    public bool IsRecording => _recording is not null;

    partial void OnSelectedChanged(KeymapItemViewModel? oldValue, KeymapItemViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;

        if (newValue is not null)
            newValue.IsSelected = true;
    }

    /// <summary>Esc, a click anywhere, or another row starting to record.</summary>
    public void CancelRecording()
    {
        if (_recording is null)
            return;

        _recording.IsRecording = false;
        _recording = null;
        OnPropertyChanged(nameof(IsRecording));
    }

    /// <summary>
    /// The key press of a recording row. A modifier alone (Ctrl, Shift…) keeps waiting for the key.
    /// </summary>
    /// <returns>True when the shortcut was assigned.</returns>
    public bool Capture(Key key, KeyModifiers modifiers)
    {
        if (_recording is not { } item || KeyChord.FromKeyPress(key, modifiers) is not { } chord)
            return false;

        var replaces = _recordingAdds ? null : item.Chord;
        CancelRecording();

        var chords = _bindings[item.Action.Id];
        var index = replaces is { } old ? chords.IndexOf(old) : -1;
        if (index >= 0)
            chords[index] = chord;
        else
            chords.Add(chord);

        _bindings[item.Action.Id] = chords.Distinct().ToList();
        Notice = TakeFromOthers(item.Action, [chord]);
        Commit(item.Action, chord);
        return true;
    }

    /// <summary>One chord per row: the row's own is removed, the action's other chords stay.</summary>
    internal void Remove(KeymapItemViewModel item)
    {
        if (item.Chord is not { } chord)
            return;

        CancelRecording();
        _bindings[item.Action.Id].Remove(chord);
        Notice = null;
        Commit(item.Action, null);
    }

    internal void Reset(KeymapItemViewModel item)
    {
        CancelRecording();
        _bindings[item.Action.Id] = item.Action.Defaults.ToList();
        Notice = TakeFromOthers(item.Action, item.Action.Defaults);
        Commit(item.Action, item.Action.Defaults.Count > 0 ? item.Action.Defaults[0] : null);
    }

    internal void BeginRecording(KeymapItemViewModel item, bool add)
    {
        CancelRecording();
        Notice = null;

        _recording = item;
        _recordingAdds = add || item.Chord is null;
        item.IsRecording = true;
        Selected = item;
        OnPropertyChanged(nameof(IsRecording));
    }

    internal bool IsDefault(KeymapAction action) => _bindings[action.Id].SequenceEqual(action.Defaults);

    /// <summary>
    /// What settings.json stores: only actions whose shortcuts differ from the defaults.
    /// Ids this version doesn't know (a newer version wrote them) are kept as they are.
    /// </summary>
    public Dictionary<string, List<string>> ToOverrides()
    {
        var overrides = new Dictionary<string, List<string>>();

        if (_stored is not null)
        {
            foreach (var (id, chords) in _stored)
            {
                if (KeymapActions.Find(id) is null && chords is not null)
                    overrides[id] = chords.ToList();
            }
        }

        foreach (var action in KeymapActions.All)
        {
            if (!IsDefault(action))
                overrides[action.Id] = _bindings[action.Id].Select(c => c.ToString()).ToList();
        }

        return overrides;
    }

    /// <summary>A chord does one thing: it's removed from every other action that had it.</summary>
    /// <returns>The notice to show, or null when nothing was taken.</returns>
    private string? TakeFromOthers(KeymapAction owner, IEnumerable<KeyChord> chords)
    {
        var taken = new List<string>();
        foreach (var chord in chords)
        {
            foreach (var other in KeymapActions.All)
            {
                if (other.Id != owner.Id && _bindings[other.Id].Remove(chord))
                    taken.Add($"{chord} was removed from “{other.Title}”.");
            }
        }

        return taken.Count > 0 ? string.Join(" ", taken) : null;
    }

    private void Commit(KeymapAction action, KeyChord? select)
    {
        Rebuild();

        var rows = Groups.SelectMany(g => g.Items).Where(i => i.Action == action).ToList();
        Selected = rows.FirstOrDefault(i => i.Chord == select) ?? rows.FirstOrDefault();

        _changed();
    }

    /// <summary>One row per chord; an action without any gets one "Unassigned" row so it can be bound again.</summary>
    private void Rebuild()
    {
        foreach (var group in Groups)
        {
            group.Items.Clear();
            foreach (var action in KeymapActions.All.Where(a => a.Group == group.Title))
            {
                var chords = _bindings[action.Id];
                if (chords.Count == 0)
                {
                    group.Items.Add(new KeymapItemViewModel(this, action, null));
                    continue;
                }

                foreach (var chord in chords)
                    group.Items.Add(new KeymapItemViewModel(this, action, chord));
            }
        }
    }
}

/// <summary>A collapsible group of the keymap (Actions, Navigation…).</summary>
public sealed partial class KeymapGroupViewModel : ObservableObject
{
    public KeymapGroupViewModel(string title)
    {
        Title = title;
        IsExpanded = true;
    }

    public string Title { get; }

    public ObservableCollection<KeymapItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}

/// <summary>One shortcut of an action, or the "Unassigned" row of an action without any.</summary>
public sealed partial class KeymapItemViewModel : ObservableObject
{
    private readonly KeymapSettingsViewModel _owner;

    internal KeymapItemViewModel(KeymapSettingsViewModel owner, KeymapAction action, KeyChord? chord)
    {
        _owner = owner;
        Action = action;
        Chord = chord;
        Keys = chord?.Parts() ?? [];
    }

    public KeymapAction Action { get; }

    public KeyChord? Chord { get; }

    public string Title => Action.Title;

    /// <summary>One key cap each: ["Ctrl", "Shift", "V"].</summary>
    public IReadOnlyList<string> Keys { get; }

    public bool HasKeys => Chord is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeys), nameof(ShowUnassigned))]
    public partial bool IsRecording { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public bool ShowKeys => HasKeys && !IsRecording;

    public bool ShowUnassigned => !HasKeys && !IsRecording;

    /// <summary>Waits for a key press that replaces this row's shortcut (or assigns one).</summary>
    [RelayCommand]
    private void Edit() => _owner.BeginRecording(this, add: false);

    /// <summary>Waits for a key press that becomes one more shortcut of the action.</summary>
    [RelayCommand(CanExecute = nameof(HasKeys))]
    private void Add() => _owner.BeginRecording(this, add: true);

    [RelayCommand(CanExecute = nameof(HasKeys))]
    private void Remove() => _owner.Remove(this);

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset() => _owner.Reset(this);

    private bool CanReset() => !_owner.IsDefault(Action);
}
