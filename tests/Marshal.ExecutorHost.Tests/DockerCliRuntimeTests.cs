using Marshal.ExecutorHost.Sandbox;

namespace Marshal.ExecutorHost.Tests;

public sealed class DockerCliRuntimeTests
{
    [Fact]
    public void Arguments_SandboxedHeadlessClaudeInTheWorktree()
    {
        var options = new ExecutorHostOptions { Image = "marshal-claude", Network = "sandbox", EnvFile = "/run/secrets/sandbox.env", MaxTurns = 40, MaxBudgetUsd = 3m };
        var run = new SandboxRun("marshal-exec-1", "/worktrees/1", "/repos/demo/.git", "Fix the \"bug\"; rm -rf /");

        IReadOnlyList<string> arguments = DockerCliRuntime.Arguments(run, options);

        Assert.Equal(["run", "--rm", "--name", "marshal-exec-1", "--network", "sandbox", "--env-file", "/run/secrets/sandbox.env",
            "-v", "/worktrees/1:/worktrees/1", "-v", "/repos/demo/.git:/repos/demo/.git", "-w", "/worktrees/1", "marshal-claude",
            "claude", "--bare", "-p", "Fix the \"bug\"; rm -rf /", "--output-format", "stream-json", "--verbose",
            "--permission-mode", "bypassPermissions", "--max-turns", "40", "--max-budget-usd", "3"], arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains("docker.sock", StringComparison.Ordinal));
    }
}
