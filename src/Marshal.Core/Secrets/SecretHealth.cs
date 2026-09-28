namespace Marshal.Core.Secrets;

/// <summary>
/// Optional secret references that did not resolve at startup (names only, never values), for <c>/health</c>.
/// </summary>
public sealed class SecretHealth
{
    private readonly Lock _lock = new();
    private List<string> _missing = [];

    public IReadOnlyList<string> MissingOptional
    {
        get
        {
            lock (_lock)
            {
                return [.. _missing];
            }
        }
    }

    public void Set(IEnumerable<string> missingOptional)
    {
        lock (_lock)
        {
            _missing = [.. missingOptional];
        }
    }
}
