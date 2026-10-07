using System.Globalization;

namespace File.Commander.Presentation.ViewModels.Browser;

/// <summary>
/// Orders file names as GNOME Files (Nautilus) does, with GLib's g_utf8_collate_key_for_filename: numbers by value
/// ("file2" before "file10"), a dot before anything else ("MainWindow.axaml" before "MainWindow (3).axaml"), numbers
/// before text, the rest by the current culture. Names starting with '.' or '#' go after all others.
/// Built once per entry, on the thread that reads the folder, so sorting doesn't split names again.
/// </summary>
public sealed class FileNameKey : IComparable<FileNameKey>
{
    private static readonly CompareInfo Collation = CultureInfo.CurrentCulture.CompareInfo;

    private readonly bool _sortsLast;
    private readonly Segment[] _segments;

    // GLib appends the count of leading zeros of every number at the very end of the key:
    // "file1" and "file01" only differ there, and the one with fewer zeros comes first
    private readonly int[] _leadingZeros;

    public FileNameKey(string name)
    {
        _sortsLast = name.Length > 0 && name[0] is '.' or '#';

        var segments = new List<Segment>();
        var zeros = new List<int>();
        var start = 0;
        var i = 0;

        while (i < name.Length)
        {
            var c = name[i];
            if (c == '.')
            {
                AddText(i);
                segments.Add(new Segment(SegmentKind.Dot, string.Empty));
                start = ++i;
            }
            else if (char.IsAsciiDigit(c))
            {
                AddText(i);

                var end = i;
                while (end < name.Length && char.IsAsciiDigit(name[end]))
                    end++;

                // "007" is 7 with two leading zeros; "000" is 0 with two
                var first = i;
                while (first < end - 1 && name[first] == '0')
                    first++;

                if (first > i)
                    zeros.Add(first - i);

                segments.Add(new Segment(SegmentKind.Number, name[first..end]));
                start = i = end;
            }
            else
            {
                i++;
            }
        }

        AddText(name.Length);
        _segments = segments.ToArray();
        _leadingZeros = zeros.ToArray();
        return;

        void AddText(int end)
        {
            if (end > start)
                segments.Add(new Segment(SegmentKind.Text, name[start..end]));
        }
    }

    public int CompareTo(FileNameKey? other)
    {
        if (other is null)
            return 1;

        if (_sortsLast != other._sortsLast)
            return _sortsLast ? 1 : -1;

        var count = Math.Min(_segments.Length, other._segments.Length);
        for (var i = 0; i < count; i++)
        {
            var result = Compare(_segments[i], other._segments[i]);
            if (result != 0)
                return result;
        }

        // A name that is the start of the other comes first
        var length = _segments.Length.CompareTo(other._segments.Length);
        if (length != 0)
            return length;

        return CompareZeros(_leadingZeros, other._leadingZeros);
    }

    private static int Compare(Segment a, Segment b)
    {
        // Dot, then number, then text: GLib's sentinels sort before any collated text
        if (a.Kind != b.Kind)
            return a.Kind.CompareTo(b.Kind);

        return a.Kind switch
        {
            SegmentKind.Dot => 0,
            // More digits is a larger number; same count: digit by digit
            SegmentKind.Number => a.Text.Length != b.Text.Length
                ? a.Text.Length.CompareTo(b.Text.Length)
                : string.CompareOrdinal(a.Text, b.Text),
            _ => Collation.Compare(a.Text, b.Text, CompareOptions.None),
        };
    }

    private static int CompareZeros(int[] a, int[] b)
    {
        var count = Math.Min(a.Length, b.Length);
        for (var i = 0; i < count; i++)
        {
            if (a[i] != b[i])
                return a[i].CompareTo(b[i]);
        }

        return a.Length.CompareTo(b.Length);
    }

    private enum SegmentKind
    {
        Dot,
        Number,
        Text
    }

    private readonly record struct Segment(SegmentKind Kind, string Text);
}
