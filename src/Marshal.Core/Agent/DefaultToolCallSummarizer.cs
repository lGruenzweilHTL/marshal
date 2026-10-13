using System.Text;
using System.Text.Json;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Agent;

/// <summary>
/// Tool-call summaries: arguments as <c>key=value</c> (strings cut to 60 characters, 120 in total), the result as its
/// first line (120 characters) or the error code. Redacted tools (the recorder's rule) get no result summary.
/// </summary>
public sealed class DefaultToolCallSummarizer(IToolRedaction redaction) : IToolCallSummarizer
{
    private const int MaxValue = 60;
    private const int MaxLength = 120;
    private const string Ellipsis = "…";

    public string Arguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return Cut(OneLine(arguments.GetRawText()), MaxLength);

        var text = new StringBuilder();
        foreach (JsonProperty property in arguments.EnumerateObject())
        {
            if (text.Length > 0)
                text.Append(", ");
            string value = property.Value.ValueKind == JsonValueKind.String ? Cut(OneLine(property.Value.GetString()!), MaxValue) : OneLine(property.Value.GetRawText());
            text.Append(property.Name).Append('=').Append(value);
        }

        return Cut(text.ToString(), MaxLength);
    }

    public string? Result(ToolId? tool, bool sensitive, string? content, string? errorCode)
    {
        if (errorCode is not null)
            return errorCode;
        if (sensitive || (tool is { } id && redaction.Redacts(id, sensitive)))
            return null;

        string? line = content?.Split('\n').Select(candidate => candidate.Trim()).FirstOrDefault(candidate => candidate.Length > 0);
        return line is null ? null : Cut(line, MaxLength);
    }

    private static string OneLine(string text) => string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + Ellipsis;
}
