namespace Marshal.Abstractions.Guard;

/// <summary>
/// A concrete target a tool call acts on (e.g. one Home Assistant entity).
/// </summary>
/// <param name="Id">Target id (e.g. <c>lock.front_door</c>).</param>
/// <param name="Attributes">Attributes rules can match on, keyed camelCase (<c>domain</c>, <c>deviceClass</c>).</param>
public sealed record RiskTarget(string Id, IReadOnlyDictionary<string, string> Attributes);
