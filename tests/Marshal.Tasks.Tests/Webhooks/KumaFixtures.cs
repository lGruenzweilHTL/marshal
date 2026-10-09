using System.Text;
using Marshal.Abstractions.Events;

namespace Marshal.Tasks.Tests.Webhooks;

/// <summary>Uptime Kuma webhook bodies, shaped after Uptime Kuma's webhook provider (<c>Webhooks/Fixtures</c>).</summary>
public static class KumaFixtures
{
    public static readonly string[] Names = ["down", "up", "pending", "maintenance", "certificate", "test", "unknown", "invalid"];

    public static string Text(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Webhooks", "Fixtures", $"kuma-{name}.json"));

    /// <summary>The body as Uptime Kuma posts it with content type JSON.</summary>
    public static WebhookRequest Json(string name) => new("application/json", Encoding.UTF8.GetBytes(Text(name)), new Dictionary<string, string>());

    /// <summary>The body as Uptime Kuma posts it with content type form-data: the JSON in a <c>data</c> field.</summary>
    public static WebhookRequest Form(string name) => new("multipart/form-data; boundary=x", ReadOnlyMemory<byte>.Empty, new Dictionary<string, string> { ["data"] = Text(name) });

    /// <summary>The recorded multipart body of a down heartbeat sent as form-data, and its content type.</summary>
    public static (string ContentType, string Body) Multipart()
    {
        string body = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Webhooks", "Fixtures", "kuma-down.form.txt"));
        string boundary = body[2..body.IndexOf('\r', StringComparison.Ordinal)];
        return ($"multipart/form-data; boundary={boundary}", body);
    }
}
