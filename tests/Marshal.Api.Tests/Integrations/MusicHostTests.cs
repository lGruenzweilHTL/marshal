using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;
using Marshal.Api.Tests.Http;
using Microsoft.Extensions.DependencyInjection;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Api.Tests.Integrations;

public sealed class MusicHostTests
{
    private static readonly Dictionary<string, string?> _ha = new()
    {
        ["ha:url"] = "http://127.0.0.1:1",
        ["ha:tokenSecret"] = "ha_token",
    };

    private static readonly Dictionary<string, string?> _music = new()
    {
        ["music:speakers:kitchen"] = "media_player.kitchen_ma",
        ["music:speakers:kids_room"] = "media_player.kids_room_ma",
    };

    [Fact]
    public async Task HomeAssistantWithoutMusic_NoMusicTools()
    {
        using MarshalApiFactory factory = await StartAsync(_ha);

        ToolSnapshot tools = await factory.Services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);

        Assert.DoesNotContain(tools.Tools, tool => tool.Id.Namespace == "music");
    }

    [Fact]
    public async Task MusicWithoutHomeAssistant_NoMusicTools()
    {
        using MarshalApiFactory factory = await StartAsync(_music);

        ToolSnapshot tools = await factory.Services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);

        Assert.DoesNotContain(tools.Tools, tool => tool.Id.Namespace == "music");
    }

    [Fact]
    public async Task Configured_SafeMusicToolsWithMusicTaintOnSearchAndQueue()
    {
        using MarshalApiFactory factory = await StartAsync(new(_ha.Concat(_music)));

        ToolSnapshot tools = await factory.Services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);

        RegisteredTool[] music = [.. tools.Tools.Where(tool => tool.Id.Namespace == "music")];
        Assert.Equal(
            ["music.control", "music.group", "music.play", "music.queue", "music.search", "music.transfer", "music.ungroup"],
            music.Select(tool => tool.Id.ToString()).Order(StringComparer.Ordinal));
        Assert.All(music, tool => Assert.Equal(RiskTier.Safe, tool.Descriptor.DefaultTier));
        Assert.Equal(["music.queue", "music.search"], music.Where(tool => tool.Descriptor.UntrustedOutput).Select(tool => tool.Id.ToString()).Order(StringComparer.Ordinal));
        Assert.All(music.Where(tool => tool.Descriptor.UntrustedOutput), tool => Assert.Equal("music", tool.Descriptor.TaintKind));
    }

    [Fact]
    public async Task SatelliteDenyTools_HidesMusicToolsInThatRoomOnly()
    {
        using MarshalApiFactory factory = await StartAsync(new(_ha.Concat(_music)), """
            satellites:
              assist_satellite.kitchen:
                speaker: kitchen
              assist_satellite.kids_room:
                speaker: kids_room
                denyTools: ["music.*"]
            """);
        await factory.Services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);
        IToolVisibilityFilter visibility = factory.Services.GetRequiredService<IToolVisibilityFilter>();

        IExecutionContextAccessor accessor = factory.Services.GetRequiredService<IExecutionContextAccessor>();

        IReadOnlyList<RegisteredTool> kids = await VisibleAsync(visibility, accessor, Voice(satelliteId: "assist_satellite.kids_room", areaId: "kids_room"));
        IReadOnlyList<RegisteredTool> kitchen = await VisibleAsync(visibility, accessor, Voice());

        Assert.DoesNotContain(kids, tool => tool.Id.Namespace == "music");
        Assert.Contains(kitchen, tool => tool.Id == new ToolId("music", "play"));
    }

    private static async Task<IReadOnlyList<RegisteredTool>> VisibleAsync(IToolVisibilityFilter visibility, IExecutionContextAccessor accessor, MarshalExecutionContext context)
    {
        using (accessor.Use(context))
            return await visibility.GetVisibleToolsAsync(context, Ct);
    }

    private static async Task<MarshalApiFactory> StartAsync(Dictionary<string, string?> settings, string? satellites = null)
    {
        var factory = new MarshalApiFactory(settings);
        await File.WriteAllTextAsync(Path.Combine(factory.SecretsDirectory, "ha_token"), "token\n", Ct);
        if (satellites is not null)
            await File.WriteAllTextAsync(Path.Combine(factory.ConfigDirectory, "satellites.yaml"), satellites, Ct);
        _ = factory.CreateClient();
        return factory;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
