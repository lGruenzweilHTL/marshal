namespace Marshal.Abstractions.Tools;

/// <summary>
/// The tool namespaces loaded in a run, in load order, with why each was loaded. Only grows within a run. Shared by
/// all copies of the run's execution context; a subagent run gets its own.
/// </summary>
public sealed class LoadedToolNamespaces
{
    private readonly Lock _lock = new();
    private readonly List<LoadedToolNamespace> _loaded = [];

    /// <summary>
    /// True when tool retrieval narrows this run's tool list. False when it is disabled or skipped (few visible
    /// tools): every visible tool is offered and <c>agent.find_tools</c> is hidden.
    /// </summary>
    public bool RetrievalActive { get; set; }

    /// <summary>Loaded namespaces in load order.</summary>
    public IReadOnlyList<LoadedToolNamespace> All
    {
        get
        {
            lock (_lock)
            {
                return [.. _loaded];
            }
        }
    }

    /// <summary>True when <paramref name="name"/> is loaded.</summary>
    public bool Contains(string name)
    {
        lock (_lock)
        {
            return _loaded.Exists(item => item.Name == name);
        }
    }

    /// <summary>Position of <paramref name="name"/> in load order, or -1.</summary>
    public int IndexOf(string name)
    {
        lock (_lock)
        {
            return _loaded.FindIndex(item => item.Name == name);
        }
    }

    /// <summary>Loads a namespace; returns false when it was already loaded (its first reason stays).</summary>
    public bool Load(string name, string reason, double? score = null)
    {
        lock (_lock)
        {
            if (_loaded.Exists(item => item.Name == name))
                return false;

            _loaded.Add(new LoadedToolNamespace(name, reason, score));
            return true;
        }
    }
}

/// <summary>A loaded namespace.</summary>
/// <param name="Name">Namespace.</param>
/// <param name="Reason">
/// <c>core</c>, <c>session</c> (loaded earlier in the session), <c>hint</c>, <c>retrieval</c>, <c>find_tools</c> or
/// <c>call</c> (the model called one of its tools).
/// </param>
/// <param name="Score">Similarity for <c>retrieval</c> and <c>find_tools</c>.</param>
public sealed record LoadedToolNamespace(string Name, string Reason, double? Score = null);
