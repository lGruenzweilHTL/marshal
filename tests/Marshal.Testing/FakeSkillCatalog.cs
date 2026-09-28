using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Skills;

namespace Marshal.Testing;

/// <summary>
/// Skill catalog over a mutable list.
/// </summary>
public sealed class FakeSkillCatalog : ISkillCatalog
{
    /// <summary>Skills offered; may be changed at any time.</summary>
    public List<Skill> Items { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<Skill> Skills => [.. Items];

    /// <inheritdoc />
    public IReadOnlyList<ConfigFileError> Errors { get; set; } = [];

    /// <summary>A skill with optional extra files.</summary>
    public static Skill Skill(string name, string description = "A skill.", string body = "Do it well.", Dictionary<string, string>? files = null) =>
        new(name, description, body, files ?? []);

    /// <inheritdoc />
    public Skill? Find(string name) => Items.FirstOrDefault(skill => skill.Name == name);
}
