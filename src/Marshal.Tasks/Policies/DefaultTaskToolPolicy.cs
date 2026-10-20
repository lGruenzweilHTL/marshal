using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Core.Tools;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Policies;

/// <summary>Derives and checks the tool policies of agent tasks (tool id globs inside the template's allowlist).</summary>
public interface ITaskToolPolicy
{
    /// <summary>
    /// The policy a new task freezes. With <paramref name="requested"/> globs: those, when they only narrow the
    /// template's allowlist (and the creating task's policy). Without: the creating task's policy inside a task run,
    /// otherwise the core namespaces plus the namespaces the creating run loaded. Null without a creating run (the
    /// template's allowlist).
    /// </summary>
    Result<IReadOnlyList<string>?> Derive(string? template, MarshalExecutionContext? creator, IReadOnlyList<string>? requested);

    /// <summary>
    /// <c>tasks.invalid_tools</c> unless every glob matches at least one current tool and only tools the template
    /// (and <paramref name="within"/>, when given) allow.
    /// </summary>
    Result Check(string? template, IReadOnlyList<string> globs, IReadOnlyList<string>? within = null);
}

/// <summary>
/// <see cref="ITaskToolPolicy"/> against the current tool registry. Derived policies are namespace globs
/// (<c>printer.*</c>); the template's allowlist still applies on top at run time. The registry is resolved lazily: it
/// lists the task tools, which use this policy.
/// </summary>
public sealed class DefaultTaskToolPolicy(Func<IToolRegistry> registry, ITemplateCatalog templates, IOptions<ToolsOptions> options) : ITaskToolPolicy
{
    public DefaultTaskToolPolicy(IToolRegistry registry, ITemplateCatalog templates, IOptions<ToolsOptions> options)
        : this(() => registry, templates, options)
    {
    }

    public const string InvalidToolsCode = "tasks.invalid_tools";

    public Result<IReadOnlyList<string>?> Derive(string? template, MarshalExecutionContext? creator, IReadOnlyList<string>? requested)
    {
        if (requested is not null)
        {
            Result valid = Check(template, requested, creator?.ToolPolicy);
            return valid.IsFailure ? valid.Error : Result.Success<IReadOnlyList<string>?>([.. requested.Select(glob => glob.Trim()).Distinct(StringComparer.Ordinal)]);
        }

        if (creator is null)
            return Result.Success<IReadOnlyList<string>?>(null);
        if (creator.ToolPolicy is { } inherited)
            return Result.Success<IReadOnlyList<string>?>(inherited);

        AgentTemplate allowlist = Find(template);
        IEnumerable<string> namespaces = Allowed(allowlist).Select(tool => tool.Id.Namespace);
        if (creator.LoadedTools.RetrievalActive)
        {
            var loaded = new HashSet<string>([.. options.Value.CoreNamespaces, .. creator.LoadedTools.All.Select(item => item.Name)], StringComparer.Ordinal);
            namespaces = namespaces.Where(loaded.Contains);
        }

        return Result.Success<IReadOnlyList<string>?>([.. namespaces.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(name => $"{name}.*")]);
    }

    public Result Check(string? template, IReadOnlyList<string> globs, IReadOnlyList<string>? within = null)
    {
        AgentTemplate allowlist = Find(template);
        IReadOnlyList<RegisteredTool> tools = registry().Current.Tools;
        foreach (string raw in globs)
        {
            string glob = raw?.Trim() ?? "";
            List<RegisteredTool> matched = glob.Length == 0 ? [] : [.. tools.Where(tool => ToolGlob.Matches(glob, tool.Id))];
            if (matched.Count == 0)
                return Invalid($"'{glob}' matches no tool.");
            if (matched.FirstOrDefault(tool => !allowlist.Allows(tool.Id) || !TaskToolPolicies.Allows(within, tool.Id)) is { } outside)
                return Invalid($"'{glob}' includes {outside.Id}, which this task may not use. tools can only narrow the template's tools.");
        }

        return Result.Success();
    }

    private IEnumerable<RegisteredTool> Allowed(AgentTemplate allowlist) => registry().Current.Tools.Where(tool => allowlist.Allows(tool.Id));

    private AgentTemplate Find(string? template) => templates.Find(template ?? AgentTemplate.DefaultName) ?? templates.Find(AgentTemplate.DefaultName) ?? AgentTemplate.BuiltInDefault;

    private static Error Invalid(string message) => new(InvalidToolsCode, message, ErrorType.Validation);
}
