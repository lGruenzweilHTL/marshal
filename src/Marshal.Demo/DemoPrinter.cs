using Marshal.Abstractions.Printers;

namespace Marshal.Demo;

/// <summary>
/// The simulated printer <c>voron</c>: a print that started before the demo began and advances with the clock,
/// a file list, a job queue and a print history. Pausing stops the clock of the job; a finished or cancelled job
/// leaves the heaters off.
/// </summary>
public sealed class DemoPrinter
{
    public const string Name = "voron";
    public static readonly IReadOnlyList<string> Macros = ["PARK", "LOAD_FILAMENT", "UNLOAD_FILAMENT", "CLEAN_NOZZLE"];

    private const int Layers = 268;
    private const double FilamentPerJob = 11_800;

    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly List<PrintJob> _history = [];
    private readonly List<QueuedJob> _queue = [];
    private Job? _job;
    private int _nextJob = 412;

    internal DemoPrinter(TimeProvider time)
    {
        _time = time;
        DateTimeOffset now = time.GetUtcNow();
        Files =
        [
            new PrinterFile("planter_bracket.gcode", now.AddDays(-2), 2_480_113),
            new PrinterFile("gridfinity_bin_2x3.gcode", now.AddDays(-1), 3_912_004),
            new PrinterFile("cable_clip_x10.gcode", now.AddDays(-9), 610_552),
            new PrinterFile("voron_spare_parts.gcode", now.AddDays(-30), 12_330_871),
        ];
        _job = new Job("planter_bracket.gcode", now.AddMinutes(-52), TimeSpan.FromMinutes(110));
        _history.Add(new PrintJob("000410", "planter_bracket.gcode", "completed", now.AddDays(-1).AddHours(-3), now.AddDays(-1).AddHours(-1).AddMinutes(-10), 6_600, 6_720, FilamentPerJob, true));
        _history.Add(new PrintJob("000409", "planter_bracket.gcode", "completed", now.AddDays(-1).AddHours(-6), now.AddDays(-1).AddHours(-4).AddMinutes(-10), 6_600, 6_700, FilamentPerJob, true));
        _history.Add(new PrintJob("000408", "cable_clip_x10.gcode", "cancelled", now.AddDays(-3), now.AddDays(-3).AddMinutes(12), 700, 720, 900, true));
        _queue.Add(new QueuedJob("q-1", "planter_bracket.gcode", now.AddMinutes(-30)));
        _queue.Add(new QueuedJob("q-2", "planter_bracket.gcode", now.AddMinutes(-30)));
    }

    public IReadOnlyList<PrinterFile> Files { get; }

    public IReadOnlyList<QueuedJob> Queue
    {
        get
        {
            lock (_gate)
                return [.. _queue];
        }
    }

    public string QueueState { get; private set; } = "ready";

    public IReadOnlyList<PrintJob> History
    {
        get
        {
            lock (_gate)
                return [.. _history];
        }
    }

    public PrinterState State()
    {
        lock (_gate)
        {
            DateTimeOffset now = _time.GetUtcNow();
            if (_job is not { } job)
                return Idle(now, "standby", null);

            double progress = job.Progress(now);
            string state = job.Cancelled ? "cancelled" : progress >= 1 ? "complete" : job.PausedAt is null ? "printing" : "paused";
            bool running = state is "printing" or "paused";
            double printed = job.Printed(now).TotalSeconds;
            return new PrinterState(
                Name,
                "ready",
                "Printer is ready",
                state,
                job.File,
                Math.Round(progress, 4),
                printed,
                (now - job.Started).TotalSeconds,
                FilamentPerJob * progress,
                (int)Math.Round(Layers * progress),
                Layers,
                state == "printing" ? (job.Duration.TotalSeconds - printed) : null,
                null,
                running ? new HeaterState(239.6, 240, 0.42) : new HeaterState(38, 0, 0),
                running ? new HeaterState(80.1, 80, 0.31) : new HeaterState(31, 0, 0),
                now);
        }
    }

    public void Pause() => Update(job => job.PausedAt is null && job.Active(_time.GetUtcNow()) ? job with { PausedAt = _time.GetUtcNow() } : job);

    public void Resume() => Update(job => job.PausedAt is { } at ? job with { PausedAt = null, Paused = job.Paused + (_time.GetUtcNow() - at) } : job);

    public void Cancel() => Update(job => job.Active(_time.GetUtcNow()) ? job with { Cancelled = true, CancelledAt = _time.GetUtcNow() } : job);

    /// <summary>Starts <paramref name="file"/>; the current job, if any, goes to the history first.</summary>
    public void Start(string file)
    {
        lock (_gate)
        {
            Archive();
            _job = new Job(file, _time.GetUtcNow(), TimeSpan.FromMinutes(90));
        }
    }

    public void AddToQueue(IEnumerable<string> files)
    {
        lock (_gate)
            _queue.AddRange(files.Select(file => new QueuedJob($"q-{Guid.NewGuid():N}"[..10], file, _time.GetUtcNow())));
    }

    public void RemoveFromQueue(IReadOnlyCollection<string> jobIds)
    {
        lock (_gate)
            _queue.RemoveAll(job => jobIds.Contains(job.JobId));
    }

    public void ClearQueue()
    {
        lock (_gate)
            _queue.Clear();
    }

    public void JumpQueue(string jobId)
    {
        lock (_gate)
        {
            int index = _queue.FindIndex(job => job.JobId == jobId);
            if (index <= 0)
                return;

            QueuedJob job = _queue[index];
            _queue.RemoveAt(index);
            _queue.Insert(0, job);
        }
    }

    public void SetQueueState(string state) => QueueState = state;

    private void Update(Func<Job, Job> change)
    {
        lock (_gate)
        {
            if (_job is { } job)
                _job = change(job);
        }
    }

    private void Archive()
    {
        if (_job is not { } job)
            return;

        DateTimeOffset now = _time.GetUtcNow();
        string status = job.Cancelled ? "cancelled" : job.Progress(now) >= 1 ? "completed" : "interrupted";
        _history.Insert(0, new PrintJob($"{_nextJob++:000000}", job.File, status, job.Started, now, job.Printed(now).TotalSeconds, (now - job.Started).TotalSeconds, FilamentPerJob * job.Progress(now), true));
    }

    private PrinterState Idle(DateTimeOffset now, string state, string? file) =>
        new(Name, "ready", "Printer is ready", state, file, 0, 0, 0, 0, null, null, null, null, new HeaterState(24, 0, 0), new HeaterState(23, 0, 0), now);

    private sealed record Job(string File, DateTimeOffset Started, TimeSpan Duration)
    {
        public DateTimeOffset? PausedAt { get; init; }

        public TimeSpan Paused { get; init; }

        public bool Cancelled { get; init; }

        public DateTimeOffset? CancelledAt { get; init; }

        public TimeSpan Printed(DateTimeOffset now)
        {
            DateTimeOffset end = CancelledAt ?? PausedAt ?? now;
            TimeSpan printed = end - Started - Paused;
            return printed > Duration ? Duration : printed < TimeSpan.Zero ? TimeSpan.Zero : printed;
        }

        public double Progress(DateTimeOffset now) => Printed(now) / Duration;

        public bool Active(DateTimeOffset now) => !Cancelled && Progress(now) < 1;
    }
}
