using System.Text.Json;
using Marshal.Abstractions.Events;
using Marshal.Integrations.HomeAssistant;
using Microsoft.Extensions.Hosting;

namespace Marshal.Demo.HomeAssistant;

/// <summary>
/// Publishes the simulated home's state changes as Home Assistant <c>state_changed</c> events (source <c>ha</c>,
/// same payload as the real event source), so triggers and conditional deliveries react to them.
/// </summary>
public sealed class DemoHomeAssistantEventSource : IEventSource, IHostedService, IDisposable
{
    private readonly DemoWorld _world;
    private readonly IEventPublisher _publisher;

    public DemoHomeAssistantEventSource(DemoWorld world, IEventPublisher publisher)
    {
        _world = world;
        _publisher = publisher;
        world.Home.Changed += Publish;
    }

    public string Source => HomeAssistantEventSource.Name;

    public bool IsConnected => true;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _world.Home.Changed -= Publish;

    private void Publish(DemoStateChange change)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new { from = change.From, to = change.To, attributes = change.Attributes });
        _publisher.Publish(new MarshalEvent(HomeAssistantEventSource.Name, HomeAssistantEventSource.StateChanged, change.EntityId, payload, null, _world.Time.GetUtcNow()));
    }
}
