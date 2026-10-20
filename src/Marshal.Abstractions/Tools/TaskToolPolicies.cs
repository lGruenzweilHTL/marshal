namespace Marshal.Abstractions.Tools;

/// <summary>Checks tools against a task's tool policy (a list of tool id globs).</summary>
public static class TaskToolPolicies
{
    /// <summary>True when <paramref name="policy"/> is null (no policy) or one of its globs matches <paramref name="tool"/>.</summary>
    public static bool Allows(IReadOnlyList<string>? policy, ToolId tool) => policy is null || policy.Any(glob => ToolGlob.Matches(glob, tool));
}
