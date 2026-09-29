using System.Text.Json.Nodes;

namespace Marshal.Integrations.Moonraker.Tests;

public sealed class FixtureTests
{
    [Fact]
    public async Task Session_HasTheSubscribedObjectsAndEverySequence()
    {
        await using FakeMoonraker fake = await FakeMoonraker.StartAsync();
        JsonObject fixture = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "moonraker-session.json"), TestContext.Current.CancellationToken))!.AsObject();

        JsonObject status = fake.Results["printer.objects.subscribe"]!["status"]!.AsObject();
        Assert.Equal(["print_stats", "virtual_sdcard", "extruder", "heater_bed", "webhooks", "display_status"], status.Select(entry => entry.Key));
        Assert.Equal(["print_start", "pause", "resume", "progress", "complete", "cancel", "error", "temperatures", "shutdown", "ready"], fixture["sequences"]!.AsObject().Select(entry => entry.Key));
    }
}
