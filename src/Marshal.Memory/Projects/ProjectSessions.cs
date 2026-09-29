using System.Collections.Concurrent;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Sessions;

namespace Marshal.Memory.Projects;

/// <summary>
/// In-memory project activation state per session: sticky weights, the last two turns (for topic shifts, corrections
/// and alias learning) and projects rejected in this session. Lost on restart, which only costs some stickiness.
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

    /// <summary>The current turn (last) and the one before; a correction in a turn usually refers to the one before.</summary>
    public List<ProjectTurn> Turns { get; } = [];

    /// <summary>The most recent turn, or null before the first one.</summary>
    public ProjectTurn? LastTurn => Turns.Count == 0 ? null : Turns[^1];

    /// <summary>The turn before the most recent one, or null.</summary>
    public ProjectTurn? PreviousTurn => Turns.Count < 2 ? null : Turns[^2];
}

/// <summary>A turn's text, embedding and activations, kept for the next turns and for corrections.</summary>
/// <param name="Phrase">Normalized turn text (the triggering phrase of inferred activations).</param>
/// <param name="Embedding">Turn embedding.</param>
/// <param name="Activations">What was active.</param>
public sealed record ProjectTurn(string Phrase, float[] Embedding, IReadOnlyList<ProjectActivation> Activations)
{
    /// <summary>
    /// Set when a correction or confirmation handled this turn's inferences; otherwise they count as confirmations
    /// once the following turn had its chance to correct them.
    /// </summary>
    public bool Handled { get; set; }
}
