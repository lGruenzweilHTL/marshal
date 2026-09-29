using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Sessions;
using Marshal.Data;
using Marshal.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Memory.Projects;

/// <summary>
/// Decides the active projects of a turn without a model call.
/// </summary>
public interface IProjectActivator
{
    /// <summary>Active projects for the turn, and updates the session's stickiness state.</summary>
    Task<IReadOnlyList<ProjectActivation>> ActivateAsync(ActivationInput input, CancellationToken cancellationToken);
}

/// <summary>A turn to activate projects for.</summary>
/// <param name="Text">Turn text.</param>
/// <param name="Embedding">Turn embedding (the one retrieval uses).</param>
/// <param name="Session">Session for stickiness; null disables it.</param>
public sealed record ActivationInput(string Text, float[] Embedding, SessionId? Session);

/// <summary>
/// Explicit name / alias matches first, then the centroid score (cosine to the project centroid minus the penalty for
/// similarity to rejected phrases, plus a recency prior) with confidence tiers: high activates stickily, medium for
/// this turn, low not at all. Sticky activations decay per turn and are dropped on a topic shift. An inference the
/// next turn does not correct counts as a confirmation of its phrase (alias learning).
/// </summary>
public sealed class ProjectActivator(
    IDbContextFactory<MarshalDbContext> contexts,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ProjectSessions sessions,
    ProjectLearning learning,
    IOptions<MemoryOptions> options,
    TimeProvider timeProvider) : IProjectActivator
{
    public async Task<IReadOnlyList<ProjectActivation>> ActivateAsync(ActivationInput input, CancellationToken cancellationToken)
    {
        ProjectActivationOptions settings = options.Value.Projects;
        string phrase = ProjectLearning.Phrase(input.Text);
        ProjectSessionState? state = input.Session is { } session ? sessions.Get(session) : null;
        await ConfirmPreviousInferencesAsync(state, cancellationToken);
        Dictionary<ProjectId, double> sticky = Decay(state, input.Embedding, settings);

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<Project> projects = await db.Projects.Where(project => project.DeletedAt == null).ToListAsync(cancellationToken);
        var activations = new Dictionary<ProjectId, ProjectActivation>();
        if (projects.Count > 0)
        {
            await MatchExplicitAsync(db, projects, phrase, activations, cancellationToken);
            HashSet<ProjectId> rejected = state is null ? [] : Locked(state, () => state.Rejected.ToHashSet());
            await InferAsync(db, [.. projects.Where(project => !activations.ContainsKey(project.Id) && !rejected.Contains(project.Id))], input.Embedding, activations, settings, cancellationToken);

            foreach ((ProjectId id, double weight) in sticky)
            {
                if (!activations.ContainsKey(id) && projects.FirstOrDefault(project => project.Id == id) is { } project && !rejected.Contains(id))
                    activations[id] = new ProjectActivation(id, project.Name, ActivationReason.Sticky, weight, Sticky: true);
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            foreach (Project project in projects.Where(project => activations.ContainsKey(project.Id)))
                project.LastActiveAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        List<ProjectActivation> result = [.. activations.Values.OrderBy(activation => activation.Reason).ThenBy(activation => activation.Name, StringComparer.Ordinal)];
        if (state is not null)
        {
            Locked(state, () =>
            {
                foreach (ProjectActivation activation in result.Where(activation => activation.Sticky && activation.Reason != ActivationReason.Sticky))
                    state.Sticky[activation.ProjectId] = 1;
                state.Turns.Add(new ProjectTurn(phrase, input.Embedding, result));
                if (state.Turns.Count > 2)
                    state.Turns.RemoveAt(0);
                return true;
            });
        }

        return result;
    }

    // The turn before the previous one leaves the window now; the previous turn was its chance to be corrected, so
    // its unhandled inferences count as confirmations.
    private async Task ConfirmPreviousInferencesAsync(ProjectSessionState? state, CancellationToken cancellationToken)
    {
        ProjectTurn? leaving = state is null ? null : Locked(state, () => state.Turns.Count == 2 ? state.Turns[0] : null);
        if (leaving is null || leaving.Handled)
            return;

        leaving.Handled = true;
        foreach (ProjectActivation activation in leaving.Activations.Where(activation => activation.IsInferred))
            await learning.ConfirmAliasAsync(activation.ProjectId, leaving.Phrase, cancellationToken);
    }

    // Topic shift drops every sticky activation; otherwise each decays and the weak ones are dropped.
    private static Dictionary<ProjectId, double> Decay(ProjectSessionState? state, float[] embedding, ProjectActivationOptions settings)
    {
        if (state is null)
            return [];

        return Locked(state, () =>
        {
            if (state.LastTurn is { } last && Vectors.Cosine(embedding, last.Embedding) < settings.TopicShiftThreshold)
                state.Sticky.Clear();

            foreach (ProjectId id in state.Sticky.Keys.ToList())
            {
                state.Sticky[id] *= settings.StickyDecay;
                if (state.Sticky[id] < settings.StickyMinimum)
                    state.Sticky.Remove(id);
            }

            return new Dictionary<ProjectId, double>(state.Sticky);
        });
    }

    private static async Task MatchExplicitAsync(MarshalDbContext db, List<Project> projects, string phrase, Dictionary<ProjectId, ProjectActivation> activations, CancellationToken cancellationToken)
    {
        string text = $" {phrase} ";
        foreach (Project project in projects.Where(project => Contains(text, project.Name)))
            activations[project.Id] = new ProjectActivation(project.Id, project.Name, ActivationReason.Name, null, Sticky: true);

        List<ProjectId> live = [.. projects.Select(project => project.Id)];
        List<ProjectAlias> aliases = await db.ProjectAliases.AsNoTracking()
            .Where(alias => alias.Status == ProjectAliasStatus.Active && live.Contains(alias.ProjectId))
            .ToListAsync(cancellationToken);
        foreach (ProjectAlias alias in aliases.Where(alias => !activations.ContainsKey(alias.ProjectId) && Contains(text, alias.Phrase)))
        {
            Project project = projects.Single(candidate => candidate.Id == alias.ProjectId);
            activations[project.Id] = new ProjectActivation(project.Id, project.Name, ActivationReason.Alias, null, Sticky: true);
        }
    }

    private static bool Contains(string paddedText, string name)
    {
        string normalized = ChunkIds.Normalize(name);
        return normalized.Length > 0 && paddedText.Contains($" {normalized} ", StringComparison.Ordinal);
    }

    private async Task InferAsync(
        MarshalDbContext db, List<Project> candidates, float[] embedding, Dictionary<ProjectId, ProjectActivation> activations, ProjectActivationOptions settings, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
            return;

        List<ProjectId> ids = [.. candidates.Select(project => project.Id)];
        List<ProjectFeedback> negatives = await db.ProjectFeedback.AsNoTracking()
            .Where(feedback => feedback.Kind == ProjectFeedbackKind.Rejected && feedback.Embedding != null && ids.Contains(feedback.ProjectId))
            .ToListAsync(cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();
        foreach (Project project in candidates)
        {
            float[]? centroid = await CentroidAsync(db, project, settings, cancellationToken);
            if (centroid is null)
                continue;

            double penalty = negatives.Where(feedback => feedback.ProjectId == project.Id)
                .Select(feedback => Math.Max(0, Vectors.Cosine(embedding, Vectors.FromBlob(feedback.Embedding!))))
                .DefaultIfEmpty(0).Max() * settings.NegativeWeight;
            double score = Vectors.Cosine(embedding, centroid) - penalty;
            double recency = project.LastActiveAt is { } active
                ? settings.RecencyBoost * Math.Pow(0.5, Math.Max(0, (now - active).TotalDays) / settings.RecencyHalfLifeDays)
                : 0;
            double total = score + recency;
            if (total < settings.MediumConfidence)
                continue;

            bool high = total >= settings.HighConfidence;
            ActivationReason reason = score >= (high ? settings.HighConfidence : settings.MediumConfidence) ? ActivationReason.Centroid : ActivationReason.Recency;
            activations[project.Id] = new ProjectActivation(project.Id, project.Name, reason, Math.Round(total, 3), Sticky: high);
        }
    }

    // Computed on demand: writes to a project's chunks and description changes clear the stored centroid.
    private async Task<float[]?> CentroidAsync(MarshalDbContext db, Project project, ProjectActivationOptions settings, CancellationToken cancellationToken)
    {
        if (project.Centroid is { } stored)
            return Vectors.FromBlob(stored);

        List<ChunkId> top = await db.Chunks.AsNoTracking()
            .Where(chunk => chunk.ProjectId == project.Id && chunk.DeletedAt == null)
            .OrderByDescending(chunk => chunk.Importance).ThenByDescending(chunk => chunk.UpdatedAt)
            .Select(chunk => chunk.Id)
            .Take(settings.CentroidTopChunks)
            .ToListAsync(cancellationToken);
        List<float[]> vectors = [.. await MemoryIndexes.ReadVectorsAsync(db, top, cancellationToken)];
        if (!string.IsNullOrWhiteSpace(project.Description))
            vectors.Add((await embeddings.GenerateAsync([$"{project.Name}\n{project.Description}"], cancellationToken: cancellationToken)).Single().Vector.ToArray());

        float[]? centroid = Vectors.NormalizedMean(vectors);
        if (centroid is not null)
            project.Centroid = Vectors.ToBlob(centroid);

        return centroid;
    }

    private static T Locked<T>(ProjectSessionState state, Func<T> action)
    {
        lock (state)
        {
            return action();
        }
    }
}
