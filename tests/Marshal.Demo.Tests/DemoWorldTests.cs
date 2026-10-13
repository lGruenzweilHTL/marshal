using Microsoft.Extensions.Time.Testing;

namespace Marshal.Demo.Tests;

public sealed class DemoWorldTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Load_HomePrinterMailFeedsAndNotesFromResources()
    {
        var world = new DemoWorld(_time);

        Assert.Equal("off", world.Home.Snapshot.States["light.kitchen"].State);
        Assert.Equal("kitchen", world.Home.Snapshot.Entities["light.kitchen"].AreaId);
        Assert.Contains("office", world.Home.Snapshot.Areas.Keys);
        Assert.Equal("upper_floor", world.Home.Snapshot.Areas["office"].FloorId);
        Assert.Equal("printing", world.Printer.State().PrintState);
        Assert.Contains(world.Mail.Messages, message => message.Subject.Contains("Invoice", StringComparison.Ordinal));
        Assert.Equal(_time.GetUtcNow().AddMinutes(-95), world.Mail.Messages.Single(message => message.Id == "m1").Date);
        Assert.Contains(world.Feeds.Articles, article => article.Title == "Klipper 0.13 released");
        Assert.Contains("printing/voron-maintenance.md", world.Notes.Files.Keys);
        Assert.StartsWith("# Voron maintenance", world.Notes.Files["printing/voron-maintenance.md"], StringComparison.Ordinal);
    }

    [Fact]
    public void Printer_ProgressAdvancesAndFinishes()
    {
        var world = new DemoWorld(_time);
        double first = world.Printer.State().Progress;

        _time.Advance(TimeSpan.FromMinutes(10));
        double later = world.Printer.State().Progress;
        _time.Advance(TimeSpan.FromHours(5));

        Assert.InRange(first, 0.1, 0.9);
        Assert.True(later > first);
        Assert.Equal(("complete", 1.0), (world.Printer.State().PrintState, world.Printer.State().Progress));
        Assert.Equal(world.Printer.State().TotalLayers, world.Printer.State().CurrentLayer);
    }

    [Fact]
    public void Printer_PausedProgressStandsStill()
    {
        var world = new DemoWorld(_time);
        world.Printer.Pause();
        double paused = world.Printer.State().Progress;

        _time.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(("paused", paused), (world.Printer.State().PrintState, world.Printer.State().Progress));
        world.Printer.Resume();
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(world.Printer.State().Progress > paused);
    }

    [Fact]
    public void Home_SetState_ChangesSnapshotAndRaisesEvent()
    {
        var world = new DemoWorld(_time);
        var changes = new List<DemoStateChange>();
        world.Home.Changed += changes.Add;
        _time.Advance(TimeSpan.FromMinutes(1));

        world.Home.Set("light.kitchen", "on", new Dictionary<string, object?> { ["brightness"] = 255 });

        Assert.Equal("on", world.Home.Snapshot.States["light.kitchen"].State);
        Assert.Equal(255, world.Home.Snapshot.States["light.kitchen"].Attributes.GetProperty("brightness").GetInt32());
        Assert.Equal("Kitchen light", world.Home.Snapshot.States["light.kitchen"].FriendlyName);
        Assert.Equal(_time.GetUtcNow(), world.Home.Snapshot.States["light.kitchen"].LastUpdated);
        DemoStateChange change = Assert.Single(changes);
        Assert.Equal(("light.kitchen", "off", "on"), (change.EntityId, change.From, change.To));
    }

    [Fact]
    public void Home_SetSameState_NoEvent()
    {
        var world = new DemoWorld(_time);
        var changes = new List<DemoStateChange>();
        world.Home.Changed += changes.Add;

        world.Home.Set("light.kitchen", "off", null);

        Assert.Empty(changes);
    }
}
