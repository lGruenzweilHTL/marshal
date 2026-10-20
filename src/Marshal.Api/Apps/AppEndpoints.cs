using System.Security.Claims;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Apps;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Marshal.Api.Widgets;
using Marshal.Core.Agent;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Apps;

/// <summary>
/// MCP apps in web chat: the app's HTML for the sandboxed iframe (<c>/mcp-apps/{appId}</c>), the app's call data, and
/// tool calls the app asks for, which run through the guard as their own run and never reach the model.
/// </summary>
internal static class AppEndpoints
{
    /// <summary>
    /// CSP of an app's HTML: inline scripts and styles, images from <c>data:</c>, nothing else (no connections, no
    /// frames, no forms), framed only by Marshal. <c>sandbox allow-scripts</c> keeps the document in an opaque origin
    /// even when it is opened outside the chat's iframe.
    /// </summary>
    public const string ContentSecurityPolicy =
        "sandbox allow-scripts; default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; frame-ancestors 'self'";

    /// <summary>Error code of an app's call that the guard put at confirm tier.</summary>
    public const string ConfirmDenied = "app_confirm";

    public static IEndpointRouteBuilder MapMcpAppHtml(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mcp-apps/{appId:guid}", GetHtmlAsync).RequireAuthorization(Policies.Human).ExcludeFromDescription();
        return app;
    }

    public static RouteGroupBuilder MapAppEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder apps = group.MapGroup("/apps").WithTags("Apps").RequireAuthorization(Policies.Human);
        apps.MapGet("/{appId:guid}", GetAsync).WithName("GetApp")
            .WithSummary("An MCP app a turn showed: server, tool, the call's arguments and result, and whether it can still be shown.");
        apps.MapPost("/{appId:guid}/tool-calls", CallToolAsync).WithName("CallAppTool")
            .WithSummary("Calls a tool of the app's own server for the app, through the guard as a run with trigger app:<server>/<tool>; confirm tier is denied.");
        return group;
    }

    private static async Task<IResult> GetHtmlAsync(Guid appId, HttpContext context, IAppInstanceStore store, CancellationToken ct)
    {
        if (await store.GetAsync(AppInstanceId.From(appId), ct) is not { } instance)
            return TypedResults.NotFound();

        IHeaderDictionary headers = context.Response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = "private, no-store";
        return TypedResults.Content(instance.Html, "text/html; charset=utf-8");
    }

    private static async Task<Results<Ok<AppResponse>, ProblemHttpResult>> GetAsync(Guid appId, IAppInstanceStore store, IMcpAppResources resources, CancellationToken ct)
    {
        if (await store.GetAsync(AppInstanceId.From(appId), ct) is not { } instance)
            return NotFound().ToProblem();

        // Shown again only with the complete result and while the server still serves the UI.
        bool available = instance.Result is not null
            && resources.AppNamespace(instance.Server) is not null
            && (await resources.ReadAsync(instance.Server, instance.ResourceUri, ct)).IsSuccess;
        return TypedResults.Ok(new AppResponse(
            instance.Id.ToString(),
            instance.Server,
            instance.Tool,
            instance.ToolCallId,
            JsonDocument.Parse(instance.Arguments).RootElement.Clone(),
            instance.Result is null ? null : JsonDocument.Parse(instance.Result).RootElement.Clone(),
            available));
    }

    private static async Task<Results<Ok<JsonElement>, ProblemHttpResult>> CallToolAsync(
        Guid appId, AppToolCallRequest request, ClaimsPrincipal user, IAppInstanceStore store, IMcpAppResources resources, IToolRegistry registry,
        IToolVisibilityFilter visibility, WidgetRuns runs, IToolCallPipeline pipeline, CancellationToken ct)
    {
        if (await store.GetAsync(AppInstanceId.From(appId), ct) is not { } instance)
            return NotFound().ToProblem();
        if (resources.AppNamespace(instance.Server) is not { } ns)
            return new Error("app.unavailable", $"The MCP server '{instance.Server}' no longer shows apps.", ErrorType.Unavailable).ToProblem();
        if (string.IsNullOrWhiteSpace(request.Name))
            return NotAllowed(request.Name).ToProblem();

        // Only the app's own server: the id is built from the server's namespace, never taken from the app.
        var id = new ToolId(ns, request.Name);
        RegisteredTool? tool = registry.Current.FindById(id);
        if (tool is null || tool.IsBuiltIn || tool.Source != instance.Server)
            return NotAllowed(request.Name).ToProblem();

        JsonElement arguments = request.Arguments is { ValueKind: JsonValueKind.Object } given ? given : JsonSerializer.SerializeToElement(new { });
        Result<JsonElement> result = await runs.RunTriggeredAsync(user, $"app:{instance.Server}/{request.Name}", context => CallAsync(tool, arguments, context, visibility, pipeline, ct), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<Result<JsonElement>> CallAsync(
        RegisteredTool tool, JsonElement arguments, MarshalExecutionContext context, IToolVisibilityFilter visibility, IToolCallPipeline pipeline, CancellationToken ct)
    {
        // The owner's web visibility rules apply as in a chat turn; the guard checks them again.
        if (!(await visibility.GetVisibleToolsAsync(context, ct)).Any(visible => visible.Id == tool.Id))
            return NotAllowed(tool.Id.Name);

        ToolCallOutcome outcome = await pipeline.CallAsync(new ToolCallRequest(tool.ModelName, $"app_{context.RunId!.Value.Value:N}", arguments, context, new HashSet<string>(StringComparer.Ordinal) { tool.ModelName })
        {
            ConfirmationDenial = new Error(ConfirmDenied, "This tool needs the user's confirmation and cannot be called from an app.", ErrorType.Forbidden),
        }, ct);
        if (outcome.Verdict?.Denial is { } denial)
            return denial;
        if (outcome.Result is not { } called)
            return new Error("app.call_failed", "The call did not run.", ErrorType.Failure);

        // A failed call reaches the app as an error result, like the server would answer it.
        return called.IsSuccess
            ? called.Value.CallResult is { } raw ? JsonDocument.Parse(raw).RootElement.Clone() : TextResult(called.Value.Content, isError: false)
            : TextResult(called.Error.Message, isError: true);
    }

    private static JsonElement TextResult(string text, bool isError) =>
        JsonSerializer.SerializeToElement(new { content = new[] { new { type = "text", text } }, isError }, JsonSerializerOptions.Web);

    private static Error NotFound() => new("app.not_found", "There is no such app.", ErrorType.NotFound);

    private static Error NotAllowed(string? name) =>
        new("app.tool_not_allowed", $"An app can only call visible tools of its own server; '{name}' is not one.", ErrorType.Forbidden);
}

/// <param name="Name">The tool name as the app's server reports it.</param>
/// <param name="Arguments">The call's arguments (a JSON object).</param>
public sealed record AppToolCallRequest(string Name, JsonElement? Arguments = null);

/// <param name="AppId">The app id.</param>
/// <param name="Server">The MCP server id.</param>
/// <param name="Tool">The tool name as the server reports it.</param>
/// <param name="ToolCallId">The tool call that showed the app.</param>
/// <param name="Arguments">The call's arguments.</param>
/// <param name="Result">The call's <c>CallToolResult</c>; null when it was too large to keep.</param>
/// <param name="Available">The app can be shown: the result is complete and the server still serves its UI.</param>
public sealed record AppResponse(string AppId, string Server, string Tool, string ToolCallId, JsonElement Arguments, JsonElement? Result, bool Available);
