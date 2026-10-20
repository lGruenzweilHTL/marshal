using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Printers;
using Marshal.Api.Tests.Auth;
using Marshal.Api.Tests.Http;
using Marshal.Demo;
using Marshal.Integrations.HomeAssistant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Integrations;

public sealed class DemoHostTests
{
    private const string Turns = "/api/v1/conversation/turns";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Startup_DemoOnWithoutModel_FailsLikeWithoutDemo()
    {
        using var factory = new MarshalApiFactory(new Dictionary<string, string?> { ["demo:enabled"] = "true", ["models:fast:model"] = "" });

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("models.fast.model is required.", StartupErrors.Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyAModel_ToolsOfAllFiveIntegrations_LightTurnedOnAfterConfirmation()
    {
        await using MarshalApiFactory factory = await DemoAsync();
        factory.Model
            .ThenToolCall("ha_intent__HassTurnOn", new { name = "Kitchen light" })
            .ThenText("Turn on the kitchen light?")
            .ThenText("The kitchen light is on.");
        (HttpClient browser, _) = await Browser.SignInAsync(factory, "user");

        string sessionId = (await FinalAsync(await browser.PostAsJsonAsync(Turns, new { text = "turn on the kitchen light" }, Ct))).GetProperty("conversationId").GetString()!;
        Assert.Equal("off", State(factory, "light.kitchen"));
        await FinalAsync(await browser.PostAsJsonAsync(Turns, new { conversationId = sessionId, text = "confirm" }, Ct));

        IReadOnlyList<string> tools = factory.Model.Requests[0].ToolNames;
        foreach (string tool in new[] { "ha_intent__HassTurnOn", "ha_homeassistant__GetLiveContext", "calendar_list_events", "printer_status", "printer_cancel", "mail_search", "rss_get_unread", "notes_read" })
            Assert.Contains(tool, tools);
        Assert.Equal("on", State(factory, "light.kitchen"));
    }

    [Fact]
    public async Task CancelThePrint_FrozenUntilConfirmed()
    {
        await using MarshalApiFactory factory = await DemoAsync();
        factory.Model.ThenToolCall("printer_cancel", new { printer = "voron" }).ThenText("Cancel the print on voron?").ThenText("Cancelled.");
        (HttpClient browser, _) = await Browser.SignInAsync(factory, "user");
        IPrinterStateCache printers = factory.Services.GetRequiredService<IPrinterStateCache>();

        string sessionId = (await FinalAsync(await browser.PostAsJsonAsync(Turns, new { text = "cancel the print" }, Ct))).GetProperty("conversationId").GetString()!;
        string before = printers.Get("voron").Value.PrintState;
        await FinalAsync(await browser.PostAsJsonAsync(Turns, new { conversationId = sessionId, text = "confirm" }, Ct));

        Assert.Equal("printing", before);
        Assert.Equal("cancelled", printers.Get("voron").Value.PrintState);
    }

    [Fact]
    public async Task Me_DemoOn_CarriesTheDemoFlags_SystemListsTheSimulatedIntegrations()
    {
        await using MarshalApiFactory factory = await DemoAsync(showBadge: false);
        (HttpClient browser, _) = await Browser.SignInAsync(factory, "user");

        JsonElement demo = (await browser.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct)).GetProperty("demo");
        JsonElement system = await browser.GetFromJsonAsync<JsonElement>("/api/v1/system", Ct);

        Assert.Equal((true, false), (demo.GetProperty("enabled").GetBoolean(), demo.GetProperty("showBadge").GetBoolean()));
        Assert.Equal(["home_assistant", "printers", "mail", "rss", "notes", "web"], system.GetProperty("simulatedIntegrations").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task Me_DemoOff_DemoDisabledAndNothingSimulated()
    {
        await using MarshalApiFactory factory = await AuthFactory.CreateAsync(AuthFactory.Local());
        (HttpClient browser, _) = await Browser.SignInAsync(factory, "user");

        JsonElement demo = (await browser.GetFromJsonAsync<JsonElement>("/api/v1/me", Ct)).GetProperty("demo");
        JsonElement system = await browser.GetFromJsonAsync<JsonElement>("/api/v1/system", Ct);

        Assert.Equal((false, true), (demo.GetProperty("enabled").GetBoolean(), demo.GetProperty("showBadge").GetBoolean()));
        Assert.Empty(system.GetProperty("simulatedIntegrations").EnumerateArray());
    }

    private static async Task<MarshalApiFactory> DemoAsync(bool showBadge = true)
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        settings["demo:enabled"] = "true";
        settings["demo:showBadge"] = showBadge ? "true" : "false";
        // The factory's settings reach the host configuration only after the services are registered, so demo mode is
        // registered again here, at the same point in the composition root (after every integration).
        IConfiguration demo = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["demo:enabled"] = "true" }).Build();
        var factory = new MarshalApiFactory(settings) { TestServices = services => services.AddMarshalDemo(demo) };
        await AuthFactory.WriteClientSecretAsync(factory);
        return factory;
    }

    private static string? State(MarshalApiFactory factory, string entityId) =>
        factory.Services.GetRequiredService<IHomeAssistantCache>().Current?.States.GetValueOrDefault(entityId)?.State;

    private static async Task<JsonElement> FinalAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        (string name, JsonElement data) = Sse.Parse(await response.Content.ReadAsStringAsync(Ct))[^1];
        Assert.Equal("final", name);
        return data;
    }
}
