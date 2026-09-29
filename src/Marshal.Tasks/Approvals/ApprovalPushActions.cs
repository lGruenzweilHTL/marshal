using System.Text.Json;
using Marshal.Abstractions.Events;
using Microsoft.Extensions.Hosting;

namespace Marshal.Tasks.Approvals;

/// <summary>
/// Hands the companion app's push actions (HA <c>mobile_app_notification_action</c> events) to the approval queue,
/// which accepts only a valid, unexpired nonce.
/// </summary>
public sealed class ApprovalPushActions(ApprovalQueue approvals, IEventSubscriber events) : IHostedService, IDisposable
{
    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = events.Subscribe(async (@event, ct) =>
        {
            if (@event is { Source: "ha", Type: "mobile_app_notification_action" }
                && @event.Payload.ValueKind == JsonValueKind.Object
                && @event.Payload.TryGetProperty("action", out JsonElement action)
                && action.ValueKind == JsonValueKind.String)
            {
                await approvals.HandlePushActionAsync(action.GetString()!, ct);
            }
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}
