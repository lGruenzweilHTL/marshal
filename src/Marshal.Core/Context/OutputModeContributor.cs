using Marshal.Abstractions.Context;
using Marshal.Abstractions.Execution;

namespace Marshal.Core.Context;

/// <summary>
/// Output rules derived from the turn's input mode.
/// </summary>
public sealed class OutputModeContributor : IContextContributor
{
    public const string VoiceRules = "Your reply will be spoken aloud. Use plain speakable sentences, no markdown, no lists, no links, no emoji. Keep it to one or two short sentences. Write numbers, units and times the way they are spoken.";

    public const string TextRules = "Your reply is shown as text. Markdown is allowed.";

    public int Order => 400;

    public Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(request.Context.InputMode == InputMode.Voice ? VoiceRules : TextRules);
}
