using System.Text.RegularExpressions;

namespace Marshal.Abstractions.Tools;

/// <summary>
/// Internal tool id <c>&lt;namespace&gt;.&lt;name&gt;</c>, used in risk rules, policies, allowlists, run records and the audit log.
/// </summary>
public readonly partial record struct ToolId
{
    /// <summary>Creates an id; throws for an invalid namespace or empty name (a programming error).</summary>
    public ToolId(string @namespace, string name)
    {
        if (!IsValidNamespace(@namespace))
            throw new ArgumentException($"Tool namespace '{@namespace}' must match [a-z][a-z0-9]*.", nameof(@namespace));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tool name must not be empty.", nameof(name));

        Namespace = @namespace;
        Name = name;
    }

    /// <summary>The namespace (capability or MCP server id).</summary>
    public string Namespace { get; }

    /// <summary>The tool name within the namespace.</summary>
    public string Name { get; }

    /// <summary>True when <paramref name="value"/> is a valid namespace (<c>[a-z][a-z0-9]*</c>).</summary>
    public static bool IsValidNamespace(string? value) => value is not null && NamespacePattern().IsMatch(value);

    /// <summary>Parses <c>namespace.name</c>; the first dot separates namespace and name.</summary>
    public static Result<ToolId> Parse(string? value)
    {
        int dot = value?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        if (value is null || dot <= 0 || dot == value.Length - 1 || !IsValidNamespace(value[..dot]))
            return new Error("tool.invalid_id", $"'{value}' is not a valid tool id.", ErrorType.Validation);

        return new ToolId(value[..dot], value[(dot + 1)..]);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Namespace}.{Name}";

    [GeneratedRegex("^[a-z][a-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamespacePattern();
}
