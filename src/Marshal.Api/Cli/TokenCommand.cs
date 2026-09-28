using System.Globalization;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;

namespace Marshal.Api.Cli;

/// <summary>
/// <c>marshal tokens create --name &lt;name&gt; --scope &lt;scope&gt;... | list | revoke &lt;id&gt;</c>. Works without a web login;
/// tokens belong to the instance owner. Every change is audited with actor kind <c>system</c>.
/// </summary>
internal sealed class TokenCommand(
    IApiTokenService tokens,
    IAuditLog audit,
    IInstanceOwner instanceOwner,
    IExecutionContextAccessor executionContext,
    TimeProvider timeProvider,
    TextWriter output,
    TextWriter error)
{
    private const string Actor = "cli";
    private const string Usage = """
        Usage:
          marshal tokens create --name <name> --scope <scope> [--scope <scope>...] [--expires-in-days <days>]
          marshal tokens list
          marshal tokens revoke <token-id>
        """;

    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        if (args.Count == 0)
            return await UsageAsync();

        OwnerId owner = await instanceOwner.GetAsync(ct);
        using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Api, IsInteractive = false });
        return args[0] switch
        {
            "create" => await CreateAsync(args, ct),
            "list" => await ListAsync(ct),
            "revoke" => await RevokeAsync(args, ct),
            _ => await UsageAsync(),
        };
    }

    private async Task<int> CreateAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        string? name = null;
        int? expiresInDays = null;
        var scopes = new List<string>();
        for (int i = 1; i < args.Count; i++)
        {
            string? value = i + 1 < args.Count ? args[i + 1] : null;
            switch (args[i])
            {
                case "--name" when value is not null:
                    name = value;
                    i++;
                    break;
                case "--scope" when value is not null:
                    scopes.Add(value);
                    i++;
                    break;
                case "--expires-in-days" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int days) && days > 0:
                    expiresInDays = days;
                    i++;
                    break;
                default:
                    return await UsageAsync();
            }
        }

        if (name is null || scopes.Count == 0)
            return await UsageAsync();

        DateTimeOffset? expiresAt = expiresInDays is { } d ? timeProvider.GetUtcNow().AddDays(d) : null;
        Result<CreatedApiToken> created = await tokens.CreateAsync(name, scopes, expiresAt, ct);
        if (created.IsFailure)
        {
            await error.WriteLineAsync(created.Error.Message);
            return 1;
        }

        ApiTokenInfo info = created.Value.Info;
        await audit.RecordAsync(new AuditEntry(AuditActorKind.System, Actor, "token.create", info.Id.ToString(), new { info.Name, info.Scopes, info.ExpiresAt }), ct);
        await output.WriteLineAsync($"Created token '{info.Name}' ({info.Id}) with scopes: {string.Join(", ", info.Scopes)}");
        await output.WriteLineAsync(created.Value.Plaintext);
        await output.WriteLineAsync("Store it now; it is not shown again.");
        return 0;
    }

    private async Task<int> ListAsync(CancellationToken ct)
    {
        foreach (ApiTokenInfo token in await tokens.ListAsync(ct))
        {
            string state = token.RevokedAt is not null ? "revoked" : token.ExpiresAt is { } expires ? $"expires {expires:u}" : "active";
            await output.WriteLineAsync($"{token.Id}  {token.Name}  [{string.Join(", ", token.Scopes)}]  created {token.CreatedAt:u}  {state}");
        }

        return 0;
    }

    private async Task<int> RevokeAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        if (args.Count != 2 || !ApiTokenId.TryParse(args[1], CultureInfo.InvariantCulture, out ApiTokenId id))
            return await UsageAsync();

        Result revoked = await tokens.RevokeAsync(id, ct);
        if (revoked.IsFailure)
        {
            await error.WriteLineAsync(revoked.Error.Message);
            return 1;
        }

        await audit.RecordAsync(new AuditEntry(AuditActorKind.System, Actor, "token.revoke", id.ToString()), ct);
        await output.WriteLineAsync($"Revoked token {id}.");
        return 0;
    }

    private async Task<int> UsageAsync()
    {
        await error.WriteLineAsync(Usage);
        return 2;
    }
}
