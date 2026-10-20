using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tokens;
using Marshal.Abstractions.Tools;
using Marshal.Api.Auth;
using Marshal.Core.Agent;
using Marshal.Core.McpServer;
using Marshal.Core.Recording;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Marshal.Api.McpServer;

/// <summary>
/// The tools of Marshal's MCP server: a fixed mapping onto Marshal tools, filtered by the token's scopes and by the
/// tools' availability, plus <c>ask_marshal</c>. Calls run through <see cref="IToolCallPipeline"/> (guard,
/// execution, recording) as one-step runs with the token owner's context; this class never implements a tool.
/// </summary>
internal sealed class McpServerToolCatalog(
    IToolRegistry registry,
    IToolCallPipeline pipeline,
    IRunRecorder recorder,
    IAskMarshalRunner ask,
    IDailyCostCap cap,
    IUserProfileProvider profiles,
    IExecutionContextAccessor executionContext)
{
    public const string AskMarshal = "ask_marshal";

    /// <summary>Exposed Marshal tools by MCP name (the tool's model name) and the scope each needs.</summary>
    public static IReadOnlyList<(ToolId Tool, string Scope)> Mapped { get; } =
    [
        (new ToolId("memory", "recall"), ApiScopes.McpRead),
        (new ToolId("notes", "search"), ApiScopes.McpRead),
        (new ToolId("notes", "read"), ApiScopes.McpRead),
        (new ToolId("tasks", "list"), ApiScopes.McpRead),
        (new ToolId("tasks", "status"), ApiScopes.McpRead),
        (new ToolId("tasks", "create"), ApiScopes.McpWrite),
    ];

    private static readonly JsonElement _askSchema = JsonDocument.Parse("""
        {"type":"object","properties":{
          "text":{"type":"string","description":"What Marshal should do or answer, in plain language."},
          "conversationId":{"type":"string","description":"The conversationId of an earlier answer, to continue that conversation. Omit to start a new one."},
          "language":{"type":"string","enum":["de","en"],"description":"Reply language. Omit for the user's preference."}},
         "required":["text"],"additionalProperties":false}
        """).RootElement.Clone();

    public async Task<IList<Tool>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        MarshalExecutionContext context = await ContextAsync(user, cancellationToken);
        var tools = new List<Tool>();
        foreach (RegisteredTool tool in await AllowedAsync(user, context, cancellationToken))
        {
            tools.Add(new Tool
            {
                Name = tool.ModelName,
                Description = tool.Descriptor.Description,
                InputSchema = tool.Descriptor.ParametersSchema,
            });
        }

        if (user.Scopes().Contains(ApiScopes.McpWrite))
        {
            tools.Add(new Tool
            {
                Name = AskMarshal,
                Description = "Asks Marshal, the user's personal assistant, to answer or do something with its own tools (home, printers, mail, calendar, memory). Actions that need the user's approval wait in Marshal's review queue; say so instead of retrying.",
                InputSchema = _askSchema,
            });
        }

        return tools;
    }

    public async Task<CallToolResult> CallAsync(ClaimsPrincipal user, string name, IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        MarshalExecutionContext context = await ContextAsync(user, cancellationToken);
        if (name == AskMarshal && user.Scopes().Contains(ApiScopes.McpWrite))
            return await AskAsync(user, context, arguments, cancellationToken);

        RegisteredTool? tool = (await AllowedAsync(user, context, cancellationToken)).FirstOrDefault(candidate => candidate.ModelName == name);
        if (tool is null)
            throw new McpProtocolException($"Unknown tool: '{name}'", McpErrorCode.InvalidParams);

        if (await cap.LeftTodayAsync(context.OwnerId, cancellationToken) <= 0)
            return Failed(AskErrors.DailyCap().Message);

        string trigger = $"mcp:{name}";
        RunId runId;
        using (executionContext.Use(context))
            runId = await recorder.StartRunAsync(new RunStart(context, trigger), cancellationToken);

        context = context with { RunId = runId };
        using IDisposable scope = executionContext.Use(context);
        using Activity? run = RunTracing.StartRun(context, "mcp", trigger);
        run?.SetTag("marshal.run_id", runId.ToString());
        ToolCallOutcome outcome;
        using (RunTracing.StartTool(name, $"mcp_{runId.Value:N}"))
        {
            JsonElement json = JsonSerializer.SerializeToElement(arguments ?? new Dictionary<string, JsonElement>());
            outcome = await pipeline.CallAsync(new ToolCallRequest(name, $"mcp_{runId.Value:N}", json, context, new HashSet<string>(StringComparer.Ordinal) { name }), cancellationToken);
        }

        bool failed = outcome.Result is not { IsSuccess: true } && outcome.Frozen is null;
        RunOutcome result = failed ? RunOutcome.Error : RunOutcome.Ok;
        string? reason = failed ? outcome.Result?.Error.Code ?? outcome.Verdict?.Denial?.Code : null;
        await recorder.CompleteRunAsync(runId, new RunCompletion(result, reason, context.Taint.Sources), cancellationToken);
        RunTracing.CompleteRun(run, result, reason, context.Taint.Sources.Count > 0);
        return new CallToolResult { Content = [new TextContentBlock { Text = outcome.Text }], IsError = failed };
    }

    private async Task<CallToolResult> AskAsync(ClaimsPrincipal user, MarshalExecutionContext context, IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        string? text = Text(arguments, "text");
        if (string.IsNullOrWhiteSpace(text))
            return Failed("text is required.");

        var token = ApiTokenId.Parse(user.FindFirstValue(MarshalClaims.TokenId)!, null);
        Result<AskResult> answer = await ask.AskAsync(new AskRequest(context.OwnerId, token, text, Text(arguments, "conversationId"), Text(arguments, "language")), cancellationToken);
        if (answer.IsFailure)
            return Failed(answer.Error.Message);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = answer.Value.Reply }],
            StructuredContent = JsonSerializer.SerializeToElement(new { reply = answer.Value.Reply, conversationId = answer.Value.ConversationId }),
        };
    }

    // The scopes allow it and its capability is configured for the owner.
    private async Task<IReadOnlyList<RegisteredTool>> AllowedAsync(ClaimsPrincipal user, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> scopes = user.Scopes();
        ToolSnapshot snapshot = context.Tools ?? registry.Current;
        var allowed = new List<RegisteredTool>();
        foreach ((ToolId id, string scope) in Mapped)
        {
            if (!scopes.Contains(scope) && !(scope == ApiScopes.McpRead && scopes.Contains(ApiScopes.McpWrite)))
                continue;

            if (snapshot.FindById(id) is { } tool && await tool.Tool.IsAvailableAsync(context, cancellationToken))
                allowed.Add(tool);
        }

        return allowed;
    }

    private async Task<MarshalExecutionContext> ContextAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        OwnerId owner = user.Owner();
        Result<UserProfile> profile = await profiles.GetAsync(owner, cancellationToken);
        return new MarshalExecutionContext
        {
            OwnerId = owner,
            Source = SessionSource.Api,
            InputMode = InputMode.Text,
            Language = profile.IsSuccess ? profile.Value.Language : "en",
            IsInteractive = false,
            Tools = registry.Current,
        };
    }

    private static string? Text(IDictionary<string, JsonElement>? arguments, string name) =>
        arguments is not null && arguments.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static CallToolResult Failed(string message) => new() { Content = [new TextContentBlock { Text = message }], IsError = true };
}
