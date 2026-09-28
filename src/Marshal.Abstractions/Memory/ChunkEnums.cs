namespace Marshal.Abstractions.Memory;

/// <summary>Where a chunk applies.</summary>
public enum ChunkScope
{
    /// <summary>Everywhere.</summary>
    General,

    /// <summary>Within one project (the chunk has a project id).</summary>
    Project,
}

/// <summary>Kind of knowledge a chunk holds.</summary>
public enum ChunkType
{
    Fact,
    Preference,
    Event,
    Procedure,
    Person,
    Device,
}

/// <summary>Relation between two chunks (from → to).</summary>
public enum ChunkEdgeKind
{
    /// <summary>From replaces to (never from a chunk to itself).</summary>
    Supersedes,

    /// <summary>From and to disagree; open until one supersedes the other or is deleted.</summary>
    Contradicts,

    DependsOn,

    RelatesTo,
}

/// <summary>Who wrote a chunk.</summary>
public enum ChunkAuthor
{
    /// <summary>The user in the web UI or through the API.</summary>
    User,

    /// <summary>The agent through <c>memory.remember</c>.</summary>
    Agent,

    /// <summary>Session-close summarization.</summary>
    Consolidation,

    /// <summary>An NDJSON import.</summary>
    Import,
}
