using System.Diagnostics;
using System.Text;

namespace Marshal.Integrations.Notes;

/// <summary>
/// Runs the git CLI without prompts. Credentials go through <c>GIT_ASKPASS</c> (https tokens) or
/// <c>GIT_SSH_COMMAND</c> with a temporary key file (deploy keys), never through arguments or the remote URL.
/// </summary>
internal sealed class GitCli(string? credential, TimeSpan timeout)
{
    public async Task<(bool Success, string Output, string Error)> RunAsync(string? workingDirectory, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        using var secrets = new CredentialFiles(credential);
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["LC_ALL"] = "C";
        secrets.Apply(start);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("git could not be started.");
        Task<string> error = process.StandardError.ReadToEndAsync(limit.Token);
        Task<string> output = process.StandardOutput.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
                throw;
            return (false, "", $"git did not finish within {timeout.TotalSeconds:0} seconds");
        }

        string message = (await error).Trim();
        return (process.ExitCode == 0, (await output).Trim(), message.Length > 0 ? message : $"git exited with {process.ExitCode}");
    }

    // Temporary helper files for one git run: an askpass script reading the token from the environment, or a key file.
    private sealed class CredentialFiles : IDisposable
    {
        private readonly string? _credential;
        private readonly string? _directory;

        public CredentialFiles(string? credential)
        {
            _credential = credential;
            if (string.IsNullOrEmpty(credential))
                return;

            _directory = Directory.CreateTempSubdirectory("marshal-git-").FullName;
            if (IsPrivateKey)
            {
                File.WriteAllText(KeyFile, credential.ReplaceLineEndings("\n").TrimEnd() + "\n");
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(KeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            else
            {
                File.WriteAllText(AskPass, "#!/bin/sh\ncase \"$1\" in\n  Username*) echo \"x-access-token\" ;;\n  *) echo \"$MARSHAL_GIT_TOKEN\" ;;\nesac\n");
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(AskPass, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        private bool IsPrivateKey => _credential!.Contains("PRIVATE KEY-----", StringComparison.Ordinal);

        private string KeyFile => Path.Combine(_directory!, "key");

        private string AskPass => Path.Combine(_directory!, "askpass.sh");

        public void Apply(ProcessStartInfo start)
        {
            if (_directory is null)
                return;

            if (IsPrivateKey)
            {
                start.Environment["GIT_SSH_COMMAND"] = $"ssh -i \"{KeyFile}\" -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new -o BatchMode=yes";
            }
            else
            {
                start.Environment["GIT_ASKPASS"] = AskPass;
                start.Environment["MARSHAL_GIT_TOKEN"] = _credential;
            }
        }

        public void Dispose()
        {
            if (_directory is not null && Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
