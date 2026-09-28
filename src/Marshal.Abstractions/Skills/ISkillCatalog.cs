using Marshal.Abstractions.Configuration;

namespace Marshal.Abstractions.Skills;

/// <summary>
/// The skills under <c>/config/skills/</c>, hot-reloaded. Invalid skills are rejected one by one.
/// </summary>
public interface ISkillCatalog
{
    /// <summary>Valid skills, ordered by name.</summary>
    IReadOnlyList<Skill> Skills { get; }

    /// <summary>Skills rejected by the last load.</summary>
    IReadOnlyList<ConfigFileError> Errors { get; }

    /// <summary>A skill by name; null when unknown.</summary>
    Skill? Find(string name);
}
