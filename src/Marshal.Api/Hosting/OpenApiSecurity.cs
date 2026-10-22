using Marshal.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Marshal.Api.Hosting;

/// <summary>
/// Declares the two credentials (<c>apiToken</c>, <c>session</c>) and gives each operation the security requirements its
/// endpoint's authorization metadata enforces. An endpoint with a policy <see cref="Policies.Credentials(string?)"/> does
/// not know fails document generation, so the document never claims less than the server enforces.
/// </summary>
internal sealed class OpenApiSecurity : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public const string ApiToken = "apiToken";

    public const string Session = "session";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.AddComponent<IOpenApiSecurityScheme>(ApiToken, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "A scoped machine token (`marshal tokens create`, or Security in the web interface). The scopes an operation needs are listed with it.",
        });
        document.AddComponent<IOpenApiSecurityScheme>(Session, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = "marshal_session",
            Description = "The browser session of the web interface. State-changing requests also need the `X-XSRF-TOKEN` header from `GET /api/v1/auth/antiforgery`.",
        });
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        PolicyCredentials? credentials = Credentials(context.Description.ActionDescriptor.EndpointMetadata, context.Description.RelativePath);
        if (credentials is null)
            return Task.CompletedTask;

        operation.Security = [];
        if (credentials.TokenScopes is { } scopes)
            operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(ApiToken, context.Document)] = [.. scopes] });
        if (credentials.Session)
            operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(Session, context.Document)] = [] });
        return Task.CompletedTask;
    }

    /// <summary>The credentials all of an endpoint's policies accept together; <see langword="null"/> when it needs none.</summary>
    internal static PolicyCredentials? Credentials(IEnumerable<object> metadata, string? route)
    {
        List<object> items = [.. metadata];
        if (items.OfType<IAllowAnonymous>().Any())
            return null;

        PolicyCredentials? combined = null;
        foreach (object item in items)
        {
            PolicyCredentials? part;
            try
            {
                part = item switch
                {
                    IAuthorizeData { Roles: not null } or IAuthorizeData { AuthenticationSchemes: not null } =>
                        throw new InvalidOperationException("Role and scheme restrictions have no credential mapping."),
                    IAuthorizeData data => Policies.Credentials(data.Policy),
                    AuthorizationPolicy policy => Policies.Credentials(policy),
                    _ => null,
                };
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException($"Endpoint '{route}': {error.Message}", error);
            }

            if (part is not null)
                combined = combined is null ? part : combined.Combine(part);
        }

        return combined;
    }
}
