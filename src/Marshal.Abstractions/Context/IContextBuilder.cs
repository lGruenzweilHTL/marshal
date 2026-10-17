namespace Marshal.Abstractions.Context;

/// <summary>
/// Assembles the system prompt from all contributors, ordered by stability and then by order.
/// </summary>
public interface IContextBuilder
{
    /// <summary>The system prompt as a stable part and a turn part.</summary>
    Task<SystemPrompt> BuildAsync(ContextRequest request, CancellationToken cancellationToken);

    /// <summary>The whole system prompt as one text.</summary>
    async Task<string> BuildSystemPromptAsync(ContextRequest request, CancellationToken cancellationToken) =>
        (await BuildAsync(request, cancellationToken)).Text;
}
