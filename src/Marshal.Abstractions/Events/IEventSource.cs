namespace Marshal.Abstractions.Events;

/// <summary>
/// Something that publishes events (HA websocket, printers, mail). Shown in the web UI with its state.
/// </summary>
public interface IEventSource
{
    /// <summary>The <see cref="MarshalEvent.Source"/> it publishes.</summary>
    string Source { get; }

    /// <summary>Whether it currently receives events.</summary>
    bool IsConnected { get; }

    /// <summary>When it last received something (webhook sources); null when unknown or never.</summary>
    DateTimeOffset? LastReceivedAt => null;
}
