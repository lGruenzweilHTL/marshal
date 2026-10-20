namespace Marshal.Core.Recording;

/// <summary>
/// The <c>telemetry</c> section of <c>marshal.yaml</c>.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>OTLP gRPC endpoint for traces and metrics; null = no export.</summary>
    public Uri? OtlpEndpoint { get; set; }

    /// <summary>
    /// Adds the user input, tool arguments, tool results and the reply to spans, redacted like run recording.
    /// Off by default: the telemetry backend then holds personal data.
    /// </summary>
    public bool CaptureContent { get; set; }
}
