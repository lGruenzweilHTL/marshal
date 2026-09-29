using System.Threading.Channels;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Marshal.Memory.Projects;
using Microsoft.Extensions.Options;

namespace Marshal.Memory.Consolidation;

/// <summary>A closed session waiting for summarization.</summary>
/// <param name="Owner">Owner of the session.</param>
/// <param name="Session">The closed session.</param>
public sealed record ClosedSession(OwnerId Owner, SessionId Session);

/// <summary>
/// Reacts to closed sessions: forgets their project activation state and queues them for the
/// <see cref="SessionSummaryWorker"/> (an in-process queue, not the scheduler), so closing never waits for a model call.
/// </summary>
public sealed class SessionSummaryInbox(ProjectSessions projectSessions, IOptions<MemoryOptions> options) : ISessionCloseListener
{
    private readonly Channel<ClosedSession> _queue = Channel.CreateUnbounded<ClosedSession>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<ClosedSession> Reader => _queue.Reader;

    public Task SessionClosedAsync(OwnerId owner, SessionId session, CancellationToken cancellationToken)
    {
        projectSessions.Remove(session);
        if (options.Value.Summarizer.Enabled)
            _queue.Writer.TryWrite(new ClosedSession(owner, session));

        return Task.CompletedTask;
    }
}
