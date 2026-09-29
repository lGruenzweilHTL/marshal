using System.Collections.Concurrent;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Sessions;

namespace Marshal.Memory.Projects;

/// <summary>
/// In-memory project activation state per session: sticky weights, the previous turn (for topic shifts and
/// corrections) and projects rejected in this session. Lost on restart, which only costs some stickiness.
/// </summary>
public sealed class ProjectSessions
{
    private readonly ConcurrentDictionary<SessionId, ProjectSessionState> _sessions = new();

    /// <summary>The state of a session (created on first use).</summary>
    public ProjectSessionState Get(SessionId session) => _sessions.GetOrAdd(session, _ => new ProjectSessionState());

    /// <summary>Forgets a closed session.</summary>
    public void Remove(SessionId session) => _sessions.TryRemove(session, out _);
}

/// <summary>Activation state of one session. Callers lock on the instance while reading or changing it.</summary>
public sealed class ProjectSessionState
{
    /// <summary>Sticky activations and their current weight.</summary>
    public Dictionary<ProjectId, double> Sticky { get; } = [];

    /// <summary>Projects the user rejected in this session; not inferred again (explicit mentions still activate).</summary>
    public HashSet<ProjectId> Rejected { get; } = [];

    /// <summary>The previous turn, or null before the first one.</summary>
    public ProjectTurn? LastTurn { get; set; }
}

/// <summary>A turn's text, embedding and activations, kept for the next turn and for corrections.</summary>
/// <param name="Phrase">Normalized turn text (the triggering phrase of inferred activations).</param>
/// <param name="Embedding">Turn embedding.</param>
/// <param name="Activations">What was active.</param>
public sealed record ProjectTurn(string Phrase, float[] Embedding, IReadOnlyList<ProjectActivation> Activations)
{
    /// <summary>Set when a correction referred to this turn; its inferences then do not count as confirmations.</summary>
    public bool Corrected { get; set; }
}
