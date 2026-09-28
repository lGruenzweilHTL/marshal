namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// HA's registries, Assist exposure and entity states, kept fresh over the websocket.
/// </summary>
public interface IHomeAssistantCache
{
    /// <summary>The current data; null until loaded and again while reconnecting (callers treat that as unknown).</summary>
    HaSnapshot? Current { get; }

    /// <summary>Why the cache is not ready, or null.</summary>
    string? LastError { get; }
}
