using System.Text.Json;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Guard;

/// <summary>
/// Expands the arguments of a call to the concrete targets it acts on, for one tool namespace.
/// Must expand every filter the call carries (name, area, floor, domain, device class).
/// </summary>
public interface IRiskTargetResolver
{
    /// <summary>The tool namespace this resolver handles (e.g. <c>ha</c>).</summary>
    string Namespace { get; }

    /// <summary>Resolves the targets; a failure or an empty list makes the call confirm tier.</summary>
    Task<Result<IReadOnlyList<RiskTarget>>> ResolveAsync(ToolId tool, JsonElement arguments, CancellationToken cancellationToken);
}
