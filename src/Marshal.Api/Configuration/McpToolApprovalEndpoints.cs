using System.Security.Claims;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tools;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Marshal.Core.Mcp;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Configuration;

/// <summary>
/// <c>/config/mcp-tools</c>: MCP tools waiting for pin approval, and the decision. Deciding is the config view's only
/// write action; only a signed-in admin decides, never a token.
/// </summary>
internal static class McpToolApprovalEndpoints
{
    public static RouteGroupBuilder MapMcpToolApprovalEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder tools = group.MapGroup("/config/mcp-tools").WithTags("Config");
        tools.MapGet("/pending", ListPendingAsync).RequireAuthorization(Policies.Admin).WithName("ListPendingMcpTools")
            .WithSummary("MCP tools that are new or changed since the last approval, with the previous and current definitions.");
        tools.MapPost("/decisions", DecideAsync).RequireAuthorization(Policies.Human, Policies.Admin).WithName("DecideMcpTool")
            .WithSummary("Approves (the current definition becomes trusted) or rejects (hidden until it changes again) a waiting MCP tool.");
        return group;
    }

    private static async Task<Ok<IReadOnlyList<McpToolApproval>>> ListPendingAsync(IMcpToolPins pins, CancellationToken ct) =>
        TypedResults.Ok<IReadOnlyList<McpToolApproval>>([.. (await pins.ListPendingAsync(ct)).Select(McpToolApproval.From)]);

    private static async Task<Results<Ok<McpToolApproval>, ProblemHttpResult>> DecideAsync(
        McpToolDecisionRequest request, McpToolPinner pinner, ClaimsPrincipal user, IAuditLog audit, CancellationToken ct)
    {
        Result<McpToolPin> decided = await pinner.DecideAsync(request.ServerId, request.ToolName, request.Approve, user.Owner(), ct);
        if (decided.IsFailure)
            return decided.Error.ToProblem();

        (AuditActorKind kind, string actor) = user.Actor();
        await audit.RecordAsync(new AuditEntry(kind, actor, request.Approve ? "mcp.tool_approve" : "mcp.tool_reject", $"{request.ServerId}/{request.ToolName}",
            new { fingerprint = decided.Value.Fingerprint }), ct);
        return TypedResults.Ok(McpToolApproval.From(decided.Value));
    }
}

/// <summary>A decision on a waiting MCP tool.</summary>
/// <param name="ServerId">MCP server id.</param>
/// <param name="ToolName">Tool name as the server reports it.</param>
/// <param name="Approve">True approves the current definition, false rejects it.</param>
public sealed record McpToolDecisionRequest(string ServerId, string ToolName, bool Approve);

/// <summary>An MCP tool pin with its definitions.</summary>
/// <param name="ServerId">MCP server id.</param>
/// <param name="ToolName">Tool name as the server reports it.</param>
/// <param name="Change"><c>new</c> or <c>changed</c>.</param>
/// <param name="State"><c>approved</c>, <c>pending</c> or <c>rejected</c>.</param>
/// <param name="Previous">The last approved definition (name, title, description, inputSchema, outputSchema, annotations); null for a new tool.</param>
/// <param name="Current">The definition the server reports now. Untrusted server text: show it, never render it as HTML.</param>
/// <param name="FirstSeen">When the server first reported the current definition.</param>
public sealed record McpToolApproval(string ServerId, string ToolName, McpToolChange Change, ToolPinState State, JsonElement? Previous, JsonElement Current, DateTimeOffset FirstSeen)
{
    internal static McpToolApproval From(McpToolPin pin) => new(
        pin.ServerId,
        pin.ToolName,
        pin.Change,
        pin.State,
        pin.PreviousDefinitionJson is { } previous ? JsonDocument.Parse(previous).RootElement.Clone() : null,
        JsonDocument.Parse(pin.CurrentDefinitionJson).RootElement.Clone(),
        pin.FirstSeen);
}
