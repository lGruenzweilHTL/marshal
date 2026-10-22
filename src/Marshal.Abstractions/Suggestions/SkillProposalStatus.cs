namespace Marshal.Abstractions.Suggestions;

/// <summary>Where a skill proposal stands.</summary>
public enum SkillProposalStatus
{
    /// <summary>Waiting in the Inbox.</summary>
    Open,

    /// <summary>The user downloaded the file (it stays listed until dismissed).</summary>
    Downloaded,

    Dismissed,

    /// <summary>The drafting model found an existing skill covering the workflow; never shown.</summary>
    Covered,
}
