namespace Marshal.Abstractions.Tasks;

/// <summary>
/// A preparation run of a <c>cron</c> task: <see cref="Before"/> each scheduled run, a run with the task's template,
/// tool policy and budget works on <see cref="Goal"/>; its result becomes the next main run's prepared notes.
/// </summary>
/// <param name="Before">How long before each scheduled run (more than zero, at most <see cref="MaxBefore"/>).</param>
/// <param name="Goal">What the preparation run collects.</param>
public sealed record TaskPreparation(TimeSpan Before, string Goal)
{
    /// <summary>The longest lead time; older notes would be too stale to help.</summary>
    public static TimeSpan MaxBefore { get; } = TimeSpan.FromHours(12);

    /// <summary>Prepared notes older than this at the main run are discarded.</summary>
    public TimeSpan MaxAge => Before + TimeSpan.FromHours(1);
}

/// <summary>The result of a preparation run, waiting for the next main run.</summary>
/// <param name="Body">The preparation run's result body.</param>
/// <param name="Taint">Taint kinds of the preparation run; the main run starts tainted with them.</param>
/// <param name="At">When the preparation run finished.</param>
public sealed record PreparedNotes(string Body, IReadOnlyList<string> Taint, DateTimeOffset At);
