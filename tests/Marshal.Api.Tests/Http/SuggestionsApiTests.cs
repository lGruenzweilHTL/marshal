using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Maintenance;
using Marshal.Abstractions.Suggestions;
using Marshal.Abstractions.Runs;
using Marshal.Api.Tests.Auth;
using Marshal.Data;
using Marshal.Data.Identity;
using Marshal.Tasks.SkillProposals;
using Marshal.Tasks.Suggestions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Http;

public sealed class SuggestionsApiTests : IAsyncLifetime
{
    private readonly FakeCounter _counter = new();
    private SignedInOwner _instanceOwner = null!;
    private MarshalApiFactory _factory = null!;
    private HttpClient _browser = null!;
    private OwnerId _user;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new MarshalApiFactory(new(AuthFactory.Local().ToSettings(0))
        {
            ["maintenance:items:dishwasher_filter:name"] = "Dishwasher filter",
            ["maintenance:items:dishwasher_filter:source:ha"] = "sensor.dishwasher_cycles",
            ["maintenance:items:dishwasher_filter:every"] = "60",
            ["maintenance:items:dishwasher_filter:warnBefore"] = "10",
        })
        {
            TestServices = services =>
            {
                // First, so it reads the test entity before the real Home Assistant source does.
                services.Insert(0, ServiceDescriptor.Singleton<IMaintenanceCounterSource>(_counter));
                services.AddSingleton<IInstanceOwner>(provider =>
                    _instanceOwner = new SignedInOwner(new EfInstanceOwner(provider.GetRequiredService<IDbContextFactory<MarshalDbContext>>())));
            },
        };
        (_browser, _user) = await Browser.SignInAsync(_factory, "user");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task SkillProposals_ListOwnOpenOnes_DetailHasContentAndDiff()
    {
        Guid proposal = await AddSkillAsync(_user, "nightly-backup-check", diff: "--- a/skills/x/SKILL.md\n+++ b/skills/x/SKILL.md\n");
        (_, OwnerId other) = await Browser.SignInAsync(_factory, "user");
        Guid others = await AddSkillAsync(other, "someone-else");

        List<JsonElement>? listed = await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/skill-proposals", Ct);
        JsonElement detail = await _browser.GetFromJsonAsync<JsonElement>($"/api/v1/skill-proposals/{proposal}", Ct);

        JsonElement item = Assert.Single(listed!);
        Assert.Equal((proposal.ToString(), "nightly-backup-check", "open", true, 3),
            (item.GetProperty("id").GetString(), item.GetProperty("name").GetString(), item.GetProperty("status").GetString(),
             item.GetProperty("hasDiff").GetBoolean(), item.GetProperty("sourceRunCount").GetInt32()));
        Assert.StartsWith("---\nname: nightly-backup-check\n", detail.GetProperty("content").GetString());
        Assert.StartsWith("--- a/skills/x/SKILL.md", detail.GetProperty("diff").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.GetAsync($"/api/v1/skill-proposals/{others}", Ct)).StatusCode);
    }

    [Fact]
    public async Task SkillProposal_Download_ReturnsSkillMdMarksDownloadedAndIsAudited()
    {
        Guid proposal = await AddSkillAsync(_user, "nightly-backup-check");

        HttpResponseMessage response = await _browser.GetAsync($"/api/v1/skill-proposals/{proposal}/download", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SKILL.md", response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("---\nname: nightly-backup-check\n", await response.Content.ReadAsStringAsync(Ct));
        JsonElement listed = Assert.Single((await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/skill-proposals", Ct))!);
        Assert.Equal("downloaded", listed.GetProperty("status").GetString());
        Assert.Equal(1, await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.CountAsync(entry => entry.Action == "skill_proposal.download" && entry.Target == proposal.ToString(), Ct)));
    }

    [Fact]
    public async Task SkillProposal_Dismiss_LeavesTheListAndIsAudited()
    {
        Guid proposal = await AddSkillAsync(_user, "nightly-backup-check");

        HttpResponseMessage response = await _browser.PostAsync($"/api/v1/skill-proposals/{proposal}/dismiss", null, Ct);

        Assert.Equal("dismissed", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
        Assert.Empty((await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/skill-proposals", Ct))!);
        Assert.Equal(1, await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.CountAsync(entry => entry.Action == "skill_proposal.dismiss", Ct)));
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.PostAsync($"/api/v1/skill-proposals/{Guid.NewGuid()}/dismiss", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task OtherUsersSkillProposal_NotDownloadedOrDismissed()
    {
        (_, OwnerId other) = await Browser.SignInAsync(_factory, "user");
        Guid proposal = await AddSkillAsync(other, "someone-else");

        Assert.Equal(HttpStatusCode.NotFound, (await _browser.GetAsync($"/api/v1/skill-proposals/{proposal}/download", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.PostAsync($"/api/v1/skill-proposals/{proposal}/dismiss", null, Ct)).StatusCode);
        Assert.Equal(SkillProposalStatus.Open, await _factory.WithDatabaseAsync(other, db => db.SkillProposals.Where(row => row.Id == proposal).Select(row => row.Status).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Suggestions_ListWithSnippet_DismissIsAudited()
    {
        Guid suggestion = await AddSuggestionAsync(_user);

        JsonElement listed = Assert.Single((await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/suggestions", Ct))!);
        HttpResponseMessage dismissed = await _browser.PostAsync($"/api/v1/suggestions/{suggestion}/dismiss", null, Ct);

        Assert.Equal(("maintenance_counter", "Track the nozzle?", "maintenance:\n", "inbox"),
            (listed.GetProperty("detector").GetString(), listed.GetProperty("text").GetString(), listed.GetProperty("snippet").GetString(), listed.GetProperty("delivery").GetString()));
        Assert.Equal("dismissed", (await dismissed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("suggestion").GetProperty("status").GetString());
        Assert.Empty((await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/suggestions", Ct))!);
        Assert.Equal(1, await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.CountAsync(entry => entry.Action == "suggestion.dismiss", Ct)));
    }

    [Fact]
    public async Task Detectors_MuteUnmuteAndReset()
    {
        await AddSuggestionAsync(_user);

        JsonElement detector = Assert.Single(
            (await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/suggestions/detectors", Ct))!,
            item => item.GetProperty("detector").GetString() == "maintenance_counter");
        JsonElement muted = await (await _browser.PutAsJsonAsync("/api/v1/suggestions/detectors/maintenance_counter/mute", new { until = (DateTimeOffset?)null }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        JsonElement reset = await (await _browser.PostAsync("/api/v1/suggestions/detectors/maintenance_counter/reset", null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        JsonElement unmuted = await (await _browser.DeleteAsync("/api/v1/suggestions/detectors/maintenance_counter/mute", Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        HttpResponseMessage past = await _browser.PutAsJsonAsync("/api/v1/suggestions/detectors/maintenance_counter/mute", new { until = DateTimeOffset.UtcNow.AddDays(-1) }, Ct);

        Assert.Equal(1, detector.GetProperty("sent").GetInt32());
        Assert.True(muted.GetProperty("muted").GetBoolean());
        Assert.Equal((0, true), (reset.GetProperty("sent").GetInt32(), reset.GetProperty("muted").GetBoolean()));
        Assert.False(unmuted.GetProperty("muted").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
    }

    [Fact]
    public async Task Maintenance_InstanceOwnerSeesItems_DoneIsAudited()
    {
        _instanceOwner.Owner = _user;
        _counter.Value = 40;
        await _browser.GetAsync("/api/v1/maintenance", Ct);
        _counter.Value = 95;

        JsonElement item = Assert.Single((await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/maintenance", Ct))!);
        HttpResponseMessage done = await _browser.PostAsync("/api/v1/maintenance/dishwasher_filter/done", null, Ct);
        HttpResponseMessage unknown = await _browser.PostAsync("/api/v1/maintenance/nothing/done", null, Ct);

        Assert.Equal(("dishwasher_filter", "Dishwasher filter", 55m, 60m, "soon"),
            (item.GetProperty("id").GetString(), item.GetProperty("name").GetString(), item.GetProperty("usage").GetDecimal(),
             item.GetProperty("every").GetDecimal(), item.GetProperty("state").GetString()));
        JsonElement after = await done.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((0m, "ok"), (after.GetProperty("usage").GetDecimal(), after.GetProperty("state").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(1, await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.CountAsync(entry => entry.Action == "maintenance.done" && entry.Target == "dishwasher_filter", Ct)));
    }

    [Fact]
    public async Task Maintenance_OtherUsers_SeeNothingAndCannotMarkDone()
    {
        _counter.Value = 40;

        Assert.Empty((await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/maintenance", Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.PostAsync("/api/v1/maintenance/dishwasher_filter/done", null, Ct)).StatusCode);
    }

    private async Task<Guid> AddSkillAsync(OwnerId owner, string name, string? diff = null)
    {
        using IDisposable scope = Use(owner);
        SkillProposalRecord added = await _factory.Services.GetRequiredService<ISkillProposalStore>().AddAsync(new NewSkillProposal(
            name, "Checks last night's backup.", $"---\nname: {name}\ndescription: Checks last night's backup.\n---\n\nSteps.\n", diff,
            [RunId.New(), RunId.New(), RunId.New()], Guid.NewGuid().ToString("N"), SkillProposalStatus.Open), Ct);
        return added.Id;
    }

    private async Task<Guid> AddSuggestionAsync(OwnerId owner)
    {
        using IDisposable scope = Use(owner);
        var id = Guid.NewGuid();
        await _factory.Services.GetRequiredService<ISuggestionStore>().AddAsync(new SuggestionRecord(
            id, owner, "maintenance_counter", "printer:voron", "Track the nozzle?", new Dictionary<string, string> { ["counter"] = "voron" },
            SuggestionDelivery.Inbox, SuggestionStatus.Open, Guid.NewGuid().ToString("N"), null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), Snippet: "maintenance:\n"), Ct);
        return id;
    }

    private IDisposable Use(OwnerId owner) =>
        _factory.Services.GetRequiredService<IExecutionContextAccessor>().Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Web });

    private sealed class FakeCounter : IMaintenanceCounterSource
    {
        public decimal? Value { get; set; }

        public bool Handles(MaintenanceSource source) => source.Ha == "sensor.dishwasher_cycles";

        public Task<Result<MaintenanceReading>> ReadAsync(MaintenanceSource source, CancellationToken cancellationToken) =>
            Task.FromResult<Result<MaintenanceReading>>(Value is { } value
                ? new MaintenanceReading(value, null)
                : new Error("ha.unavailable", "Home Assistant is not connected.", ErrorType.Unavailable));
    }

    /// <summary>The instance owner as stored, or the user a test makes the owner.</summary>
    private sealed class SignedInOwner(IInstanceOwner stored) : IInstanceOwner
    {
        public OwnerId? Owner { get; set; }

        public async Task<OwnerId> GetAsync(CancellationToken cancellationToken) => Owner ?? await stored.GetAsync(cancellationToken);
    }
}
