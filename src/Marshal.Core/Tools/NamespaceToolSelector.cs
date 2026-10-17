using Marshal.Abstractions.Context;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tools;

/// <summary>
/// Tool retrieval by namespace: core namespaces, the session's earlier namespaces, deterministic hints and the best
/// matching descriptions for the turn embedding (reused from memory retrieval; no extra embedding call per turn).
/// Without an embedding, only core, session, hints, <c>agent.find_tools</c> and direct calls load namespaces.
/// </summary>
public sealed partial class NamespaceToolSelector(
    IOptions<ToolsOptions> options,
    IEnumerable<IToolNamespaceHints> hints,
    INamespaceEmbeddings namespaceEmbeddings,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ILogger<NamespaceToolSelector> logger,
    ITurnEmbedding? turnEmbedding = null) : IToolSelector
{
    /// <summary>Id of the tool that finds more namespaces; offered only while some visible namespace is unloaded.</summary>
    public static readonly ToolId FindToolsId = new("agent", "find_tools");

    public async Task PrepareAsync(IReadOnlyList<RegisteredTool> visible, ToolSelectionRequest request, CancellationToken cancellationToken)
    {
        MarshalExecutionContext context = request.Turn.Context;
        LoadedToolNamespaces loaded = context.LoadedTools;
        ToolRetrievalOptions retrieval = options.Value.Retrieval;
        loaded.RetrievalActive = retrieval.Enabled && visible.Count > retrieval.MinTools;
        if (!loaded.RetrievalActive)
            return;

        foreach (string name in options.Value.CoreNamespaces)
            loaded.Load(name, LoadReasons.Core);
        foreach (string name in request.SessionNamespaces)
            loaded.Load(name, LoadReasons.Session);

        HashSet<string> candidates = [.. visible.Select(tool => tool.Id.Namespace)];
        string? text = request.Turn.UserText;
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (string name in await HintsAsync(text, context, cancellationToken))
            {
                if (candidates.Contains(name))
                    loaded.Load(name, LoadReasons.Hint);
            }

            ReadOnlyMemory<float>? turn = await TurnEmbeddingAsync(request.Turn, cancellationToken);
            if (turn is { } vector)
            {
                foreach ((string name, double score) in await RankAsync(vector, Unloaded(candidates, loaded, context), cancellationToken))
                {
                    if (score >= retrieval.Threshold && loaded.All.Count(item => item.Reason == LoadReasons.Retrieval) < retrieval.MaxPerTurn)
                        loaded.Load(name, LoadReasons.Retrieval, Math.Round(score, 3));
                }
            }
        }

        foreach (LoadedToolNamespace item in loaded.All)
            request.Turn.Notes.Add(new ContextNote(LoadReasons.NoteKind, item.Name, item.Reason, item.Score));
    }

    public IReadOnlyList<RegisteredTool> Select(IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context)
    {
        LoadedToolNamespaces loaded = context.LoadedTools;
        if (!loaded.RetrievalActive)
            return [.. visible.Where(tool => tool.Id != FindToolsId)];

        bool anyUnloaded = visible.Any(tool => !loaded.Contains(tool.Id.Namespace));
        return
        [
            .. visible
                .Where(tool => loaded.Contains(tool.Id.Namespace) && (anyUnloaded || tool.Id != FindToolsId))
                .OrderBy(tool => loaded.IndexOf(tool.Id.Namespace))
                .ThenBy(tool => tool.Id.Name, StringComparer.Ordinal),
        ];
    }

    public async Task<IReadOnlyList<ToolNamespace>> FindAsync(string query, IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        LoadedToolNamespaces loaded = context.LoadedTools;
        if (!loaded.RetrievalActive || string.IsNullOrWhiteSpace(query))
            return [];

        IReadOnlyList<ToolNamespace> unloaded = Unloaded(visible.Select(tool => tool.Id.Namespace).ToHashSet(StringComparer.Ordinal), loaded, context);

        // A namespace named in the query ranks first; the rest rank by description similarity above the threshold.
        string[] words = query.ToLowerInvariant().Split([' ', ',', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        List<(string Name, double Score)> ranked = [.. unloaded.Where(item => words.Contains(item.Name)).Select(item => (item.Name, 1.0))];
        float[] vector = (await embeddings.GenerateAsync([query], cancellationToken: cancellationToken)).Single().Vector.ToArray();
        ranked.AddRange((await RankAsync(vector, unloaded, cancellationToken))
            .Where(item => item.Score >= options.Value.Retrieval.Threshold && ranked.TrueForAll(named => named.Name != item.Name)));

        var found = new List<ToolNamespace>();
        foreach ((string name, double score) in ranked.Take(options.Value.Retrieval.MaxPerTurn))
        {
            if (loaded.Load(name, LoadReasons.FindTools, Math.Round(score, 3)))
                found.Add(unloaded.First(item => item.Name == name));
        }

        return found;
    }

    // Namespaces with visible tools that are not loaded, with their descriptions (the namespace name when a source declares none).
    private static IReadOnlyList<ToolNamespace> Unloaded(IReadOnlySet<string> candidates, LoadedToolNamespaces loaded, MarshalExecutionContext context)
    {
        IReadOnlyList<ToolNamespace> described = context.Tools?.Namespaces ?? [];
        return
        [
            .. candidates
                .Where(name => !loaded.Contains(name))
                .Order(StringComparer.Ordinal)
                .Select(name => described.FirstOrDefault(item => item.Name == name) ?? new ToolNamespace(name, name)),
        ];
    }

    private async Task<IReadOnlyList<(string Name, double Score)>> RankAsync(ReadOnlyMemory<float> vector, IReadOnlyList<ToolNamespace> namespaces, CancellationToken cancellationToken)
    {
        if (namespaces.Count == 0)
            return [];

        IReadOnlyDictionary<string, ReadOnlyMemory<float>> described = await namespaceEmbeddings.GetAsync(namespaces, cancellationToken);
        return
        [
            .. namespaces
                .Select(item => (item.Name, Score: Cosine(vector.Span, described[item.Name].Span)))
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Name, StringComparer.Ordinal),
        ];
    }

    private async Task<IReadOnlyList<string>> HintsAsync(string text, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        foreach (IToolNamespaceHints hint in hints)
        {
            try
            {
                names.AddRange(await hint.GetHintsAsync(text, context, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Hints only save a round trip; the model can still find the tools.
                LogHintFailed(logger, exception, hint.GetType().Name);
            }
        }

        return names;
    }

    private async Task<ReadOnlyMemory<float>?> TurnEmbeddingAsync(ContextRequest turn, CancellationToken cancellationToken)
    {
        if (turnEmbedding is null)
            return null;

        try
        {
            return await turnEmbedding.GetAsync(turn, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogEmbeddingFailed(logger, exception);
            return null;
        }
    }

    private static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;

        double dot = 0;
        double normA = 0;
        double normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool namespace hint {Hint} failed")]
    private static partial void LogHintFailed(ILogger logger, Exception exception, string hint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Turn embedding for tool retrieval failed; using core, session and hinted namespaces")]
    private static partial void LogEmbeddingFailed(ILogger logger, Exception exception);
}
