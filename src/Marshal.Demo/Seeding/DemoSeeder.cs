using System.Text.Json;
using System.Text.Json.Serialization;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Data;
using Marshal.Data.Conversations;
using Marshal.Data.Demo;
using Marshal.Data.Memory;
using Marshal.Data.Notifications;
using Marshal.Data.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Demo.Seeding;

/// <summary>Seeds the demo data for an owner, once.</summary>
public interface IDemoSeeder
{
    Task<bool> IsSeededAsync(OwnerId owner, CancellationToken cancellationToken);

    /// <summary>True when the data was ever seeded for the owner (also after a reset), from the audit log.</summary>
    Task<bool> WasSeededBeforeAsync(OwnerId owner, CancellationToken cancellationToken);

    /// <summary>Seeds and returns the number of recorded rows; <c>demo.already_seeded</c> when seeded rows exist.</summary>
    Task<Result<int>> SeedAsync(OwnerId owner, CancellationToken cancellationToken);
}

/// <summary>
/// Which optional parts are seeded. Background results and reasoning steps depend on the chat history and reasoning
/// features; both exist, so <see cref="All"/> is what the host uses.
/// </summary>
public sealed record DemoSeedParts(bool BackgroundResults, bool Reasoning)
{
    public static DemoSeedParts All { get; } = new(BackgroundResults: true, Reasoning: true);
}

/// <summary>
/// Writes the demo history: conversations (web and voice from the kitchen and office satellites, one turn moved to the
/// background), runs with steps, the morning task with two past results, notifications, one pending approval and
/// memory chunks with edges. Runs, sessions, approvals and memory go through the app's own stores and recorder,
/// driven by a clock set to each item's time, so payloads are valid by construction; tasks and notifications are
/// written as rows, because their stores cannot backdate results. Every top-level row is recorded in
/// <c>demo_seed</c>; times count back from the seed time.
/// </summary>
public sealed class DemoSeeder(
    IDbContextFactory<MarshalDbContext> contexts,
    IExecutionContextAccessor executionContext,
    IMemoryStore memory,
    IUserProfileProvider profiles,
    IAuditLog audit,
    IOptions<RecordingOptions> recording,
    TimeProvider time,
    DemoSeedParts parts) : IDemoSeeder
{
    public const string Kitchen = "assist_satellite.kitchen";
    public const string Office = "assist_satellite.office";

    /// <summary>Audit action of a seed.</summary>
    public const string SeedAction = "demo.seed";

    private static readonly JsonSerializerOptions _taskJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public async Task<bool> IsSeededAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        using IDisposable scope = Use(owner);
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.DemoSeeds.AnyAsync(cancellationToken);
    }

    public async Task<bool> WasSeededBeforeAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        using IDisposable scope = Use(owner);
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.DemoSeeds.AnyAsync(cancellationToken) || await db.AuditEvents.AnyAsync(entry => entry.Action == SeedAction, cancellationToken);
    }

    public async Task<Result<int>> SeedAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        if (await IsSeededAsync(owner, cancellationToken))
            return new Error("demo.already_seeded", "The demo data is already seeded; run marshal demo reset first.", ErrorType.Conflict);

        using IDisposable scope = Use(owner);
        DateTimeOffset now = time.GetUtcNow();
        Result<UserProfile> profile = await profiles.GetAsync(owner, cancellationToken);
        TimeZoneInfo zone = profile.IsSuccess && TimeZoneInfo.TryFindSystemTimeZoneById(profile.Value.TimeZone, out TimeZoneInfo? found) ? found : TimeZoneInfo.Utc;
        var seed = new Seed(owner, now, zone, new SeedClock(now));
        try
        {
            await ConversationsAsync(seed, cancellationToken);
            await MorningTaskAsync(seed, cancellationToken);
            await ApprovalAsync(seed, cancellationToken);
            await NotificationsAsync(seed, cancellationToken);
            await MemoryAsync(seed, cancellationToken);
        }
        finally
        {
            // Recorded even when a part failed, so a reset removes what was written.
            await using MarshalDbContext db = await contexts.CreateDbContextAsync(CancellationToken.None);
            db.DemoSeeds.AddRange(seed.Rows.Select(row => new DemoSeed { Id = Guid.CreateVersion7(), OwnerId = owner, Entity = row.Entity, RowId = row.Id, SeededAt = now }));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        await audit.RecordAsync(new AuditEntry(AuditActorKind.System, "demo", SeedAction, Details: new { rows = seed.Rows.Count }), cancellationToken);
        return seed.Rows.Count;
    }

    private async Task ConversationsAsync(Seed seed, CancellationToken ct)
    {
        // Yesterday evening on the web: notes, then a light with reasoning.
        SessionId web = await SessionAsync(seed, SessionSource.Web, seed.Day(-1, 19, 10), null, ct);
        await TurnAsync(seed, Context(seed, web, InputMode.Text), seed.Day(-1, 19, 10), "Which filament did I pick for the planter brackets?",
        [
            Model("Searching the notes for the planter bracket project.", tokensIn: 2_840, tokensOut: 42),
            Tool("notes", "search", """{"query":"planter brackets filament"}""", "projects/planter-brackets.md › Material: PETG Jet Black, 5 walls, 40 % gyroid infill.", RiskTier.Safe, GuardDecision.Safe, 180),
            Model(null, tokensIn: 3_120, tokensOut: 61),
        ], "PETG Jet Black, printed with five walls and 40 % gyroid infill, according to your project note.", ct);
        await TurnAsync(seed, Context(seed, web, InputMode.Text), seed.Day(-1, 19, 14), "Dim the living room light to 30 percent.",
        [
            Model("The user wants the living room light at 30 percent. HassLightSet with area living room and brightness 30.", tokensIn: 3_300, tokensOut: 38),
            Tool("ha", "intent__HassLightSet", """{"area":"Living room","brightness":30}""", "Set the brightness", RiskTier.Safe, GuardDecision.Safe, 240),
            Model(null, tokensIn: 3_380, tokensOut: 12),
        ], "Done, the living room light is at 30 percent.", ct);
        await CloseAsync(seed, web, seed.Day(-1, 19, 15), ct);

        // Voice: the print from the office yesterday afternoon, the calendar from the kitchen this morning.
        SessionId office = await SessionAsync(seed, SessionSource.Voice, seed.Day(-1, 14, 20), Office, ct);
        await TurnAsync(seed, Context(seed, office, InputMode.Voice, Office, "office"), seed.Day(-1, 14, 20), "How long until the print is done?",
        [
            Model(null, tokensIn: 2_450, tokensOut: 20),
            Tool("printer", "status", """{"printer":"voron"}""", "voron: printing planter-bracket-v3.gcode, 41 %, layer 110 of 268, about 2 h 35 min left.", RiskTier.Safe, GuardDecision.Safe, 90),
            Model(null, tokensIn: 2_610, tokensOut: 24),
        ], "The Voron is at 41 percent, about two and a half hours to go.", ct);
        await CloseAsync(seed, office, seed.Day(-1, 14, 21), ct);

        SessionId kitchen = await SessionAsync(seed, SessionSource.Voice, seed.Day(0, 7, 5), Kitchen, ct);
        await TurnAsync(seed, Context(seed, kitchen, InputMode.Voice, Kitchen, "kitchen"), seed.Day(0, 7, 5), "What's on my calendar today?",
        [
            Model(null, tokensIn: 2_390, tokensOut: 18),
            Tool("calendar", "list_events", """{"day":"today"}""", "09:30-10:00 Dentist; 18:00-19:30 Climbing with Jana", RiskTier.Safe, GuardDecision.Safe, 120),
            Model(null, tokensIn: 2_520, tokensOut: 30),
        ], "You have the dentist at half past nine and climbing with Jana at six.", ct);
        await TurnAsync(seed, Context(seed, kitchen, InputMode.Voice, Kitchen, "kitchen"), seed.Day(0, 7, 6), "Set a timer for 8 minutes.",
        [
            Model(null, tokensIn: 2_560, tokensOut: 14),
            Tool("timer", "start", """{"duration":"PT8M"}""", "Timer started for 8 minutes on the kitchen satellite.", RiskTier.Safe, GuardDecision.Safe, 60),
            Model(null, tokensIn: 2_610, tokensOut: 9),
        ], "Timer set for eight minutes.", ct);
        await CloseAsync(seed, kitchen, seed.Day(0, 7, 7), ct);

        if (!parts.BackgroundResults)
            return;

        // Two hours ago on the web: a mail summary moved to the background, with its result.
        DateTimeOffset asked = seed.Now.AddHours(-2);
        SessionId background = await SessionAsync(seed, SessionSource.Web, asked, null, ct);
        await TurnAsync(seed, Context(seed, background, InputMode.Text), asked, "Go through my unread mail and tell me what needs an answer.",
        [
            Model("Several messages to read; this takes a while, so move it to the background.", tokensIn: 2_900, tokensOut: 35),
            Tool("agent", "move_to_background", """{"statusMessage":"I'll go through your mail and report back."}""", "Moved to the background.", RiskTier.Safe, GuardDecision.Safe, 15),
        ], "I'll go through your mail and report back.", ct);
        var task = TaskId.New();
        await AddTaskAsync(seed, new AgentTask
        {
            Id = task,
            OwnerId = seed.Owner,
            CreatedBy = TaskCreator.User,
            Kind = TaskKind.Agent,
            Goal = "Go through my unread mail and tell me what needs an answer.",
            Template = "default",
            OriginSessionId = background,
            TriggerKind = TaskTriggerKind.Now,
            TriggerSpec = "",
            Status = AgentTaskStatus.Done,
            Notes = "Continues a turn moved to the background (move_to_background).",
            Deliveries = Json(new[] { new TaskDelivery(DeliveryChannel.Inbox) }),
            ResultSpeech = "Two of your unread mails need an answer.",
            ResultBody = "Two unread mails need an answer:\n\n- **Jana Berger** asks whether Saturday at 10 still works for the bouldering trip.\n- **Prusament** sent invoice 2026-4471 (74.70 EUR), payable within 14 days.\n\nThe Printables notification needs nothing.",
            ResultAt = asked.AddMinutes(3),
            CreatedAt = asked,
            UpdatedAt = asked.AddMinutes(3),
        }, ct);
        await CloseAsync(seed, background, asked.AddMinutes(4), ct);
    }

    private async Task MorningTaskAsync(Seed seed, CancellationToken ct)
    {
        var morning = TaskId.New();
        DateTimeOffset created = seed.Day(-6, 21, 40);
        DateTimeOffset latest = seed.Day(0, 6, 0);
        DateTimeOffset next = latest.AddDays(1);
        seed.Morning = morning;
        await AddTaskAsync(seed, new AgentTask
        {
            Id = morning,
            OwnerId = seed.Owner,
            CreatedBy = TaskCreator.User,
            Kind = TaskKind.Agent,
            Name = "morning",
            Goal = "Prepare today's morning briefing: appointments, mail that needs attention, the RSS digest, the weather and the house.",
            Template = "default",
            TriggerKind = TaskTriggerKind.Cron,
            TriggerSpec = "0 6 * * *",
            Status = AgentTaskStatus.Pending,
            NextRunAt = next,
            Deliveries = Json(new[] { new TaskDelivery(DeliveryChannel.Inbox) }),
            ResultSpeech = "Good morning. Dentist at 9:30, climbing at 6. Two mails need an answer, and the print finished overnight.",
            ResultBody = "## Today\n\n- 09:30 Dentist\n- 18:00 Climbing with Jana\n\n## Mail\n\n- Jana asks about Saturday.\n- Prusament invoice 2026-4471, 74.70 EUR.\n\n## Reading\n\n- Klipper 0.13 released\n- Drying PETG: what actually works\n\n## House\n\nAll windows closed, 21.5 °C inside. The Voron finished planter-bracket-v3 at 02:14.",
            ResultAt = latest.AddMinutes(1),
            CreatedAt = created,
            UpdatedAt = latest.AddMinutes(1),
        }, ct);

        foreach (DateTimeOffset at in new[] { latest.AddDays(-1), latest })
        {
            MarshalExecutionContext context = new() { OwnerId = seed.Owner, Source = SessionSource.Task, TaskId = morning, IsInteractive = false, Template = "default" };
            await TurnAsync(seed, context, at, null,
            [
                Model(null, tokensIn: 4_100, tokensOut: 40),
                Tool("calendar", "list_events", """{"day":"today"}""", "09:30-10:00 Dentist; 18:00-19:30 Climbing with Jana", RiskTier.Safe, GuardDecision.Safe, 110),
                Tool("mail", "search", """{"unread":true}""", "3 unread messages.", RiskTier.Safe, GuardDecision.Safe, 420),
                Tool("rss", "get_unread", """{"limit":5}""", "3 unread articles.", RiskTier.Safe, GuardDecision.Safe, 260),
                Model(null, tokensIn: 6_800, tokensOut: 310),
            ], "Good morning. Dentist at 9:30, climbing at 6. Two mails need an answer.", ct, trigger: "task");
        }
    }

    private async Task ApprovalAsync(Seed seed, CancellationToken ct)
    {
        var babysit = TaskId.New();
        DateTimeOffset asked = seed.Now.AddMinutes(-25);
        await AddTaskAsync(seed, new AgentTask
        {
            Id = babysit,
            OwnerId = seed.Owner,
            CreatedBy = TaskCreator.User,
            Kind = TaskKind.Agent,
            Name = "watch-print",
            Goal = "Watch the print on voron and cancel it if the first layers look like spaghetti.",
            Template = "default",
            TriggerKind = TaskTriggerKind.Now,
            TriggerSpec = "",
            Status = AgentTaskStatus.Waiting,
            StatusReason = "Waiting for approval to cancel the print.",
            Deliveries = Json(new[] { new TaskDelivery(DeliveryChannel.Push) }),
            CreatedAt = asked.AddMinutes(-40),
            UpdatedAt = asked,
        }, ct);

        seed.Clock.Now = asked;
        using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = seed.Owner, Source = SessionSource.Task, TaskId = babysit, IsInteractive = false });
        PendingActionRecord frozen = await new EfPendingActionStore(contexts, executionContext, seed.Clock).FreezeAsync(new FreezeRequest(
            null,
            null,
            new ToolId("printer", "cancel"),
            """{"printer":"voron"}""",
            "Cancel the print on voron",
            [new ConfirmationReason(ConfirmationReasonCode.Rule, "Cancelling a print cannot be undone.")],
            "en",
            seed.Now.AddDays(30),
            TaskId: babysit,
            Queued: true), ct);
        seed.Add("pending_actions", frozen.Id.ToString());
    }

    private async Task NotificationsAsync(Seed seed, CancellationToken ct)
    {
        (string Source, NotificationPriority Priority, string Content, NotificationOutcome Outcome, string Channel, DateTimeOffset At)[] notifications =
        [
            ("system:babysitter", NotificationPriority.Normal, "The print on voron finished: planter-bracket-v3.gcode after 5 h 12 min.", NotificationOutcome.Delivered, "push", seed.Day(0, 2, 14)),
            ($"task:{seed.Morning}", NotificationPriority.Normal, "Your morning briefing is ready: two appointments, two mails that need an answer, three new articles.", NotificationOutcome.Inboxed, "inbox", seed.Day(0, 6, 1)),
            ("system:uptime", NotificationPriority.Normal, "Nextcloud was unreachable for 4 minutes and is back up.", NotificationOutcome.Inboxed, "inbox", seed.Now.AddHours(-5)),
            ("system:mail", NotificationPriority.Low, "New mail from Jana Berger: Saturday?", NotificationOutcome.Inboxed, "inbox", seed.Now.AddMinutes(-50)),
        ];
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(ct);
        foreach ((string source, NotificationPriority priority, string content, NotificationOutcome outcome, string channel, DateTimeOffset at) in notifications)
        {
            var row = new Notification
            {
                Id = Guid.CreateVersion7(),
                OwnerId = seed.Owner,
                TaskId = source == $"task:{seed.Morning}" ? seed.Morning : null,
                Source = source,
                Priority = priority,
                Content = content,
                Channel = channel,
                Outcome = outcome,
                CreatedAt = at,
                DeliveredAt = at,
            };
            db.Notifications.Add(row);
            seed.Add("notifications", row.Id.ToString());
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task MemoryAsync(Seed seed, CancellationToken ct)
    {
        (string Key, ChunkType Type, string Title, string Content, int Importance)[] chunks =
        [
            ("climbing", ChunkType.Event, "Climbing on Thursdays", "Goes climbing with Jana Berger on Thursday evenings at 18:00.", 3),
            ("jana", ChunkType.Person, "Jana Berger", "Climbing partner; mail jana@example.org.", 3),
            ("voron", ChunkType.Device, "Voron 2.4 printer", "The printer is called voron (Voron 2.4, 350 mm). PETG prints at 240 °C nozzle and 80 °C bed.", 4),
            ("petg", ChunkType.Preference, "PETG for outdoor parts", "Prefers PETG Jet Black for parts that live outside, such as the planter brackets.", 3),
            ("coffee", ChunkType.Preference, "Coffee", "Drinks a flat white in the morning; the kitchen light should be bright at 7:00.", 2),
        ];
        var ids = new Dictionary<string, ChunkId>();
        foreach ((string key, ChunkType type, string title, string content, int importance) in chunks)
        {
            Result<ChunkWriteResult> written;
            try
            {
                written = await memory.WriteAsync(new ChunkDraft(ChunkScope.General, null, type, title, content, importance, "demo"), ChunkAuthor.User, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The embedding model is unreachable: the rest of the demo works without seeded memory.
                return;
            }

            if (written.IsFailure)
                continue;

            ids[key] = written.Value.Id;
            if (written.Value.Outcome == ChunkWriteOutcome.Created)
                seed.Add("chunks", written.Value.Id.ToString());
        }

        foreach ((string from, string to, ChunkEdgeKind kind) in new[] { ("climbing", "jana", ChunkEdgeKind.RelatesTo), ("petg", "voron", ChunkEdgeKind.RelatesTo) })
        {
            if (ids.TryGetValue(from, out ChunkId fromId) && ids.TryGetValue(to, out ChunkId toId))
                await memory.LinkAsync(fromId, toId, kind, ct);
        }

        if (ids.TryGetValue("coffee", out ChunkId coffee) && ids.TryGetValue("morning-coffee", out ChunkId morning))
            await DuplicateProposalAsync(seed, coffee, morning, ct);
    }

    // What the nightly duplicate job would propose for the two coffee chunks, open in the audit.
    private async Task DuplicateProposalAsync(Seed seed, ChunkId keep, ChunkId other, CancellationToken ct)
    {
        (ChunkId a, ChunkId b) = MemoryProposal.Pair(keep, other);
        var row = new MemoryProposal
        {
            Id = Guid.CreateVersion7(),
            OwnerId = seed.Owner,
            Kind = MemoryProposalKind.Duplicate,
            ChunkA = a,
            ChunkB = b,
            SuggestionJson = JsonSerializer.Serialize(new
            {
                keep = keep.Value,
                other = other.Value,
                title = "Coffee",
                content = "Drinks a flat white every morning; the kitchen light should be bright at 7:00.",
                similarity = 0.94,
            }),
            Status = MemoryProposalStatus.Open,
            CreatedAt = seed.Day(0, 3, 0),
        };
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(ct);
        db.MemoryProposals.Add(row);
        await db.SaveChangesAsync(ct);
        seed.Add("memory_proposals", row.Id.ToString());
    }

    private async Task<SessionId> SessionAsync(Seed seed, SessionSource source, DateTimeOffset at, string? satellite, CancellationToken ct)
    {
        seed.Clock.Now = at;
        var store = new EfSessionStore(contexts, executionContext, seed.Clock);
        SessionRecord session = await store.CreateAsync(new NewSession(source, null, null, new IdentityEvidence(null, null, satellite, null), source == SessionSource.Web ? "cookie" : "satellite"), ct);
        seed.Add("sessions", session.Id.ToString());
        return session.Id;
    }

    private async Task CloseAsync(Seed seed, SessionId session, DateTimeOffset at, CancellationToken ct)
    {
        seed.Clock.Now = at;
        await new EfSessionStore(contexts, executionContext, seed.Clock).CloseAsync(session, ct);
    }

    /// <summary>One run: the context step, the given model and tool steps, then the reply (none for task runs without input).</summary>
    private async Task TurnAsync(Seed seed, MarshalExecutionContext context, DateTimeOffset at, string? input, IReadOnlyList<RunStepRecord> steps, string reply, CancellationToken ct, string trigger = "turn")
    {
        seed.Clock.Now = at;
        var recorder = new DbRunRecorder(contexts, [], recording, seed.Clock);
        RunId run = await recorder.StartRunAsync(new RunStart(context, trigger), ct);
        seed.Add("runs", run.ToString());
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Context, Input = input, Content = "Demo context.", DurationMs = 35 }, ct);
        foreach (RunStepRecord step in steps)
        {
            seed.Clock.Now += TimeSpan.FromMilliseconds(step.DurationMs);
            await recorder.RecordStepAsync(run, step, ct);
        }

        if (context.SessionId is { } session && trigger == "turn")
        {
            await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Reply, Content = reply, ContinueConversation = false, DurationMs = 5 }, ct);
            await new EfSessionStore(contexts, executionContext, seed.Clock).TouchAsync(session, ct);
        }
        else
        {
            await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Reply, Content = reply, DurationMs = 5 }, ct);
        }

        await recorder.CompleteRunAsync(run, new RunCompletion(RunOutcome.Ok, null, []), ct);
    }

    private RunStepRecord Model(string? reasoning, long tokensIn, long tokensOut) => new()
    {
        Kind = RunStepKind.ModelCall,
        Model = "demo",
        Tier = "standard",
        TokensIn = tokensIn,
        TokensOut = tokensOut,
        FirstTokenMs = 420,
        Reasoning = parts.Reasoning ? reasoning : null,
        DurationMs = 900 + (int)(tokensOut * 12),
    };

    private static RunStepRecord Tool(string ns, string name, string arguments, string result, RiskTier tier, GuardDecision decision, int durationMs) => new()
    {
        Kind = RunStepKind.ToolCall,
        Tool = new ToolId(ns, name),
        Arguments = arguments,
        Content = result,
        RiskTier = tier,
        GuardDecision = decision,
        DurationMs = durationMs,
    };

    private static MarshalExecutionContext Context(Seed seed, SessionId session, InputMode mode, string? satellite = null, string? area = null) => new()
    {
        OwnerId = seed.Owner,
        Source = mode == InputMode.Voice ? SessionSource.Voice : SessionSource.Web,
        InputMode = mode,
        SatelliteId = satellite,
        AreaId = area,
        SessionId = session,
    };

    private async Task AddTaskAsync(Seed seed, AgentTask task, CancellationToken ct)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(ct);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync(ct);
        seed.Add("agent_tasks", task.Id.ToString());
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, _taskJson);

    private IDisposable Use(OwnerId owner) =>
        executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Task, IsInteractive = false });

    /// <summary>The seed in progress: owner, seed time, the owner's time zone, the clock the stores write with, and the recorded rows.</summary>
    private sealed class Seed(OwnerId owner, DateTimeOffset now, TimeZoneInfo zone, SeedClock clock)
    {
        public OwnerId Owner { get; } = owner;

        public DateTimeOffset Now { get; } = now;

        public SeedClock Clock { get; } = clock;

        public List<(string Entity, string Id)> Rows { get; } = [];

        public TaskId? Morning { get; set; }

        public void Add(string entity, string id) => Rows.Add((entity, id));

        /// <summary><paramref name="hour"/>:<paramref name="minute"/> local time, <paramref name="days"/> from today; never after the seed time.</summary>
        public DateTimeOffset Day(int days, int hour, int minute)
        {
            DateTime local = TimeZoneInfo.ConvertTime(Now, zone).Date.AddDays(days).AddHours(hour).AddMinutes(minute);
            var at = new DateTimeOffset(local, zone.GetUtcOffset(local));
            return at <= Now ? at : at.AddDays(-1);
        }
    }

    /// <summary>A clock the seeder moves to each item's time.</summary>
    private sealed class SeedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
