namespace Marshal.Demo;

/// <summary>
/// The demo maintenance item: the demo printer's nozzle, every 300 print hours with a warning 20 hours before. The
/// host adds it to <c>maintenance.items</c> while the printer is simulated and no item is configured; the seeder
/// starts it at <see cref="SeededUsage"/>, so it shows as due soon.
/// </summary>
public static class DemoMaintenance
{
    public const string ItemId = "voron_nozzle";

    public const string Name = "Voron nozzle";

    public const decimal Every = 300m;

    public const decimal WarnBefore = 20m;

    /// <summary>Print hours since the nozzle was last changed, at seed time.</summary>
    public const decimal SeededUsage = 286m;

    /// <summary>Days since the seeded last change.</summary>
    public const int SeededDaysSinceDone = 48;
}
