using System.Text.Json;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Runs;

/// <summary>
/// Short one-line summaries of a tool call for the chat's tool timeline, live and in reopened conversations. Plain
/// text; never rendered as markup.
/// </summary>
public interface IToolCallSummarizer
{
    /// <summary>The arguments as <c>key=value</c> pairs.</summary>
    string Arguments(JsonElement arguments);

    /// <summary>
    /// The result's first line, or the error code; null for tools whose results are redacted or when there is nothing
    /// to show.
    /// </summary>
    string? Result(ToolId? tool, bool sensitive, string? content, string? errorCode);
}
