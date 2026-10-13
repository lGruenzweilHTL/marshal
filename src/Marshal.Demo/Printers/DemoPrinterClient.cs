using Marshal.Abstractions;
using Marshal.Abstractions.Printers;
using Marshal.Integrations.Moonraker;

namespace Marshal.Demo.Printers;

/// <summary>
/// The simulated printer's commands. Validation is the real integration's (files, temperatures, moves, only
/// allowlisted macros); accepted commands change the simulated job and queue. Never contacts a printer.
/// </summary>
public sealed class DemoPrinterClient(DemoWorld world, IPrinterDirectory directory, DemoPrinterStateCache states) : IPrinterClient
{
    public Task<Result<IReadOnlyList<PrinterFile>>> ListFilesAsync(string printer, CancellationToken cancellationToken) =>
        Task.FromResult(Known(printer) is { } error ? error : Result.Success(world.Printer.Files));

    public Task<Result<JobQueueStatus>> GetQueueAsync(string printer, CancellationToken cancellationToken) =>
        Task.FromResult(Known(printer) is { } error ? error : Result.Success(new JobQueueStatus(world.Printer.QueueState, world.Printer.Queue)));

    public Task<Result<IReadOnlyList<PrintJob>>> GetHistoryAsync(string printer, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(Known(printer) is { } error ? error : Result.Success<IReadOnlyList<PrintJob>>([.. world.Printer.History.Take(limit)]));

    public Result Validate(string printer, PrinterCommand command)
    {
        Result<PrinterInfo> found = directory.Resolve(printer);
        return found.IsFailure ? found.Error : PrinterCommandRules.Validate(command, found.Value);
    }

    public Task<Result> SendAsync(string printer, PrinterCommand command, CancellationToken cancellationToken)
    {
        Result valid = Validate(printer, command);
        if (valid.IsFailure)
            return Task.FromResult(valid);

        DemoPrinter simulated = world.Printer;
        switch (command)
        {
            case PrinterCommand.StartPrint start when !simulated.Files.Any(file => file.Path == start.File):
                return Task.FromResult<Result>(PrinterErrors.CommandFailed(printer, $"File {start.File} does not exist."));
            case PrinterCommand.StartPrint start:
                simulated.Start(start.File);
                break;
            case PrinterCommand.Pause:
                simulated.Pause();
                break;
            case PrinterCommand.Resume:
                simulated.Resume();
                break;
            case PrinterCommand.Cancel or PrinterCommand.EmergencyStop:
                simulated.Cancel();
                break;
            case PrinterCommand.QueueAdd add:
                simulated.AddToQueue(add.Files);
                break;
            case PrinterCommand.QueueRemove remove:
                simulated.RemoveFromQueue(remove.JobIds);
                break;
            case PrinterCommand.QueueClear:
                simulated.ClearQueue();
                break;
            case PrinterCommand.QueueJump jump:
                simulated.JumpQueue(jump.JobId);
                break;
            case PrinterCommand.QueuePause:
                simulated.SetQueueState("paused");
                break;
            case PrinterCommand.QueueStart:
                simulated.SetQueueState("ready");
                break;
        }

        // Temperatures, homing, moves and macros are accepted without visible effect.
        states.Refresh();
        return Task.FromResult(Result.Success());
    }

    private Error? Known(string printer) => directory.Resolve(printer) is { IsFailure: true } failed ? failed.Error : null;
}
