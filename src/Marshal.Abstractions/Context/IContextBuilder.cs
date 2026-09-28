namespace Marshal.Abstractions.Context;

/// <summary>
/// Assembles the system prompt from all contributors in order.
/// </summary>
public interface IContextBuilder
{
    Task<string> BuildSystemPromptAsync(ContextRequest request, CancellationToken cancellationToken);
}
