using Microsoft.Extensions.AI;

namespace Marshal.Api.Models;

/// <summary>
/// Wraps provider clients with Microsoft.Extensions.AI's OpenTelemetry instrumentation: a <c>chat</c> or
/// <c>embeddings</c> span per call, parented on the current run span, and the standard <c>gen_ai.client.*</c>
/// metrics. Sensitive data stays off: it would export raw tool results inside the messages, past the recording
/// redaction.
/// </summary>
internal static class ModelTelemetry
{
    /// <summary>Activity source and meter name of the instrumentation.</summary>
    public const string SourceName = "Experimental.Microsoft.Extensions.AI";

    public static IChatClient Instrument(IChatClient client) => client
        .AsBuilder()
        .UseOpenTelemetry(sourceName: SourceName, configure: telemetry => telemetry.EnableSensitiveData = false)
        .Build();

    public static IEmbeddingGenerator<string, Embedding<float>> Instrument(IEmbeddingGenerator<string, Embedding<float>> generator) => generator
        .AsBuilder()
        .UseOpenTelemetry(sourceName: SourceName, configure: telemetry => telemetry.EnableSensitiveData = false)
        .Build();
}
