using System.Text.RegularExpressions;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Finds names (entities, aliases, areas) as whole words in a user's text, ignoring case.
/// </summary>
public static class HaNames
{
    private const int MinNameLength = 3;

    /// <summary>True when any of <paramref name="names"/> occurs as a whole word in <paramref name="text"/>.</summary>
    public static bool AnyMentioned(IEnumerable<string> names, string text) =>
        names.Any(name => name.Length >= MinNameLength
            && Regex.IsMatch(text, $@"(?<!\w){Regex.Escape(name)}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));

    /// <summary>Name, aliases and friendly name of an entity.</summary>
    public static IEnumerable<string> Of(HaEntity entity, HaSnapshot snapshot)
    {
        IEnumerable<string> names = entity.Aliases.Prepend(entity.Name);
        return snapshot.States.GetValueOrDefault(entity.EntityId)?.FriendlyName is { } friendly ? names.Append(friendly) : names;
    }
}
