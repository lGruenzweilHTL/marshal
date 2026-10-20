using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;
using Marshal.Core.Tools;
using Marshal.Tasks.Policies;
using Marshal.Testing;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Tests.Policies;

public sealed class DefaultTaskToolPolicyTests
{
    private readonly FakeTemplateCatalog _templates = new();
    private ToolRegistry _registry = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<DefaultTaskToolPolicy> PolicyAsync()
    {
        _registry = new ToolRegistry([new FakeToolSource("test", true,
            new FakeTool("agent.delegate"), new FakeTool("memory.recall"), new FakeTool("tasks.create"), new FakeTool("notifications.notify"),
            new FakeTool("printer.status"), new FakeTool("printer.pause"), new FakeTool("mail.search"), new FakeTool("mail.create_draft"),
            new FakeTool("calendar.list_events"))]);
        await _registry.RefreshAsync(Ct);
        return new DefaultTaskToolPolicy(_registry, _templates, Options.Create(new ToolsOptions { Core = ["agent", "memory", "tasks", "notifications"] }));
    }

    private static MarshalExecutionContext Turn(params string[] loaded)
    {
        MarshalExecutionContext context = TestExecutionContext.Text();
        context.LoadedTools.RetrievalActive = true;
        foreach (string name in loaded)
            context.LoadedTools.Load(name, "retrieval");
        return context;
    }

    [Fact]
    public async Task Derive_CoreAndLoadedNamespacesOfTheTemplate()
    {
        Result<IReadOnlyList<string>?> policy = (await PolicyAsync()).Derive(null, Turn("agent", "printer"), null);

        Assert.Equal(["agent.*", "memory.*", "notifications.*", "printer.*", "tasks.*"], policy.Value);
    }

    [Fact]
    public async Task Derive_NarrowTemplate_KeepsOnlyNamespacesItAllows()
    {
        _templates.Items.Add(FakeTemplateCatalog.Template("printing", ["printer.*", "notifications.notify"]));

        Result<IReadOnlyList<string>?> policy = (await PolicyAsync()).Derive("printing", Turn("printer", "mail"), null);

        Assert.Equal(["notifications.*", "printer.*"], policy.Value);
    }

    [Fact]
    public async Task Derive_RetrievalSkipped_EveryNamespaceTheTemplateAllows()
    {
        MarshalExecutionContext context = TestExecutionContext.Text();

        Result<IReadOnlyList<string>?> policy = (await PolicyAsync()).Derive(null, context, null);

        Assert.Equal(["agent.*", "calendar.*", "mail.*", "memory.*", "notifications.*", "printer.*", "tasks.*"], policy.Value);
    }

    [Fact]
    public async Task Derive_Sessionless_TemplateAllowlist()
    {
        Result<IReadOnlyList<string>?> policy = (await PolicyAsync()).Derive(null, null, null);

        Assert.True(policy.IsSuccess);
        Assert.Null(policy.Value);
    }

    [Fact]
    public async Task Derive_InsideATaskRun_InheritsTheCreatorsPolicy()
    {
        MarshalExecutionContext run = Turn("mail") with { ToolPolicy = ["printer.status", "notifications.notify"] };

        Result<IReadOnlyList<string>?> policy = (await PolicyAsync()).Derive(null, run, null);

        Assert.Equal(["printer.status", "notifications.notify"], policy.Value);
    }

    [Fact]
    public async Task Requested_NarrowingAccepted()
    {
        Result<IReadOnlyList<string>?> policy = (await PolicyAsync()).Derive(null, Turn(), ["printer.status", "notifications.notify"]);

        Assert.Equal(["printer.status", "notifications.notify"], policy.Value);
    }

    [Theory]
    [InlineData("mail.*")]
    [InlineData("printer.*")]
    [InlineData("nothing.here")]
    [InlineData("")]
    public async Task Requested_WideningRejected(string glob)
    {
        _templates.Items.Add(FakeTemplateCatalog.Template("printing", ["printer.status", "notifications.notify"]));

        Result<IReadOnlyList<string>?> policy = (await PolicyAsync()).Derive("printing", Turn(), [glob]);

        Assert.Equal((ErrorType.Validation, "tasks.invalid_tools"), (policy.Error.Type, policy.Error.Code));
    }

    [Fact]
    public async Task Requested_InsideATaskRun_CannotLeaveTheCreatorsPolicy()
    {
        MarshalExecutionContext run = Turn() with { ToolPolicy = ["printer.status"] };

        Result<IReadOnlyList<string>?> policy = (await PolicyAsync()).Derive(null, run, ["mail.search"]);

        Assert.Equal("tasks.invalid_tools", policy.Error.Code);
    }

    [Fact]
    public async Task Check_AgainstTheTemplateForUiEdits()
    {
        DefaultTaskToolPolicy policy = await PolicyAsync();
        _templates.Items.Add(FakeTemplateCatalog.Template("printing", ["printer.*", "calendar.*"]));

        Assert.True(policy.Check("printing", ["printer.*", "calendar.list_events"]).IsSuccess);
        Assert.Equal("tasks.invalid_tools", policy.Check("printing", ["mail.search"]).Error.Code);
    }

    [Fact]
    public void Allows_NullPolicyAllowsEverything()
    {
        Assert.True(TaskToolPolicies.Allows(null, new ToolId("mail", "search")));
        Assert.True(TaskToolPolicies.Allows(["mail.*"], new ToolId("mail", "search")));
        Assert.False(TaskToolPolicies.Allows(["printer.*"], new ToolId("mail", "search")));
    }
}
