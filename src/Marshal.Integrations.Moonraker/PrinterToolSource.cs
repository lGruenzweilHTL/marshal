using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.Moonraker;

/// <summary>The <c>printer.*</c> tools (a built-in source); none without a configured printer.</summary>
public sealed class PrinterToolSource(IPrinterDirectory directory, IPrinterClient client, IPrinterStateCache states) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = directory.Printers.Count == 0 ? [] : PrinterTools.Create(directory, client, states);

    public string Name => "printer";

    public bool IsBuiltIn => true;

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
