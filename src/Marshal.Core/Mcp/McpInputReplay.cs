using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions.InputRequests;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Marshal.Core.Mcp;

/// <summary>
/// An answer to one input request of an MCP server, kept to give it again when the frozen call is repeated.
/// </summary>
/// <param name="Signature">Identifies the request: mode, message and (form) schema; a URL may change between calls.</param>
/// <param name="Mode">Form or URL.</param>
/// <param name="Content">Form mode: the submitted object; null for URL mode.</param>
public sealed record McpInputAnswer(string Signature, InputRequestMode Mode, JsonElement? Content)
{
    /// <summary>The signature of a request the server sent.</summary>
    public static string SignatureOf(ElicitRequestParams request)
    {
        var node = new JsonObject
        {
            ["mode"] = ModeOf(request).ToString().ToLowerInvariant(),
            ["message"] = request.Message,
        };
        if (ModeOf(request) == InputRequestMode.Form && request.RequestedSchema is { } schema)
            node["schema"] = JsonSerializer.SerializeToNode(schema, McpJsonUtilities.DefaultOptions);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(McpToolFingerprint.Canonical(node))));
    }

    /// <summary>Form unless the server asks for URL mode.</summary>
    public static InputRequestMode ModeOf(ElicitRequestParams request) =>
        string.Equals(request.Mode, "url", StringComparison.Ordinal) ? InputRequestMode.Url : InputRequestMode.Form;

    /// <summary>The answers of a request's <see cref="InputRequestRecord.PreviousAnswersJson"/>.</summary>
    public static IReadOnlyList<McpInputAnswer> ParseList(string json) =>
        JsonSerializer.Deserialize<List<McpInputAnswer>>(json, JsonSerializerOptions.Web) ?? [];

    /// <summary>Serializes answers for <see cref="InputRequestRecord.PreviousAnswersJson"/>.</summary>
    public static string ToJson(IEnumerable<McpInputAnswer> answers) => JsonSerializer.Serialize(answers, JsonSerializerOptions.Web);
}

/// <summary>
/// Repeats a frozen MCP call with stored answers: while in scope, the server's input requests are answered from
/// <see cref="Answers"/> in order. A request they do not match is a new (chained) input request; it is recorded in
/// <see cref="Created"/>.
/// </summary>
public sealed class McpInputReplay(IReadOnlyList<McpInputAnswer> answers)
{
    private static readonly AsyncLocal<McpInputReplay?> _current = new();

    public IReadOnlyList<McpInputAnswer> Answers => answers;

    /// <summary>The chained request the repeated call created, if any.</summary>
    public InputRequestRecord? Created { get; internal set; }

    /// <summary>The replay in scope, if any.</summary>
    public static McpInputReplay? Current => _current.Value;

    /// <summary>Puts this replay in scope for the calls made until the returned scope is disposed.</summary>
    public IDisposable Use()
    {
        McpInputReplay? previous = _current.Value;
        _current.Value = this;
        return new Scope(previous);
    }

    private sealed class Scope(McpInputReplay? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}
