using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Widgets;

/// <summary>
/// Builds the data of a built-in chat widget after a successful call of a tool that declares the widget
/// (<see cref="ToolDescriptor.Widget"/>). The web chat renders one component per kind.
/// </summary>
public interface IWidgetDataProvider
{
    /// <summary>The widget kind, e.g. <c>printer</c> or <c>music</c>.</summary>
    string Kind { get; }

    /// <summary>The widget's data, or null when there is nothing to show.</summary>
    Task<JsonElement?> GetAsync(WidgetRequest request, CancellationToken cancellationToken);
}

/// <param name="Tool">The tool that ran.</param>
/// <param name="Arguments">Its arguments.</param>
/// <param name="Output">Its result.</param>
/// <param name="Context">The turn's execution context.</param>
public sealed record WidgetRequest(ToolId Tool, JsonElement Arguments, ToolOutput Output, MarshalExecutionContext Context);

/// <summary>A widget a turn showed: kept with the reply so a reopened conversation renders it again.</summary>
/// <param name="Kind">The widget kind.</param>
/// <param name="CallId">The tool call that produced it.</param>
/// <param name="Data">The widget's data at the time of the call.</param>
public sealed record WidgetRef(string Kind, string CallId, JsonElement Data);
