using System.ComponentModel.DataAnnotations;

namespace Marshal.Core.Tools;

/// <summary>
/// Which tools reach the model (<c>tools</c> in <c>marshal.yaml</c>).
/// </summary>
public sealed class ToolsOptions : IValidatableObject
{
    /// <summary>Namespaces loaded in every session when <c>tools.core</c> is not set.</summary>
    public static IReadOnlyList<string> DefaultCore { get; } = ["agent", "memory", "tasks", "notifications", "timers", "places"];

    /// <summary>
    /// Configured core namespaces. Null keeps the defaults; the configuration binder appends to a list that already
    /// has items, so the defaults are not the initial value.
    /// </summary>
    public List<string>? Core { get; set; }

    public ToolRetrievalOptions Retrieval { get; set; } = new();

    /// <summary>Namespaces that are always loaded, in list order.</summary>
    public IReadOnlyList<string> CoreNamespaces => Core is { Count: > 0 } core ? core : DefaultCore;

    /// <summary>Validates the nested retrieval values too.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(Retrieval, new ValidationContext(Retrieval), results, validateAllProperties: true);
        return results;
    }
}
