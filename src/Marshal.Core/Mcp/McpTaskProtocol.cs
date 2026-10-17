using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Marshal.Abstractions.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Marshal.Core.Mcp;

/// <summary>
/// Wire types of the tasks extension (SEP-2663) that <c>ModelContextProtocol.Core</c> 2.2 does not model. The SDK's
/// typed <c>CallToolAsync</c> keeps <c>resultType: task</c> but drops the task id, so tool calls deserialize into
/// <see cref="CallResult"/>, which carries both shapes.
/// </summary>
internal static class McpTaskProtocol
{
    public const string GetMethod = "tasks/get";
    public const string CancelMethod = "tasks/cancel";
    public const string UpdateMethod = "tasks/update";

    private static readonly JsonSerializerOptions _options = CreateOptions();

    public static JsonSerializerOptions Options => _options;

    public static JsonTypeInfo<CallResult> CallResultType { get; } = (JsonTypeInfo<CallResult>)_options.GetTypeInfo(typeof(CallResult));

    private static JsonSerializerOptions CreateOptions()
    {
        // Protocol types (content blocks, results) keep the SDK's converters; the types here use reflection.
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        return options;
    }

    public static TimeSpan? Interval(long? milliseconds) => milliseconds is > 0 ? TimeSpan.FromMilliseconds(milliseconds.Value) : null;

    public static McpTaskStatus? StatusOf(string? status) => status switch
    {
        "working" => McpTaskStatus.Working,
        "input_required" => McpTaskStatus.InputRequired,
        "completed" => McpTaskStatus.Completed,
        "failed" => McpTaskStatus.Failed,
        "cancelled" => McpTaskStatus.Cancelled,
        _ => null,
    };

    /// <summary>A <c>tools/call</c> result: a <c>CallToolResult</c>, or a <c>CreateTaskResult</c> (<c>resultType: task</c>).</summary>
    public sealed class CallResult : Result
    {
        [JsonPropertyName("content")]
        public IList<ContentBlock> Content { get; set; } = [];

        [JsonPropertyName("structuredContent")]
        public JsonElement? StructuredContent { get; set; }

        [JsonPropertyName("isError")]
        public bool? IsError { get; set; }

        [JsonPropertyName("taskId")]
        public string? TaskId { get; set; }

        [JsonPropertyName("pollIntervalMs")]
        public long? PollIntervalMs { get; set; }

        public bool IsTask => ResultType == "task" && !string.IsNullOrEmpty(TaskId);

        public CallToolResult ToToolResult() => new() { Content = Content, StructuredContent = StructuredContent, IsError = IsError };
    }

    /// <summary><c>tasks/get</c> and <c>tasks/cancel</c> parameters.</summary>
    public sealed class TaskParams : RequestParams
    {
        [JsonPropertyName("taskId")]
        public required string TaskId { get; init; }
    }

    /// <summary>A <c>tasks/get</c> result (a <c>DetailedTask</c>).</summary>
    public sealed class TaskResult : Result
    {
        [JsonPropertyName("taskId")]
        public string? TaskId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("pollIntervalMs")]
        public long? PollIntervalMs { get; set; }

        [JsonPropertyName("result")]
        public CallToolResult? Result { get; set; }

        [JsonPropertyName("error")]
        public TaskError? Error { get; set; }

        [JsonPropertyName("inputRequests")]
        public IDictionary<string, InputRequest>? InputRequests { get; set; }
    }

    /// <summary><c>tasks/update</c> parameters: answers to the task's input requests by key (the inherited <c>inputResponses</c>).</summary>
    public sealed class UpdateParams : RequestParams
    {
        [JsonPropertyName("taskId")]
        public required string TaskId { get; init; }
    }

    /// <summary>The JSON-RPC error of a failed task.</summary>
    public sealed class TaskError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    /// <summary>The empty <c>tasks/cancel</c> acknowledgement.</summary>
    public sealed class EmptyResult : Result;
}
