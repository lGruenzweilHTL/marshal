using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;

namespace Marshal.Testing;

/// <summary>
/// Configurable tool for registry, guard and loop tests. Records every invocation.
/// </summary>
public sealed class FakeTool : ITool
{
    private readonly List<ToolInvocation> _invocations = [];
    private Func<ToolInvocation, Result<ToolOutput>> _handler = _ => new ToolOutput("ok");
    private Func<MarshalExecutionContext, bool> _available = _ => true;
    private Func<JsonElement, Result> _validate = _ => Result.Success();

    /// <summary>Creates a fake tool with id <paramref name="id"/> (<c>namespace.name</c>).</summary>
    public FakeTool(string id, RiskTier defaultTier = RiskTier.Safe)
    {
        Descriptor = new ToolDescriptor
        {
            Id = ToolId.Parse(id).Value,
            Description = $"Fake tool {id}.",
            ParametersSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone(),
            DefaultTier = defaultTier,
        };
    }

    /// <inheritdoc />
    public ToolDescriptor Descriptor { get; private set; }

    /// <summary>Invocations received, in order.</summary>
    public IReadOnlyList<ToolInvocation> Invocations => _invocations;

    /// <summary>A safe tool.</summary>
    public static FakeTool Safe(string id = "test.safe") => new(id);

    /// <summary>A tool whose default tier is confirm.</summary>
    public static FakeTool Confirm(string id = "test.confirm") => new(id, RiskTier.Confirm);

    /// <summary>A tool whose default tier is forbidden.</summary>
    public static FakeTool Forbidden(string id = "test.forbidden") => new(id, RiskTier.Forbidden);

    /// <summary>A safe tool returning untrusted third-party content.</summary>
    public static FakeTool Untrusted(string id = "mail.read", string kind = "mail") =>
        new FakeTool(id).With(descriptor => descriptor with { UntrustedOutput = true, TaintKind = kind });

    /// <summary>A safe tool that persists state the agent acts on later.</summary>
    public static FakeTool Persisting(string id = "memory.remember") =>
        new FakeTool(id).With(descriptor => descriptor with { Persists = true });

    /// <summary>A safe tool whose results are recorded as references only.</summary>
    public static FakeTool Sensitive(string id = "notes.read") =>
        new FakeTool(id).With(descriptor => descriptor with { Sensitive = true });

    /// <summary>Changes the descriptor.</summary>
    public FakeTool With(Func<ToolDescriptor, ToolDescriptor> change)
    {
        Descriptor = change(Descriptor);
        return this;
    }

    /// <summary>Sets the result every call returns.</summary>
    public FakeTool Returns(string content, string? reference = null) => Returns(_ => new ToolOutput(content, reference));

    /// <summary>Sets a failed result every call returns.</summary>
    public FakeTool Fails(string code, string message) => Returns(_ => new Error(code, message));

    /// <summary>Sets a custom handler.</summary>
    public FakeTool Returns(Func<ToolInvocation, Result<ToolOutput>> handler)
    {
        _handler = handler;
        return this;
    }

    /// <summary>Sets the tool-specific visibility condition.</summary>
    public FakeTool AvailableWhen(Func<MarshalExecutionContext, bool> available)
    {
        _available = available;
        return this;
    }

    /// <summary>Sets the argument validation the guard runs before tiers.</summary>
    public FakeTool ValidatesWith(Func<JsonElement, Result> validate)
    {
        _validate = validate;
        return this;
    }

    /// <inheritdoc />
    public Result Validate(JsonElement arguments) => _validate(arguments);

    /// <inheritdoc />
    public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_available(context));

    /// <inheritdoc />
    public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        _invocations.Add(invocation);
        return Task.FromResult(_handler(invocation));
    }
}
