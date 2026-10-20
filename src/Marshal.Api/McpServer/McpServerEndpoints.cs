using Marshal.Abstractions.Tokens;
using Marshal.Api.Auth;
using ModelContextProtocol.Protocol;

namespace Marshal.Api.McpServer;

/// <summary>
/// Marshal's own MCP server at <c>/mcp</c>: streamable HTTP, stateless, tools only, for machine tokens with
/// <c>mcp:read</c> or <c>mcp:write</c>. Unversioned: the MCP protocol versions itself.
/// </summary>
internal static class McpServerEndpoints
{
    public const string Path = "/mcp";

    /// <summary>The policy of <c>/mcp</c>: a token principal with an MCP scope. Cookie principals get 403.</summary>
    public const string Policy = "mcp";

    public static IServiceCollection AddMarshalMcpServer(this IServiceCollection services)
    {
        services.AddSingleton<McpServerToolCatalog>();
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "marshal", Version = typeof(McpServerEndpoints).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" };
                options.ServerInstructions = "Marshal is the user's self-hosted personal assistant. Use it to recall what Marshal remembers, search the user's notes, see and create Marshal's background tasks, and ask Marshal to act with its own tools.";
            })
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithListToolsHandler(async (request, ct) => new ListToolsResult
            {
                Tools = await request.Services!.GetRequiredService<McpServerToolCatalog>().ListAsync(request.User!, ct),
            })
            .WithCallToolHandler((request, ct) => new ValueTask<CallToolResult>(
                request.Services!.GetRequiredService<McpServerToolCatalog>().CallAsync(request.User!, request.Params!.Name, request.Params.Arguments, ct)));
        return services;
    }

    public static void AddMcpPolicy(this Microsoft.AspNetCore.Authorization.AuthorizationOptions options) =>
        options.AddPolicy(Policy, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
            context.User.Identity?.AuthenticationType == MarshalAuthSchemes.ApiToken
            && (context.User.HasClaim(MarshalClaims.Scope, ApiScopes.McpRead) || context.User.HasClaim(MarshalClaims.Scope, ApiScopes.McpWrite))));

    public static void MapMarshalMcp(this IEndpointRouteBuilder app) =>
        app.MapMcp(Path).RequireAuthorization(Policy);
}
