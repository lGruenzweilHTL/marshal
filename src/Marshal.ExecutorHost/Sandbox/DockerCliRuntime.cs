using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace Marshal.ExecutorHost.Sandbox;

/// <summary>
/// <see cref="ISandboxRuntime"/> through the <c>docker</c> CLI of this host (rootless Docker or Sysbox): a throwaway
/// container on the allowlist network with only the worktree and the repository's git directory mounted, running
/// <c>claude --bare -p</c> with stream-json output. Arguments are passed as a list, never through a shell.
/// </summary>
public sealed class DockerCliRuntime(IOptions<ExecutorHostOptions> options) : ISandboxRuntime
{
    public async Task<int> RunAsync(SandboxRun run, Func<string, CancellationToken, Task> onLine, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in Arguments(run, options.Value))
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("docker could not be started.");
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
                await onLine(line, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            await RemoveAsync(run.Name);
            throw;
        }
    }

    /// <summary>The <c>docker</c> arguments for <paramref name="run"/>.</summary>
    public static IReadOnlyList<string> Arguments(SandboxRun run, ExecutorHostOptions options) =>
    [
        "run", "--rm", "--name", run.Name, "--network", options.Network, "--env-file", options.EnvFile,
        "-v", $"{run.WorkDirectory}:{run.WorkDirectory}", "-v", $"{run.GitDirectory}:{run.GitDirectory}", "-w", run.WorkDirectory, options.Image,
        "claude", "--bare", "-p", run.Prompt, "--output-format", "stream-json", "--verbose",
        "--permission-mode", "bypassPermissions",
        "--max-turns", options.MaxTurns.ToString(CultureInfo.InvariantCulture),
        "--max-budget-usd", options.MaxBudgetUsd.ToString(CultureInfo.InvariantCulture),
    ];

    private static async Task RemoveAsync(string name)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("rm");
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add(name);
        using var process = Process.Start(start);
        if (process is not null)
            await process.WaitForExitAsync(CancellationToken.None);
    }
}
