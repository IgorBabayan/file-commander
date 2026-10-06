using System.Globalization;

namespace File.Commander.Presentation.ViewModels.Helpers;

public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value.ToString("0.#", CultureInfo.CurrentCulture)} {Units[unit]}";
    }
}
