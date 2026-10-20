using System.Security.Claims;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Music;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Marshal.Api.Widgets;
using Marshal.Core.Agent;
using Marshal.Integrations.HomeAssistant.Music;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Music;

/// <summary>
/// The music widget's endpoints: the speaker's queue, and playback controls that run <c>music.control</c> through the
/// guard as a run with trigger <c>widget:music</c>.
/// </summary>
internal static class MusicEndpoints
{
    private const string ControlTool = "music_control";

    public static RouteGroupBuilder MapMusicEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder music = group.MapGroup("/music").WithTags("Music").RequireAuthorization(Policies.Human);
        music.MapGet("/{speaker}", GetAsync).WithName("GetMusicQueue")
            .WithSummary("The speaker's queue as the music widget shows it: state, current and next item, item count, shuffle and repeat.");
        music.MapPost("/{speaker}/control", ControlAsync).WithName("ControlMusic")
            .WithSummary("Runs a playback control (pause, resume, next, previous, stop, volume) through the guard, recorded as a run with trigger widget:music.");
        return group;
    }

    private static async Task<Results<Ok<JsonElement>, ProblemHttpResult>> GetAsync(string speaker, IServiceProvider services, CancellationToken ct)
    {
        if (Find(services, speaker) is not { } found)
            return UnknownSpeaker(speaker).ToProblem();

        Result<MusicQueueState> queue = await services.GetRequiredService<IMusicPlayer>().GetQueueAsync(found, ct);
        return queue.IsSuccess ? TypedResults.Ok(MusicWidgetData.Data(found.Name, queue.Value)) : queue.Error.ToProblem();
    }

    private static async Task<Results<Ok<MusicControlResult>, ProblemHttpResult>> ControlAsync(
        string speaker, MusicControlRequest request, ClaimsPrincipal user, IServiceProvider services, WidgetRuns widgets, IToolCallPipeline pipeline, CancellationToken ct)
    {
        if (Find(services, speaker) is not { } found)
            return UnknownSpeaker(speaker).ToProblem();

        JsonElement arguments = JsonSerializer.SerializeToElement(new { action = request.Action, speaker = found.Name, volume = request.Volume }, JsonSerializerOptions.Web);
        Result<MusicControlResult> result = await widgets.RunAsync(user, MusicWidgetData.WidgetKind, context => CallAsync(context, arguments, pipeline, ct), ct);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<Result<MusicControlResult>> CallAsync(MarshalExecutionContext context, JsonElement arguments, IToolCallPipeline pipeline, CancellationToken ct)
    {
        ToolCallOutcome outcome = await pipeline.CallAsync(new ToolCallRequest(ControlTool, $"widget_{context.RunId!.Value.Value:N}", arguments, context, new HashSet<string>(StringComparer.Ordinal) { ControlTool }), ct);
        if (outcome.Frozen is not null)
            return new MusicControlResult("approval_required");
        if (outcome.Verdict?.Denial is { } denial)
            return denial;

        return outcome.Result is { IsSuccess: true } ? new MusicControlResult("done") : outcome.Result!.Error;
    }

    private static MusicSpeaker? Find(IServiceProvider services, string speaker) =>
        services.GetService<ISpeakerDirectory>()?.ByName(speaker) is { } found && services.GetService<IMusicPlayer>() is not null ? found : null;

    private static Error UnknownSpeaker(string speaker) => new("music.unknown_speaker", $"There is no speaker '{speaker}'.", ErrorType.NotFound);
}

/// <param name="Action">pause, resume, next, previous, stop or volume.</param>
/// <param name="Volume">0–100, for volume.</param>
public sealed record MusicControlRequest(string Action, int? Volume = null);

/// <param name="Status"><c>done</c>, or <c>approval_required</c> when a risk rule made the control confirm tier and it waits in the approval queue.</param>
public sealed record MusicControlResult(string Status);
