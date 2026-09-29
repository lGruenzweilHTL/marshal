namespace Marshal.Integrations.Moonraker;

/// <summary>A Moonraker request failed (rejected, timed out or disconnected).</summary>
public sealed class MoonrakerException : Exception
{
    public MoonrakerException()
    {
    }

    public MoonrakerException(string message)
        : base(message)
    {
    }

    public MoonrakerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
