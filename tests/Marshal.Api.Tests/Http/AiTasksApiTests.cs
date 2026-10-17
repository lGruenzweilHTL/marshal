using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tokens;
using Marshal.Data.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Http;

public sealed class AiTasksApiTests : IAsyncLifetime
{
    private static readonly byte[] _jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _factory = Factory();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task WithoutScope_403()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        HttpResponseMessage response = await client.PostAsync("/api/v1/ai-tasks", Task("garage", "Remind me"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TextTask_200WithStringDataAndRunId()
    {
        _factory.Model.ThenText("The garage is still open.");
        HttpClient client = await ClientAsync();

        HttpResponseMessage response = await client.PostAsync("/api/v1/ai-tasks", Task("garage", "Write a short reminder that the garage is open"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("The garage is still open.", body.GetProperty("data").GetString());
        Run run = await RunAsync(body.GetProperty("runId").GetGuid());
        Assert.Equal((SessionSource.Automation, "ai_task:garage", RunOutcome.Ok), (run.Source, run.Trigger, run.Outcome));
    }

    [Fact]
    public async Task StructuredTask_200WithObjectData()
    {
        _factory.Model.ThenText("A courier.").ThenText("""{"visitor":"courier"}""");
        HttpClient client = await ClientAsync();

        HttpResponseMessage response = await client.PostAsync("/api/v1/ai-tasks", Task("door", "Who is there?", structure: """{"type":"object","properties":{"visitor":{"type":"string"}},"required":["visitor"]}"""), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("courier", body.GetProperty("data").GetProperty("visitor").GetString());
    }

    [Theory]
    [InlineData(null, "ai_task.invalid_request")]
    [InlineData("""{"taskName":"x","instructions":"  "}""", "ai_task.invalid_request")]
    [InlineData("""{"taskName":"x","instructions":"go","structure":"text"}""", "ai_task.invalid_request")]
    [InlineData("""{"taskName":"","instructions":"go"}""", "ai_task.invalid_request")]
    [InlineData("not json", "ai_task.invalid_request")]
    public async Task InvalidTaskPart_400(string? task, string code)
    {
        HttpClient client = await ClientAsync();
        var content = new MultipartFormDataContent();
        if (task is not null)
            content.Add(new StringContent(task, Encoding.UTF8, "application/json"), "task");

        HttpResponseMessage response = await client.PostAsync("/api/v1/ai-tasks", content, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeAsync(response));
        Assert.Empty(_factory.Model.Requests);
    }

    [Fact]
    public async Task NonImageAttachment_415()
    {
        HttpClient client = await ClientAsync();

        HttpResponseMessage response = await client.PostAsync("/api/v1/ai-tasks", Task("door", "Look", attachments: [("text/plain", "hello"u8.ToArray())]), Ct);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("ai_task.unsupported_attachment", await CodeAsync(response));
    }

    [Fact]
    public async Task AttachmentAboveLimit_413()
    {
        await _factory.DisposeAsync();
        _factory = Factory(new() { ["aiTasks:maxAttachmentBytes"] = "4", ["models:fast:vision"] = "true" });
        HttpClient client = await ClientAsync();

        HttpResponseMessage response = await client.PostAsync("/api/v1/ai-tasks", Task("door", "Look", attachments: [("image/jpeg", _jpeg)]), Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("ai_task.too_large", await CodeAsync(response));
    }

    [Fact]
    public async Task TooManyAttachments_413()
    {
        await _factory.DisposeAsync();
        _factory = Factory(new() { ["aiTasks:maxAttachments"] = "1", ["models:fast:vision"] = "true" });
        HttpClient client = await ClientAsync();

        HttpResponseMessage response = await client.PostAsync("/api/v1/ai-tasks", Task("door", "Look", attachments: [("image/jpeg", _jpeg), ("image/png", _jpeg)]), Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task AttachmentWithoutVisionModel_422()
    {
        HttpClient client = await ClientAsync();

        HttpResponseMessage response = await client.PostAsync("/api/v1/ai-tasks", Task("door", "Look", attachments: [("image/jpeg", _jpeg)]), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("ai_task.no_vision_model", await CodeAsync(response));
        Assert.Empty(_factory.Model.Requests);
    }

    [Fact]
    public async Task AttachmentWithVisionModel_RecordKeepsTypeSizeAndHashNotBytes()
    {
        await _factory.DisposeAsync();
        _factory = Factory(new() { ["models:fast:vision"] = "true" });
        _factory.Model.ThenText("A parcel on the doormat.");
        HttpClient client = await ClientAsync();

        HttpResponseMessage response = await client.PostAsync("/api/v1/ai-tasks", Task("door", "What is at the door?", attachments: [("image/jpeg", _jpeg)]), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_jpeg, _factory.Model.Requests[0].Messages[^1].Contents.OfType<DataContent>().Single().Data.ToArray());
        Guid runId = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("runId").GetGuid();
        string payloads = string.Join("\n", await StepPayloadsAsync(runId));
        Assert.Contains("image/jpeg", payloads, StringComparison.Ordinal);
        Assert.Contains($"8 bytes, sha256 {Convert.ToHexStringLower(SHA256.HashData(_jpeg))}", payloads, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(_jpeg), payloads, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DailyCapReached_429()
    {
        await _factory.DisposeAsync();
        _factory = Factory(new() { ["tasks:dailyCostCap"] = "0" });
        HttpClient client = await ClientAsync();

        HttpResponseMessage response = await client.PostAsync("/api/v1/ai-tasks", Task("garage", "Remind me"), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("ai_task.daily_cap", await CodeAsync(response));
    }

    [Fact]
    public async Task Timeout_504()
    {
        await _factory.DisposeAsync();
        _factory = Factory(new() { ["aiTasks:timeoutSeconds"] = "1" });
        _factory.Model.Then(async (_, _, ct) =>
        {
            await System.Threading.Tasks.Task.Delay(Timeout.Infinite, ct);
            return new ChatResponse();
        });
        HttpClient client = await ClientAsync();

        HttpResponseMessage response = await client.PostAsync("/api/v1/ai-tasks", Task("garage", "Remind me"), Ct);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("ai_task.timeout", await CodeAsync(response));
    }

    [Fact]
    public async Task InvalidOutput_502AndRunError()
    {
        _factory.Model.ThenText("A courier.").ThenText("{}").ThenText("{}");
        HttpClient client = await ClientAsync();

        HttpResponseMessage response = await client.PostAsync("/api/v1/ai-tasks", Task("door", "Who?", structure: """{"type":"object","required":["visitor"]}"""), Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("ai_task.invalid_output", await CodeAsync(response));
    }

    [Fact]
    public async Task OpenApi_DocumentsTheEndpoint()
    {
        JsonElement document = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);

        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/ai-tasks", out JsonElement path));
        Assert.True(path.TryGetProperty("post", out _));
    }

    private static MarshalApiFactory Factory(Dictionary<string, string?>? settings = null) => new(settings);

    private Task<HttpClient> ClientAsync() => _factory.ClientWithTokenAsync(ApiScopes.AiTasksWrite);

    private static MultipartFormDataContent Task(string name, string instructions, string? structure = null, IReadOnlyList<(string Type, byte[] Bytes)>? attachments = null)
    {
        var content = new MultipartFormDataContent();
        string task = structure is null
            ? JsonSerializer.Serialize(new { taskName = name, instructions, language = "en" })
            : $$"""{"taskName":{{JsonSerializer.Serialize(name)}},"instructions":{{JsonSerializer.Serialize(instructions)}},"language":"en","structure":{{structure}}}""";
        content.Add(new StringContent(task, Encoding.UTF8, "application/json"), "task");
        int index = 0;
        foreach ((string type, byte[] bytes) in attachments ?? [])
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(type);
            content.Add(file, "attachment", $"file{index++}");
        }

        return content;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();

    private async Task<OwnerId> OwnerAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IInstanceOwner>().GetAsync(Ct);
    }

    private async Task<Run> RunAsync(Guid id) =>
        await _factory.WithDatabaseAsync(await OwnerAsync(), db => db.Runs.AsNoTracking().SingleAsync(run => run.Id == RunId.From(id), Ct));

    private async Task<List<string?>> StepPayloadsAsync(Guid id) =>
        await _factory.WithDatabaseAsync(await OwnerAsync(), db => db.RunSteps.AsNoTracking().Where(step => step.RunId == RunId.From(id)).Select(step => step.Payload).ToListAsync(Ct));
}
