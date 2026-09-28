using Marshal.Abstractions.Tools;

namespace Marshal.Testing;

/// <summary>
/// Dynamic tool sources over a mutable list.
/// </summary>
public sealed class FakeToolSourceProvider : IToolSourceProvider
{
    /// <summary>Sources offered; may be changed between registry refreshes.</summary>
    public List<IToolSource> Sources { get; } = [];

    /// <inheritdoc />
    IReadOnlyList<IToolSource> IToolSourceProvider.Sources => [.. Sources];
}
