namespace Marshal.Abstractions.Context;

/// <summary>
/// Adds one section to the system prompt (base prompt, profile, time, satellite, output mode; later memory,
/// skills, templates). Must not hardcode user names or personal data.
/// </summary>
public interface IContextContributor
{
    /// <summary>Position in the prompt; lower comes first.</summary>
    int Order { get; }

    /// <summary>The section text, or null to add nothing.</summary>
    Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken);
}
