using System.Text.Json;
using System.Text.Json.Serialization;

namespace File.Commander.Domain.Config;

/// <summary>
/// Stores <see cref="AppTheme"/> by name. Unlike JsonStringEnumConverter it never throws: an unknown
/// value falls back to Mocha instead of making the whole settings file unreadable, and the names of
/// the first version ("Dark", "Light") still load as Mocha and Latte.
/// </summary>
public sealed class AppThemeJsonConverter : JsonConverter<AppTheme>
{
    private const AppTheme Fallback = AppTheme.Mocha;

    public override AppTheme Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            // An object or array must be consumed whole; Skip is a no-op for a single value
            reader.Skip();
            return Fallback;
        }

        var name = reader.GetString();
        if (Enum.TryParse<AppTheme>(name, ignoreCase: true, out var theme) && Enum.IsDefined(theme))
            return theme;

        return string.Equals(name, "Light", StringComparison.OrdinalIgnoreCase) ? AppTheme.Latte : Fallback;
    }

    public override void Write(Utf8JsonWriter writer, AppTheme value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
