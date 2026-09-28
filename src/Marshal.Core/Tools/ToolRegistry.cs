using System.Security.Cryptography;
using System.Text;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Tools;

/// <summary>
/// Collects tools from static sources (built-in tools) and dynamic sources (<see cref="IToolSourceProvider"/>).
/// Built-in sources come first; a source that uses a reserved namespace (non-built-in), reuses another source's
/// namespace, or collides on a model-facing name is rejected as a unit.
/// </summary>
public sealed class ToolRegistry(IEnumerable<IToolSource> sources, IEnumerable<IToolSourceProvider> providers) : IToolRegistry
{
    private const int MaxModelNameLength = 64;
    private const int HashLength = 8;

    private readonly IToolSource[] _sources = [.. sources];
    private readonly IToolSourceProvider[] _providers = [.. providers];
    private volatile ToolSnapshot _snapshot = ToolSnapshot.Empty;

    public ToolRegistry(IEnumerable<IToolSource> sources)
        : this(sources, [])
    {
    }

    public ToolSnapshot Current => _snapshot;

    public IReadOnlyList<RegisteredTool> Tools => _snapshot.Tools;

    public IReadOnlyList<ToolSourceError> Errors => _snapshot.Errors;

    public RegisteredTool? FindByModelName(string modelName) => _snapshot.FindByModelName(modelName);

    public RegisteredTool? FindById(ToolId id) => _snapshot.FindById(id);

    public async Task<ToolSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        var tools = new List<RegisteredTool>();
        var errors = new List<ToolSourceError>();
        var byModelName = new Dictionary<string, RegisteredTool>(StringComparer.Ordinal);
        var namespaceOwners = new Dictionary<string, string>(StringComparer.Ordinal);

        IEnumerable<IToolSource> all = _sources.Concat(_providers.SelectMany(provider => provider.Sources));
        foreach (IToolSource source in all.OrderByDescending(source => source.IsBuiltIn))
        {
            IReadOnlyList<ITool> offered;
            try
            {
                offered = await source.GetToolsAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add(new ToolSourceError(source.Name, $"Source '{source.Name}' failed to list its tools: {exception.Message}"));
                continue;
            }

            string? rejection = Check(source, offered, byModelName, namespaceOwners);
            if (rejection is not null)
            {
                errors.Add(new ToolSourceError(source.Name, rejection));
                continue;
            }

            foreach (ITool tool in offered)
            {
                var registered = new RegisteredTool(tool, ModelName(tool.Descriptor.Id), source.Name, source.IsBuiltIn);
                tools.Add(registered);
                byModelName[registered.ModelName] = registered;
                if (!source.IsBuiltIn)
                    namespaceOwners[registered.Id.Namespace] = source.Name;
            }
        }

        var snapshot = new ToolSnapshot(tools, errors);
        _snapshot = snapshot;
        return snapshot;
    }

    /// <summary>Model-facing name: <c>namespace_name</c>, sanitized, truncated with a hash suffix above 64 characters.</summary>
    public static string ModelName(ToolId id)
    {
        var builder = new StringBuilder($"{id.Namespace}_");
        foreach (char character in id.Name)
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_');

        string name = builder.ToString();
        if (name.Length <= MaxModelNameLength)
            return name;

        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToString())))[..HashLength];
        return $"{name[..(MaxModelNameLength - HashLength - 1)]}_{hash}";
    }

    private static string? Check(IToolSource source, IReadOnlyList<ITool> offered, Dictionary<string, RegisteredTool> byModelName, Dictionary<string, string> namespaceOwners)
    {
        var ownNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ITool tool in offered)
        {
            ToolId id = tool.Descriptor.Id;
            if (!source.IsBuiltIn && ReservedNamespaces.All.Contains(id.Namespace))
                return $"Source '{source.Name}' uses the reserved namespace '{id.Namespace}'.";

            if (!source.IsBuiltIn && namespaceOwners.TryGetValue(id.Namespace, out string? owner) && owner != source.Name)
                return $"Namespace '{id.Namespace}' of source '{source.Name}' is already used by source '{owner}'.";

            string modelName = ModelName(id);
            if (byModelName.TryGetValue(modelName, out RegisteredTool? existing))
                return $"Tool '{id}' of source '{source.Name}' collides with '{existing.Id}' of source '{existing.Source}' (model name '{modelName}').";

            if (!ownNames.Add(modelName))
                return $"Source '{source.Name}' offers the model name '{modelName}' twice.";
        }

        return null;
    }
}
