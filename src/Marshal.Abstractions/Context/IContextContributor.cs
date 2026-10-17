namespace Marshal.Abstractions.Context;

/// <summary>
/// Adds one section to the system prompt (base prompt, profile, time, satellite, output mode; later memory,
/// skills, templates). Must not hardcode user names or personal data.
/// </summary>
public interface IContextContributor
{
    /// <summary>
    /// How often the section changes. The prompt holds all static sections first, then session, then turn sections,
    /// so the text before the first turn section stays identical across a session's turns (provider prompt caches).
    /// </summary>
    ContextStability Stability { get; }

    /// <summary>Position within the sections of the same stability; lower comes first.</summary>
    int Order { get; }

    /// <summary>The section text, or null to add nothing.</summary>
    Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken);
}
