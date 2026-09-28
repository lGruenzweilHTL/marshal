using System.ComponentModel.DataAnnotations;

namespace Marshal.Core.Agent;

/// <summary>
/// Agent loop limits (<c>agent</c> in <c>marshal.yaml</c>).
/// </summary>
public sealed class AgentOptions : IValidatableObject
{
    /// <summary>Maximum tool rounds per run.</summary>
    [Range(1, 50, ErrorMessage = "agent.maxSteps must be between {1} and {2}.")]
    public int MaxSteps { get; set; } = 8;

    /// <summary>Hard latency cap for voice turns.</summary>
    public TimeSpan VoiceLatencyCap { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Hard latency cap for text turns.</summary>
    public TimeSpan TextLatencyCap { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Deepest subagent level; <c>agent.delegate</c> is hidden at this depth.</summary>
    [Range(0, 10, ErrorMessage = "agent.maxDelegationDepth must be between {1} and {2}.")]
    public int MaxDelegationDepth { get; set; } = 3;

    public AdvisorOptions Advisor { get; set; } = new();

    /// <summary>Validates the nested advisor limits too.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(Advisor, new ValidationContext(Advisor), results, validateAllProperties: true);
        return results;
    }
}
