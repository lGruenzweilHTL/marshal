namespace Marshal.Demo;

/// <summary>
/// The simulated world behind every demo integration: the home, the printer, the mailbox, the feeds and the notes.
/// One instance per process; times are relative to its creation, so a restart starts the story again.
/// </summary>
public sealed class DemoWorld
{
    public DemoWorld(TimeProvider time)
    {
        Time = time;
        Started = time.GetUtcNow();
        Home = new DemoHome(time);
        Printer = new DemoPrinter(time);
        Mail = new DemoMailbox(Started);
        Feeds = new DemoFeeds(Started);
        Notes = new DemoNotes(Started);
    }

    public TimeProvider Time { get; }

    /// <summary>When the world was created; the demo content's times count back from here.</summary>
    public DateTimeOffset Started { get; }

    public DemoHome Home { get; }

    public DemoPrinter Printer { get; }

    public DemoMailbox Mail { get; }

    public DemoFeeds Feeds { get; }

    public DemoNotes Notes { get; }
}
