namespace Marshal.Abstractions.Tools;

/// <summary>
/// A tool accepted by the registry, with its model-facing name.
/// </summary>
/// <param name="Tool">The tool.</param>
/// <param name="ModelName">Name the model sees (<c>^[a-zA-Z0-9_-]{1,64}$</c>).</param>
/// <param name="Source">Name of the tool source.</param>
/// <param name="IsBuiltIn">True for Marshal's own tools.</param>
public sealed record RegisteredTool(ITool Tool, string ModelName, string Source, bool IsBuiltIn)
{
    /// <summary>Internal id.</summary>
    public ToolId Id => Tool.Descriptor.Id;

    /// <summary>Descriptor of the tool.</summary>
    public ToolDescriptor Descriptor => Tool.Descriptor;
}
