using Marshal.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Models;

/// <summary>
/// Warns at startup about models configured with reasoning that cannot return it; they are called as <c>off</c>.
/// </summary>
public sealed partial class ModelReasoningCheck(IOptions<ModelRoutingOptions> options, ILogger<ModelReasoningCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach ((string key, ModelOptions model) in options.Value.Models)
        {
            if (ModelReasoningSupport.TryParse(model.Reasoning, out ModelReasoning reasoning) && reasoning != ModelReasoning.Off && !ModelReasoningSupport.Supported(model))
                LogUnsupported(logger, key, model.Model);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "models.{Key}.reasoning is set, but {Model} cannot return reasoning; it runs without reasoning")]
    private static partial void LogUnsupported(ILogger logger, string key, string model);
}
