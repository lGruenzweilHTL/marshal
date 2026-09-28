namespace Marshal.Core.Agent;

/// <summary>
/// Runs the agent loop for an execution context.
/// </summary>
public interface IAgentRunner
{
    Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken);
}
