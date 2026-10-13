using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Integrations.HomeAssistant;

namespace Marshal.Demo.HomeAssistant;

/// <summary>Home Assistant's REST side, simulated: intents, service calls and the time zone. Never opens a connection.</summary>
public sealed class DemoHomeAssistantClient(DemoWorld world) : IHomeAssistantClient
{
    public Task<Result<JsonElement>> HandleIntentAsync(HaIntentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<Result<JsonElement>>(DemoIntents.Handle(world.Home, world.Time.GetUtcNow(), request.Name, request.Data, request.DeviceId));

    public Task<Result<string>> GetTimeZoneAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>(world.Home.Snapshot.TimeZone ?? "UTC");

    public Task<Result<JsonElement>> GetAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult<Result<JsonElement>>(new Error("ha.service_not_supported", $"The demo does not simulate GET {path}.", ErrorType.Validation));

    public Task<Result<JsonElement>> CallServiceAsync(string domain, string service, JsonObject data, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<HaState>> called = DemoServiceCalls.Call(world.Home, domain, service, data);
        return Task.FromResult(called.IsSuccess ? Result.Success(DemoServiceCalls.Response(called.Value)) : called.Error);
    }
}
