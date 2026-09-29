using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Tools;
using Marshal.Api.Tests.Http;
using Marshal.Integrations.Moonraker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Tests.Integrations;

public sealed class PrinterHostTests
{
    private static Dictionary<string, string?> Voron() => new()
    {
        ["printers:0:name"] = "voron",
        ["printers:0:url"] = "http://127.0.0.1:1",
        ["printers:0:apiKeySecret"] = "moonraker_voron",
        ["printers:0:macros:0"] = "PARK",
    };

    [Fact]
    public async Task Configured_BindsTheApiKeyAndRegistersToolsAndConnection()
    {
        using var factory = new MarshalApiFactory(Voron());
        await File.WriteAllTextAsync(Path.Combine(factory.SecretsDirectory, "moonraker_voron"), "moonraker-key\n", Ct);
        _ = factory.CreateClient();
        IServiceProvider services = factory.Services;

        Assert.Equal("moonraker-key", services.GetRequiredService<IOptions<PrinterOptions>>().Value.Printers[0].ApiKey);
        Assert.Contains(services.GetServices<IHostedService>(), service => service is MoonrakerStateCache);
        Assert.IsType<MoonrakerStateCache>(services.GetRequiredService<IPrinterStateCache>());
        ToolSnapshot tools = await services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);
        Assert.NotNull(tools.FindById(new ToolId("printer", "status")));
        Assert.NotNull(tools.FindById(new ToolId("printer", "run_macro")));
    }

    [Fact]
    public void Startup_LiteralApiKey_Fails()
    {
        Dictionary<string, string?> settings = Voron();
        settings.Remove("printers:0:apiKeySecret");
        settings["printers:0:apiKey"] = "moonraker-key";
        using var factory = new MarshalApiFactory(settings);

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("printers[0].apiKey", StartupErrors.Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_ApiKeySecretMissing_FailsNamingReference()
    {
        using var factory = new MarshalApiFactory(Voron());

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("printers[0].apiKeySecret (moonraker_voron)", StartupErrors.Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_InvalidPrinterName_Fails()
    {
        Dictionary<string, string?> settings = Voron();
        settings["printers:0:name"] = "../voron";
        using var factory = new MarshalApiFactory(settings);

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("printers[].name", StartupErrors.Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotConfigured_NoPrinterTools()
    {
        using var factory = new MarshalApiFactory();
        _ = factory.CreateClient();

        ToolSnapshot tools = await factory.Services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);

        Assert.DoesNotContain(tools.Tools, tool => tool.Id.Namespace == "printer");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
