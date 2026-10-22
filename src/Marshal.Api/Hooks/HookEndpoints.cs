using System.Security.Claims;
using System.Threading.RateLimiting;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Marshal.Tasks.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Hooks;

/// <summary>
/// <c>POST /hooks/{source}</c>: webhooks from infrastructure tools (Uptime Kuma), authenticated with a machine token
/// carrying <c>hooks:&lt;source&gt;</c>. The source is checked before the scope, so a 403 does not reveal which
/// sources exist and a 404 does not reveal the token's scopes. Bodies over 64 KB get 413; each token gets
/// <c>webhooks.requestsPerMinute</c> requests per minute, then 429.
/// </summary>
internal static class HookEndpoints
{
    public const string RateLimitPolicy = "webhooks";

    public const int MaxBodyBytes = 64 * 1024;

    private static readonly AuthorizationPolicy _machineToken = new AuthorizationPolicyBuilder(MarshalAuthSchemes.ApiToken).RequireAuthenticatedUser().Build();

    public static RouteGroupBuilder MapHookEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/hooks/{source}", ReceiveAsync)
            .RequireAuthorization(_machineToken)
            .RequireRateLimiting(RateLimitPolicy)
            .DisableAntiforgery()
            .Accepts<object>("application/json", "multipart/form-data", "application/x-www-form-urlencoded")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithTags("Hooks").WithName("PostWebhook")
            .WithDescription("Receives a webhook of a source (e.g. uptime-kuma) and publishes its events.");
        return group;
    }

    /// <summary>The fixed-window limiter per token (<c>webhooks.requestsPerMinute</c>).</summary>
    public static IServiceCollection AddHookRateLimiter(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                Error error = new("webhook.rate_limited", "Too many webhook requests for this token. Try again in a minute.");
                await TypedResults.Problem(detail: error.Message, statusCode: StatusCodes.Status429TooManyRequests, title: error.Code,
                    extensions: new Dictionary<string, object?> { ["code"] = error.Code }).ExecuteAsync(context.HttpContext);
            };
            options.AddPolicy(RateLimitPolicy, context =>
            {
                int permits = context.RequestServices.GetRequiredService<IOptions<WebhookOptions>>().Value.RequestsPerMinute;
                string key = context.User.FindFirstValue(MarshalClaims.TokenId) ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
            });
        });
        return services;
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> ReceiveAsync(string source, HttpContext context, ClaimsPrincipal user, IWebhookIngest ingest)
    {
        if (!ingest.Sources.Contains(source, StringComparer.Ordinal))
            return WebhookErrors.UnknownSource(source).ToProblem();
        if (!user.HasClaim(MarshalClaims.Scope, ApiScopes.Hooks(source)))
            return new Error("webhook.forbidden", $"This token cannot post '{source}' webhooks; it needs the scope '{ApiScopes.Hooks(source)}'.", ErrorType.Forbidden).ToProblem();
        if (!OwnerId.TryParse(user.FindFirstValue(MarshalClaims.Owner), null, out OwnerId owner))
            return new Error("webhook.forbidden", "This token has no owner.", ErrorType.Forbidden).ToProblem();

        Result<WebhookRequest> request = await ReadAsync(context);
        if (request.IsFailure)
            return request.Error.Type == ErrorType.Failure ? TooLarge() : request.Error.ToProblem();

        Result<int> ingested = ingest.Ingest(source, owner, request.Value);
        return ingested.IsSuccess ? TypedResults.Accepted((string?)null) : ingested.Error.ToProblem();
    }

    // Reads at most MaxBodyBytes; a larger body is a Failure (413), an unreadable form a Validation error (400).
    private static async Task<Result<WebhookRequest>> ReadAsync(HttpContext context)
    {
        HttpRequest request = context.Request;
        if (request.ContentLength > MaxBodyBytes)
            return new Error("webhook.too_large", "The webhook body is larger than 64 KB.");
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
            bodySize.MaxRequestBodySize = MaxBodyBytes;

        try
        {
            if (request.HasFormContentType)
            {
                IFormCollection form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = MaxBodyBytes, ValueLengthLimit = MaxBodyBytes }, context.RequestAborted);
                return new WebhookRequest(request.ContentType, ReadOnlyMemory<byte>.Empty, form.ToDictionary(field => field.Key, field => field.Value.ToString(), StringComparer.Ordinal));
            }

            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int read;
            while ((read = await request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes)
                    return new Error("webhook.too_large", "The webhook body is larger than 64 KB.");
                buffer.Write(chunk, 0, read);
            }

            return new WebhookRequest(request.ContentType, buffer.ToArray(), new Dictionary<string, string>());
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return new Error("webhook.too_large", "The webhook body is larger than 64 KB.");
        }
        catch (InvalidDataException exception)
        {
            return WebhookErrors.InvalidBody(exception.Message);
        }
    }

    private static ProblemHttpResult TooLarge() => TypedResults.Problem(
        detail: "The webhook body is larger than 64 KB.", statusCode: StatusCodes.Status413PayloadTooLarge, title: "webhook.too_large",
        extensions: new Dictionary<string, object?> { ["code"] = "webhook.too_large" });
}
