using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Marshal.Integrations.Mail.Tests.Imap;

/// <summary>
/// A minimal IMAP responder advertising <c>AUTH=XOAUTH2</c> with SASL-IR: records every command and answers OK, so a
/// test can check what the client sent to authenticate.
/// </summary>
internal sealed class FakeXOAuth2Server : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Task _loop;
    private readonly CancellationTokenSource _stop = new();

    private FakeXOAuth2Server()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _loop = Task.Run(ServeAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Commands without their tags, in order.</summary>
    public List<string> Commands { get; } = [];

    public static FakeXOAuth2Server Start() => new();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or IOException or ObjectDisposedException)
        {
        }

        _stop.Dispose();
    }

    private async Task ServeAsync()
    {
        using TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
        await using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("* OK [CAPABILITY IMAP4rev1 SASL-IR AUTH=XOAUTH2] ready");
        while (await reader.ReadLineAsync(_stop.Token) is { } line)
        {
            int space = line.IndexOf(' ', StringComparison.Ordinal);
            string tag = line[..space];
            string command = line[(space + 1)..];
            lock (Commands)
                Commands.Add(command);

            if (command.StartsWith("CAPABILITY", StringComparison.Ordinal))
                await writer.WriteLineAsync("* CAPABILITY IMAP4rev1 SASL-IR AUTH=XOAUTH2");
            if (command.StartsWith("LOGOUT", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("* BYE");
                await writer.WriteLineAsync($"{tag} OK LOGOUT completed");
                return;
            }

            await writer.WriteLineAsync(command.StartsWith("AUTHENTICATE", StringComparison.Ordinal)
                ? $"{tag} OK [CAPABILITY IMAP4rev1] Logged in"
                : $"{tag} OK done");
        }
    }
}
