namespace Marshal.Abstractions;

/// <summary>
/// A strongly typed id wrapping a <see cref="Guid"/>.
/// </summary>
/// <typeparam name="TSelf">The id type itself.</typeparam>
public interface IGuidId<TSelf> : IParsable<TSelf>
    where TSelf : struct, IGuidId<TSelf>
{
    /// <summary>The wrapped value.</summary>
    Guid Value { get; }

    /// <summary>Wraps an existing value.</summary>
    static abstract TSelf From(Guid value);
}
