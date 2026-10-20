using System.Globalization;
using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Memory.Retrieval;

/// <summary>
/// Pinned chunks, always and in full, before any retrieved memory.
/// </summary>
public sealed class PinnedMemoryContributor(IMemoryRetriever retriever) : IContextContributor
{
    public ContextStability Stability => ContextStability.Session;

    public int Order => 150;

    public async Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<MemoryChunk> pinned = await retriever.GetPinnedAsync(cancellationToken);
        foreach (MemoryChunk chunk in pinned)
            request.Notes.Add(new ContextNote("memory", chunk.Title, $"{MemoryText.Type(chunk.Type)}, pinned"));

        return pinned.Count == 0 ? null : MemoryText.Section("Pinned memory (always applies):", pinned);
    }
}

/// <summary>
/// The summaries of the turn's active projects (written by the idle-time jobs from memory, so trusted). Projects
/// without a summary add nothing.
/// </summary>
public sealed class ProjectSummaryContributor(ITurnMemory turns, IDbContextFactory<MarshalDbContext> contexts) : IContextContributor
{
    public ContextStability Stability => ContextStability.Turn;

    public int Order => 155;

    public async Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        TurnMemoryContext turn;
        try
        {
            turn = await turns.GetAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The retrieved-memory contributor reports the failure.
            return null;
        }

        List<ProjectId> active = [.. turn.ActiveProjects];
        if (active.Count == 0)
            return null;

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        var summaries = await db.Projects.AsNoTracking()
            .Where(project => active.Contains(project.Id) && project.DeletedAt == null && project.Summary != null)
            .Select(project => new { project.Name, project.Summary })
            .ToListAsync(cancellationToken);
        if (summaries.Count == 0)
            return null;

        StringBuilder text = new StringBuilder("Active projects:").Append('\n');
        foreach (var project in summaries.OrderBy(project => project.Name, StringComparer.Ordinal))
            text.Append("- ").Append(project.Name).Append(": ").AppendLine(project.Summary);

        return text.ToString();
    }
}

/// <summary>
/// Memory retrieved for the turn server-side, without a model tool call, using the turn embedding. When the embedding
/// server or the index fails, the turn continues without retrieved memory (device control must not depend on it).
/// </summary>
public sealed partial class RetrievedMemoryContributor(
    IMemoryRetriever retriever,
    ITurnMemory turns,
    IUserProfileProvider profiles,
    IOptions<MemoryOptions> options,
    TimeProvider timeProvider,
    ILogger<RetrievedMemoryContributor> logger) : IContextContributor
{
    public ContextStability Stability => ContextStability.Turn;

    public int Order => 160;

    public async Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        TurnMemoryContext turn;
        try
        {
            turn = await turns.GetAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable(request, "embedding failed", exception);
        }

        if (turn.Text is null)
            return null;

        foreach (ProjectActivation activation in turn.Activations)
            request.Notes.Add(new ContextNote("project", activation.Name, MemoryText.Snake(activation.Reason.ToString()), Round(activation.Score)));

        IReadOnlyList<RetrievedChunk> chunks;
        try
        {
            chunks = await retriever.RetrieveAsync(
                new MemoryQuery(turn.Text, turn.Embedding, turn.ActiveProjects, options.Value.Retrieval.TokenBudget), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable(request, "retrieval failed", exception);
        }

        foreach (RetrievedChunk chunk in chunks)
            request.Notes.Add(new ContextNote("memory", chunk.Chunk.Title, $"{MemoryText.Type(chunk.Chunk.Type)} via {MemoryText.Snake(chunk.Source.ToString())}", Round(chunk.Score)));

        if (chunks.Count == 0)
            return null;

        var validTime = new ValidTimeText(await profiles.ZoneAsync(request.Context.OwnerId, cancellationToken), timeProvider.GetUtcNow());
        return MemoryText.Section("Relevant memory (retrieved automatically; it may be outdated, and what the user says now wins):", [.. chunks.Select(chunk => chunk.Chunk)], validTime);
    }

    private static double? Round(double? score) => score is { } value ? Math.Round(value, 3) : null;

    private string? Unavailable(ContextRequest request, string reason, Exception exception)
    {
        LogUnavailable(logger, exception, reason);
        request.Notes.Add(new ContextNote("memory", "unavailable", reason));
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Memory retrieval skipped for this turn: {Reason}")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, string reason);
}

/// <summary>
/// How valid time is shown: in the owner's time zone, for chunks not yet valid or with an end, or always (a search at a
/// past time).
/// </summary>
internal sealed record ValidTimeText(TimeZoneInfo Zone, DateTimeOffset Now, bool Always = false)
{
    public string Suffix(MemoryChunk chunk)
    {
        bool future = chunk.ValidFrom > Now;
        return (Always || future, chunk.ValidTo) switch
        {
            (true, null) => $" (valid from {Format(chunk.ValidFrom)})",
            (true, { } end) => $" (valid {Format(chunk.ValidFrom)} to {Format(end)})",
            (false, { } end) => $" (valid until {Format(end)})",
            _ => "",
        };
    }

    // A date alone when the time is midnight in the owner's zone, which is how dates are stored.
    private string Format(DateTimeOffset instant)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(instant, Zone);
        return local.ToString(local.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}

/// <summary>How chunks appear in the system prompt and in tool results.</summary>
internal static class MemoryText
{
    public static string Section(string heading, IEnumerable<MemoryChunk> chunks, ValidTimeText? validTime = null)
    {
        StringBuilder text = new StringBuilder(heading).Append('\n');
        foreach (MemoryChunk chunk in chunks)
            text.AppendLine(Line(chunk, validTime));

        return text.ToString();
    }

    public static string Line(MemoryChunk chunk, ValidTimeText? validTime = null) =>
        string.Create(CultureInfo.InvariantCulture, $"- [{Type(chunk.Type)}] {chunk.Title}: {chunk.Content}{validTime?.Suffix(chunk)}");

    /// <summary>The owner's time zone; UTC when the profile or its zone id cannot be resolved.</summary>
    public static async Task<TimeZoneInfo> ZoneAsync(this IUserProfileProvider profiles, OwnerId owner, CancellationToken cancellationToken)
    {
        Result<UserProfile> profile = await profiles.GetAsync(owner, cancellationToken);
        return profile.IsSuccess && TimeZoneInfo.TryFindSystemTimeZoneById(profile.Value.TimeZone, out TimeZoneInfo? zone) ? zone : TimeZoneInfo.Utc;
    }

    public static string Type(ChunkType type) => Snake(type.ToString());

    public static string Snake(string name) => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}
