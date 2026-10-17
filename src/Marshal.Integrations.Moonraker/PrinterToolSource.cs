using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.Moonraker;

/// <summary>The <c>printer.*</c> tools (a built-in source), including the camera tools; none without a configured printer.</summary>
public sealed class PrinterToolSource(IPrinterDirectory directory, IPrinterClient client, IPrinterStateCache states, IPrintBabysitterStatus babysitter, PrinterCameraTools camera) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = directory.Printers.Count == 0 ? [] : [.. PrinterTools.Create(directory, client, states, babysitter), .. camera.Create()];

    public string Name => "printer";

    public bool IsBuiltIn => true;


    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [new("printer", "3D printers: print status, progress, temperatures, camera snapshots, starting, pausing and cancelling prints")];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
