using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Marshal.Data.Persistence;

/// <summary>
/// Stores an enum as its snake_case name (<c>ConfirmedByKeyword</c> → <c>confirmed_by_keyword</c>).
/// </summary>
internal sealed class SnakeCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(value => ToText(value), text => FromText(text))
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> _toText = Enum.GetValues<TEnum>()
        .ToDictionary(value => value, value => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()));

    private static readonly Dictionary<string, TEnum> _fromText = _toText.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public static string ToText(TEnum value) => _toText[value];

    public static TEnum FromText(string text) => _fromText[text];
}
