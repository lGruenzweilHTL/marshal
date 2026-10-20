using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Models;
using Marshal.Api.Tests.Http;
using Marshal.Core.Mcp;
using Marshal.Core.Models;
using Marshal.Core.Recording;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Marshal.Api.Tests.Hosting;

public sealed class TelemetryTests
{
    [Fact]
    public void Tracing_ResourceNamesTheGenAiConventionVersion()
    {
        using var factory = new MarshalApiFactory();
        _ = factory.Server;

        TracerProvider provider = factory.Services.GetRequiredService<TracerProvider>();

        Assert.Equal("1.41", GenAiConventions.Version);
        Assert.Contains(provider.GetResource().Attributes, attribute => attribute.Key == "marshal.genai_semconv_version" && attribute.Value as string == GenAiConventions.Version);
    }

    [Theory]
    [InlineData(RunTracing.SourceName)]
    [InlineData(ModelTelemetry.SourceName)]
    [InlineData(McpToolSource.TracingSourceName)]
    public void Tracing_ListensToRunModelAndMcpSources(string name)
    {
        using var factory = new MarshalApiFactory();
        _ = factory.Server;
        _ = factory.Services.GetRequiredService<TracerProvider>();

        using var source = new ActivitySource(name);

        Assert.True(source.HasListeners());
    }

    [Fact]
    public async Task InstrumentedChatClient_ChatSpanUnderCurrentActivityWithoutContent()
    {
        using var collector = new ActivityCollector(ModelTelemetry.SourceName, "test.parent");
        using var parentSource = new ActivitySource("test.parent");
        FakeChatClient inner = new FakeChatClient().ThenText("It is 21 degrees.", new UsageDetails { InputTokenCount = 1200, OutputTokenCount = 80 });
        using IChatClient client = ModelTelemetry.Instrument(inner);

        ActivityTraceId traceId;
        ActivitySpanId parentId;
        using (Activity parent = parentSource.StartActivity("parent")!)
        {
            traceId = parent.TraceId;
            parentId = parent.SpanId;
            await client.GetResponseAsync("how warm is it?", new ChatOptions { ModelId = "qwen3" }, Ct);
        }

        Activity chat = Assert.Single(collector.Trace(traceId), activity => activity.Source.Name == ModelTelemetry.SourceName);
        Assert.Equal(parentId, chat.ParentSpanId);
        Assert.Equal("chat", chat.GetTagItem("gen_ai.operation.name"));
        Assert.Equal(1200, Convert.ToInt32(chat.GetTagItem("gen_ai.usage.input_tokens"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(80, Convert.ToInt32(chat.GetTagItem("gen_ai.usage.output_tokens"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(chat.TagObjects, tag => tag.Value?.ToString()?.Contains("21 degrees", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(chat.TagObjects, tag => tag.Value?.ToString()?.Contains("how warm", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task InstrumentedEmbeddingGenerator_EmbeddingsSpanUnderCurrentActivity()
    {
        using var collector = new ActivityCollector(ModelTelemetry.SourceName, "test.parent");
        using var parentSource = new ActivitySource("test.parent");
        using IEmbeddingGenerator<string, Embedding<float>> generator = ModelTelemetry.Instrument(new FakeEmbeddingGenerator());

        ActivityTraceId traceId;
        ActivitySpanId parentId;
        using (Activity parent = parentSource.StartActivity("parent")!)
        {
            traceId = parent.TraceId;
            parentId = parent.SpanId;
            await generator.GenerateAsync(["voron"], cancellationToken: Ct);
        }

        Activity embeddings = Assert.Single(collector.Trace(traceId), activity => activity.Source.Name == ModelTelemetry.SourceName);
        Assert.Equal(parentId, embeddings.ParentSpanId);
        Assert.Equal("embeddings", embeddings.GetTagItem("gen_ai.operation.name"));
    }

    [Theory]
    [InlineData("ollama", null)]
    [InlineData("openai-compatible", "http://llama:8080/v1")]
    [InlineData("anthropic", null)]
    public void ChatFactory_EveryProvider_InstrumentedWithoutSensitiveData(string provider, string? endpoint)
    {
        using IChatClient client = new ProviderChatClientFactory().Create("standard", new ModelOptions
        {
            Provider = provider,
            Model = "model",
            Endpoint = endpoint is null ? null : new Uri(endpoint),
            ApiKey = "key",
        });

        OpenTelemetryChatClient? telemetry = client.GetService<OpenTelemetryChatClient>();
        Assert.NotNull(telemetry);
        Assert.False(telemetry.EnableSensitiveData);
    }

    [Theory]
    [InlineData("ollama", null)]
    [InlineData("openai-compatible", "http://llama:8080/v1")]
    public void EmbeddingFactory_EveryProvider_Instrumented(string provider, string? endpoint)
    {
        using IEmbeddingGenerator<string, Embedding<float>> generator = new ProviderEmbeddingGeneratorFactory().Create(
            new EmbeddingModelOptions { Provider = provider, Model = "bge-m3", Endpoint = endpoint is null ? null : new Uri(endpoint) });

        OpenTelemetryEmbeddingGenerator<string, Embedding<float>>? telemetry = generator.GetService<OpenTelemetryEmbeddingGenerator<string, Embedding<float>>>();
        Assert.NotNull(telemetry);
        Assert.False(telemetry.EnableSensitiveData);
    }

    [Fact]
    public async Task Turn_CaptureContent_SpansCarryRecordedContentNeverThePrompt()
    {
        using var collector = new ActivityCollector(RunTracing.SourceName);
        FakeTool tool = FakeTool.Sensitive("test.sensitive").Returns("the private body", reference: "test:doc/7");
        using var factory = new MarshalApiFactory(new() { ["telemetry:captureContent"] = "true" }, tool);
        factory.Model.ThenToolCall("test_sensitive", new { doc = "seven" }).ThenText("Read it.");
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/conversation/turns", new { conversationId = "c1", text = "read capture-doc-seven", inputMode = "text" }, Ct);
        await response.Content.ReadAsStringAsync(Ct);

        Activity root = Assert.Single(collector.All, activity => RunTracing.IsRun(activity) && activity.GetTagItem("marshal.input") as string == "read capture-doc-seven");
        IReadOnlyList<Activity> trace = collector.Trace(root.TraceId);
        Assert.Equal("Read it.", root.GetTagItem("marshal.reply"));
        Activity span = Assert.Single(trace, RunTracing.IsTool);
        Assert.Equal("""{"doc":"seven"}""", span.GetTagItem("gen_ai.tool.call.arguments"));
        Assert.Equal("test:doc/7", span.GetTagItem("gen_ai.tool.call.result"));
        string prompt = factory.Model.Requests[0].SystemPrompt;
        List<string> values = [.. trace.SelectMany(activity => activity.TagObjects).Select(tag => tag.Value?.ToString() ?? "")];
        Assert.DoesNotContain(values, value => value.Contains("the private body", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => value.Contains(prompt[..Math.Min(60, prompt.Length)], StringComparison.Ordinal));
    }

    [Fact]
    public void Startup_CaptureContent_LogsWarning()
    {
        var logs = new LogCollector();
        using var factory = new MarshalApiFactory(new() { ["telemetry:captureContent"] = "true" }) { TestServices = services => services.AddSingleton<ILoggerProvider>(logs) };

        _ = factory.Server;

        Assert.Contains(logs.Entries, entry => entry.StartsWith("Warning: telemetry.captureContent is on", StringComparison.Ordinal));
    }

    [Fact]
    public void Startup_CaptureContentOff_NoWarning()
    {
        var logs = new LogCollector();
        using var factory = new MarshalApiFactory { TestServices = services => services.AddSingleton<ILoggerProvider>(logs) };

        _ = factory.Server;

        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("captureContent", StringComparison.Ordinal));
    }

    [Fact]
    public void Startup_CaptureContentNotABool_Fails()
    {
        using var factory = new MarshalApiFactory(new() { ["telemetry:captureContent"] = "sometimes" });

        Assert.ThrowsAny<Exception>(() => factory.Server);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class LogCollector : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(Entries);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue($"{logLevel}: {formatter(state, exception)}");
        }
    }
}
