using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Execution;

/// <summary>
/// Who a run acts for and under which policy. Tools, the guard and data access read the user from here,
/// never from a global.
/// </summary>
public sealed record MarshalExecutionContext
{
    /// <summary>The user the run acts for.</summary>
    public required OwnerId OwnerId { get; init; }

    /// <summary>Kind of client that started the run.</summary>
    public required SessionSource Source { get; init; }

    /// <summary>How the user entered the turn.</summary>
    public InputMode InputMode { get; init; } = InputMode.Text;

    /// <summary>Reply language (<c>de</c> or <c>en</c>).</summary>
    public string Language { get; init; } = "en";

    /// <summary>Satellite entity id the turn came from, if any.</summary>
    public string? SatelliteId { get; init; }

    /// <summary>Home Assistant device id the turn came from (e.g. a voice satellite), if any.</summary>
    public string? DeviceId { get; init; }

    /// <summary>Area id the turn came from, if any.</summary>
    public string? AreaId { get; init; }

    /// <summary>Session the run belongs to, if any.</summary>
    public SessionId? SessionId { get; init; }

    /// <summary>Task the run belongs to, for task runs.</summary>
    public TaskId? TaskId { get; init; }

    /// <summary>Project that is active on every turn of the run (a task's project).</summary>
    public ProjectId? ProjectId { get; init; }

    /// <summary>The run, once recording started.</summary>
    public RunId? RunId { get; init; }

    /// <summary>True when a user waits for the reply (voice or text turn).</summary>
    public bool IsInteractive { get; init; } = true;

    /// <summary>Taint of the run. Shared by all copies of this context within one run.</summary>
    public TaintState Taint { get; init; } = new();

    /// <summary>Per-run counters (e.g. advisor calls). Shared by all copies within one run.</summary>
    public RunCounters Counters { get; init; } = new();

    /// <summary>Model usage of the run. Shared by all copies within one run.</summary>
    public RunUsage Usage { get; init; } = new();

    /// <summary>Requests of the run's tools to its loop. Shared by all copies within one run.</summary>
    public RunControl Control { get; init; } = new();

    /// <summary>Agent template of the run.</summary>
    public string Template { get; init; } = "default";

    /// <summary>Delegation depth: 0 for a top-level run, 1 and more inside subagents.</summary>
    public int Depth { get; init; }

    /// <summary>True inside a subagent (no user to ask: confirm-tier actions are never run).</summary>
    public bool IsSubagent => Depth > 0;

    /// <summary>
    /// Tools fixed at run start (subagents inherit them), so the tool list never changes mid-turn. Null outside a
    /// run; the registry's current snapshot applies then.
    /// </summary>
    public ToolSnapshot? Tools { get; init; }
}
