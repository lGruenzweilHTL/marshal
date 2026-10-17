namespace Marshal.Abstractions.Tools;

/// <summary>
/// A tool namespace and the one-line English description tool retrieval matches turns against.
/// </summary>
/// <param name="Name">Namespace (<c>printer</c>, an MCP server's namespace).</param>
/// <param name="Description">What the namespace's tools are about.</param>
public sealed record ToolNamespace(string Name, string Description);
