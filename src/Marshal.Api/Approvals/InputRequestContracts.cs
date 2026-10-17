using System.Text.Json;
using Marshal.Abstractions.InputRequests;

namespace Marshal.Api.Approvals;

/// <summary>
/// An MCP server's request for the user's input, with the interrupted call frozen. Message, schema and URL are the
/// server's text: show them as plain text, and open the URL only when the user chooses to.
/// </summary>
/// <param name="Id">Input request id.</param>
/// <param name="ServerId">MCP server that asks.</param>
/// <param name="Tool">The interrupted tool; answering repeats exactly this call once.</param>
/// <param name="Mode"><c>form</c> (answer the requested fields) or <c>url</c> (open the page, then say done).</param>
/// <param name="Message">What the server asks.</param>
/// <param name="RequestedSchema">Form mode: object schema with string, number, integer, boolean and enum properties.</param>
/// <param name="Url">URL mode: the page to open.</param>
/// <param name="SessionId">Conversation the request came from, if any.</param>
/// <param name="TaskId">The task that resumes with the outcome.</param>
/// <param name="Status"><c>pending</c>, <c>answered</c>, <c>declined</c>, <c>expired</c> or <c>dropped</c>.</param>
/// <param name="CreatedAt">When the server asked.</param>
/// <param name="ExpiresAt">When the request expires (nothing is sent to the server then).</param>
public sealed record InputRequestItem(
    string Id,
    string ServerId,
    string Tool,
    InputRequestMode Mode,
    string Message,
    JsonElement? RequestedSchema,
    string? Url,
    string? SessionId,
    string? TaskId,
    InputRequestStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    internal static InputRequestItem From(InputRequestRecord request) => new(
        request.Id.ToString(),
        request.ServerId,
        request.Tool.ToString(),
        request.Mode,
        request.Message,
        request.RequestedSchemaJson is { } schema ? JsonDocument.Parse(schema).RootElement.Clone() : null,
        request.Url,
        request.SessionId?.ToString(),
        request.TaskId?.ToString(),
        request.Status,
        request.CreatedAt,
        request.ExpiresAt);
}

/// <summary>The user's answer to an input request.</summary>
/// <param name="Content">Form mode: an object with the requested fields. URL mode: null once the page is done.</param>
public sealed record InputAnswerRequest(JsonElement? Content);
