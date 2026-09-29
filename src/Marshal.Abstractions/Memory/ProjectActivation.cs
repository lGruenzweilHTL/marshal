namespace Marshal.Abstractions.Memory;

/// <summary>Why a project is active for a turn.</summary>
public enum ActivationReason
{
    /// <summary>The turn names the project.</summary>
    Name,

    /// <summary>The turn contains an active alias.</summary>
    Alias,

    /// <summary>The turn embedding is close to the project centroid.</summary>
    Centroid,

    /// <summary>The centroid score reached the threshold only with the recency prior.</summary>
    Recency,

    /// <summary>Still active from an earlier turn of the session (decaying).</summary>
    Sticky,

    /// <summary>The run belongs to a task of the project (active on every run of the task).</summary>
    Task,
}

/// <summary>A project active for a turn.</summary>
/// <param name="ProjectId">The project.</param>
/// <param name="Name">Its name.</param>
/// <param name="Reason">Why it is active.</param>
/// <param name="Score">Centroid score, or the sticky weight; null for name and alias matches.</param>
/// <param name="Sticky">Stays active for the following turns of the session (with decay).</param>
public sealed record ProjectActivation(ProjectId ProjectId, string Name, ActivationReason Reason, double? Score, bool Sticky)
{
    /// <summary>Inferred (not named): corrections and alias learning apply.</summary>
    public bool IsInferred => Reason is ActivationReason.Centroid or ActivationReason.Recency;
}
