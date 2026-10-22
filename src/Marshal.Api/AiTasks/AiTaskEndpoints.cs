using System.Security.Claims;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Auth;
using Marshal.Core.AiTasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Marshal.Api.AiTasks;

/// <summary>
/// AI tasks from Home Assistant automations: one multipart request (a <c>task</c> JSON part and image
/// <c>attachment</c> parts), one headless run, one JSON answer. No streaming.
/// </summary>
internal static class AiTaskEndpoints
{
    private const int MaxTaskPartBytes = 64 * 1024;
    private const string InvalidRequest = "ai_task.invalid_request";

    private static readonly HashSet<string> _imageTypes = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    public static RouteGroupBuilder MapAiTaskEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/ai-tasks", RunAsync).WithName("RunAiTask").WithTags("AI tasks")
            .WithDescription("Runs one AI task (multipart: a 'task' JSON part with taskName, instructions, optional structure and language; " +
                "optional 'attachment' image parts) and answers with its data: a string, or an object matching structure.")
            .RequireAuthorization(ApiScopes.AiTasksWrite)
            .DisableAntiforgery()
            .Accepts<AiTaskForm>("multipart/form-data")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);
        return group;
    }

    private static async Task<Results<Ok<AiTaskResponse>, ProblemHttpResult>> RunAsync(
        HttpContext context, ClaimsPrincipal user, IAiTaskRunner runner, IOptions<AiTaskOptions> options)
    {
        CancellationToken ct = context.RequestAborted;
        AiTaskOptions limits = options.Value;
        if (context.Request.ContentLength > limits.RequestBodyLimit)
            return TooLarge(limits);
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
            bodySize.MaxRequestBodySize = limits.RequestBodyLimit;

        string? boundary = MediaTypeHeaderValue.TryParse(context.Request.ContentType, out MediaTypeHeaderValue? type) ? HeaderUtilities.RemoveQuotes(type.Boundary).Value : null;
        if (string.IsNullOrEmpty(boundary) || type?.MediaType.Value?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) != true)
            return Invalid("Send the task as multipart/form-data with a 'task' part.");

        TaskPart? task = null;
        var attachments = new List<AiTaskAttachment>();
        try
        {
            var reader = new MultipartReader(boundary, context.Request.Body);
            while (await reader.ReadNextSectionAsync(ct) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out ContentDispositionHeaderValue? disposition))
                    continue;

                string name = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? "";
                if (name == "task")
                {
                    byte[]? json = await ReadAsync(section.Body, MaxTaskPartBytes, ct);
                    Result<TaskPart> parsed = json is null ? Error("The task part is too large.") : Parse(json);
                    if (parsed.IsFailure)
                        return Problem(parsed.Error, StatusCodes.Status400BadRequest);

                    task = parsed.Value;
                }
                else if (name == "attachment")
                {
                    string mediaType = MediaTypeHeaderValue.TryParse(section.ContentType, out MediaTypeHeaderValue? partType) ? partType.MediaType.Value ?? "" : "";
                    if (!_imageTypes.Contains(mediaType))
                    {
                        return Problem(new Error("ai_task.unsupported_attachment", $"Attachments must be JPEG, PNG or WebP images, not '{mediaType}'.", ErrorType.Validation),
                            StatusCodes.Status415UnsupportedMediaType);
                    }

                    if (attachments.Count == limits.MaxAttachments)
                        return TooLarge(limits);

                    byte[]? bytes = await ReadAsync(section.Body, limits.MaxAttachmentBytes, ct);
                    if (bytes is null)
                        return TooLarge(limits);

                    attachments.Add(new AiTaskAttachment(mediaType.ToLowerInvariant(), bytes));
                }
            }
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge(limits);
        }
        catch (IOException)
        {
            // Malformed framing, e.g. a multipart body without any part.
            return Invalid("The multipart body is malformed or has no 'task' part.");
        }

        if (task is null)
            return Invalid("The 'task' part is missing.");

        Result<AiTaskResult> result = await runner.RunAsync(
            new AiTaskRequest(user.Owner(), task.TaskName, task.Instructions, task.Structure, task.Language, attachments), ct);
        if (result.IsFailure)
            return Problem(result.Error, StatusOf(result.Error.Code));

        return TypedResults.Ok(new AiTaskResponse(result.Value.Data, result.Value.RunId.Value));
    }

    private static int StatusOf(string code) => code switch
    {
        AiTaskErrors.TimeoutCode => StatusCodes.Status504GatewayTimeout,
        AiTaskErrors.DailyCapCode => StatusCodes.Status429TooManyRequests,
        AiTaskErrors.NoVisionModelCode => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status502BadGateway,
    };

    private static Result<TaskPart> Parse(byte[] json)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Error("The task part is not valid JSON.");
        }

        if (root.ValueKind != JsonValueKind.Object)
            return Error("The task part must be a JSON object.");
        if (Text(root, "taskName") is not { } taskName || string.IsNullOrWhiteSpace(taskName))
            return Error("taskName is required.");
        if (Text(root, "instructions") is not { } instructions || string.IsNullOrWhiteSpace(instructions))
            return Error("instructions must not be empty.");

        JsonElement? structure = null;
        if (root.TryGetProperty("structure", out JsonElement value) && value.ValueKind != JsonValueKind.Null)
        {
            if (value.ValueKind != JsonValueKind.Object)
                return Error("structure must be a JSON Schema object.");

            structure = value;
        }

        string? language = root.TryGetProperty("language", out JsonElement lang) && lang.ValueKind == JsonValueKind.String ? lang.GetString() : null;
        return new TaskPart(taskName.Trim(), instructions, structure, language);
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // Null when the part is longer than limit.
    private static async Task<byte[]?> ReadAsync(Stream body, int limit, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > limit)
                return null;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static Error Error(string message) => new(InvalidRequest, message, ErrorType.Validation);

    private static ProblemHttpResult Invalid(string message) => Problem(Error(message), StatusCodes.Status400BadRequest);

    private static ProblemHttpResult TooLarge(AiTaskOptions limits) => Problem(
        new Error("ai_task.too_large", $"At most {limits.MaxAttachments} attachments of at most {limits.MaxAttachmentBytes} bytes each.", ErrorType.Validation),
        StatusCodes.Status413PayloadTooLarge);

    private static ProblemHttpResult Problem(Error error, int status) => TypedResults.Problem(
        detail: error.Message,
        statusCode: status,
        title: error.Code,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    private sealed record TaskPart(string TaskName, string Instructions, JsonElement? Structure, string? Language);
}

/// <summary>The answer of an AI task.</summary>
/// <param name="Data">A string without structure; an object matching the structure otherwise.</param>
/// <param name="RunId">The run that answered (runs view).</param>
public sealed record AiTaskResponse(JsonElement Data, Guid RunId);

/// <summary>The multipart form of an AI task (OpenAPI description only).</summary>
/// <param name="Task">JSON: <c>taskName</c>, <c>instructions</c>, optional <c>structure</c> (JSON Schema object) and <c>language</c>.</param>
/// <param name="Attachment">JPEG, PNG or WebP images; repeat the part for several.</param>
public sealed record AiTaskForm(string Task, IFormFileCollection? Attachment);
