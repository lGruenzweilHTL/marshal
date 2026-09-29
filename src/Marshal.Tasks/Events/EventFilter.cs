using System.Globalization;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Events;

/// <summary>
/// A watch's declarative event filter: <c>source</c> (required), optional <c>type</c> and <c>entity</c> globs, and
/// payload conditions for every other key (a dotted path into the payload, e.g. <c>to</c> or
/// <c>attributes.battery</c>). A condition value is a string glob, a number, a boolean, null (field missing) or a
/// list of those (any of them). Strings compare case-insensitively; <c>*</c> matches any text. Numbers also match
/// numeric strings, because HA states are strings.
/// </summary>
internal sealed class EventFilter
{
    private static readonly HashSet<string> _reserved = ["source", "type", "entity"];

    private readonly string _source;
    private readonly string? _type;
    private readonly string? _entity;
    private readonly IReadOnlyList<(string[] Path, JsonElement[] Values)> _conditions;

    private EventFilter(string source, string? type, string? entity, IReadOnlyList<(string[] Path, JsonElement[] Values)> conditions)
    {
        _source = source;
        _type = type;
        _entity = entity;
        _conditions = conditions;
    }

    public static Result<EventFilter> Parse(string json)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Invalid();
        }

        if (root.ValueKind != JsonValueKind.Object || Text(root, "source") is not { } source)
            return Invalid();

        var conditions = new List<(string[], JsonElement[])>();
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (_reserved.Contains(property.Name))
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    return Invalid();
                continue;
            }

            JsonElement[] values = property.Value.ValueKind == JsonValueKind.Array ? [.. property.Value.EnumerateArray()] : [property.Value];
            if (values.Length == 0 || values.Any(value => value.ValueKind is JsonValueKind.Object or JsonValueKind.Array))
                return Invalid();
            conditions.Add((property.Name.Split('.'), values));
        }

        return new EventFilter(source, Text(root, "type"), Text(root, "entity"), conditions);
    }

    public bool Matches(MarshalEvent @event)
    {
        if (!string.Equals(_source, @event.Source, StringComparison.OrdinalIgnoreCase))
            return false;
        if (_type is not null && !Glob(_type, @event.Type))
            return false;
        if (_entity is not null && (@event.Entity is null || !Glob(_entity, @event.Entity)))
            return false;

        foreach ((string[] path, JsonElement[] values) in _conditions)
        {
            JsonElement? actual = Find(@event.Payload, path);
            if (!values.Any(expected => Satisfies(expected, actual)))
                return false;
        }

        return true;
    }

    private static Error Invalid() =>
        TaskErrors.Invalid("An event filter is a JSON object with a source, optional type and entity, and payload conditions (text, number, true/false or a list of them).");

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static JsonElement? Find(JsonElement payload, string[] path)
    {
        JsonElement current = payload;
        foreach (string segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return null;
        }

        return current;
    }

    private static bool Satisfies(JsonElement expected, JsonElement? actual)
    {
        if (expected.ValueKind == JsonValueKind.Null)
            return actual is null || actual.Value.ValueKind == JsonValueKind.Null;
        if (actual is not { } value)
            return false;

        return expected.ValueKind switch
        {
            JsonValueKind.String => AsText(value) is { } text && Glob(expected.GetString()!, text),
            JsonValueKind.Number => AsNumber(value) is { } number && number == expected.GetDecimal(),
            JsonValueKind.True or JsonValueKind.False => AsBoolean(value) == (expected.ValueKind == JsonValueKind.True),
            _ => false,
        };
    }

    private static string? AsText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => null,
    };

    private static decimal? AsNumber(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDecimal(out decimal number) => number,
        JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed) => parsed,
        _ => null,
    };

    private static bool? AsBoolean(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
        _ => null,
    };

    // '*' matches any run of characters; everything else literally, ignoring case.
    private static bool Glob(string pattern, string text)
    {
        int p = 0, t = 0, star = -1, resume = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = t;
            }
            else if (p < pattern.Length && char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t]))
            {
                p++;
                t++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;
        return p == pattern.Length;
    }
}
