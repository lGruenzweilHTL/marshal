namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Home Assistant rejected the token or a command, or the connection broke.
/// </summary>
public sealed class HomeAssistantException : Exception
{
    public HomeAssistantException()
    {
    }

    public HomeAssistantException(string message)
        : base(message)
    {
    }

    public HomeAssistantException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
