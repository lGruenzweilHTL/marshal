using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// The model-facing printer tools with their fixed tiers. Every tool takes an optional <c>printer</c> that defaults
/// to the only configured printer. Control tools turn their arguments into a <see cref="PrinterCommand"/> during
/// validation, so malformed calls and macros outside the allowlist fail before any confirmation is asked.
/// </summary>
internal static class PrinterTools
{
    private const string NoArguments = "{}";

    public static IReadOnlyList<ITool> Create(IPrinterDirectory directory, IPrinterClient client, IPrinterStateCache states)
    {
        string[] macros = [.. directory.Printers.SelectMany(printer => printer.Macros).Distinct().Order(StringComparer.Ordinal)];
        return
        [
            Read(directory, "status", "Current state of the 3D printer: job, progress, layer, time left and temperatures.", NoArguments,
                (printer, _, _) => Task.FromResult(states.Get(printer.Name).Map(state => new ToolOutput(Status(state))))),
            Read(directory, "list_files", "Lists the printable files on the 3D printer, newest first.", NoArguments,
                async (printer, _, ct) => (await client.ListFilesAsync(printer.Name, ct)).Map(files => Json(files.Take(50).Select(file => new { file.Path, file.Size, modified = file.Modified })))),
            Read(directory, "job_queue", "Shows the 3D printer's job queue.", NoArguments,
                async (printer, _, ct) => (await client.GetQueueAsync(printer.Name, ct)).Map(queue => Json(new { queue.State, jobs = queue.Jobs.Select(job => new { jobId = job.JobId, job.Filename, addedAt = job.AddedAt }) }))),
            Read(directory, "history", "Shows recent print jobs, newest first.", """{"limit":{"type":"integer","minimum":1,"maximum":50,"description":"Number of jobs (default 10)."}}""",
                async (printer, arguments, ct) => (await client.GetHistoryAsync(printer.Name, Integer(arguments, "limit") ?? 10, ct)).Map(Json)),
            Command(directory, client, "pause", "Pauses the running print.", RiskTier.Safe, NoArguments, _ => new PrinterCommand.Pause()),
            Command(directory, client, "emergency_stop", "Emergency stop: halts the printer immediately. Only for emergencies; Klipper needs a restart afterwards.", RiskTier.Safe, NoArguments, _ => new PrinterCommand.EmergencyStop()),
            Command(directory, client, "start_print", "Starts printing a file from the printer's file list.", RiskTier.Confirm,
                """{"file":{"type":"string","description":"Path from printer_list_files."}}""",
                arguments => Text(arguments, "file") is { } file ? new PrinterCommand.StartPrint(file) : PrinterErrors.Invalid("file is required.")),
            Command(directory, client, "resume", "Resumes a paused print.", RiskTier.Confirm, NoArguments, _ => new PrinterCommand.Resume()),
            Command(directory, client, "cancel", "Cancels the running print.", RiskTier.Confirm, NoArguments, _ => new PrinterCommand.Cancel()),
            Command(directory, client, "set_temperature", "Sets a heater target temperature (0 turns it off).", RiskTier.Confirm,
                """{"heater":{"type":"string","enum":["extruder","bed"]},"target":{"type":"number","minimum":0,"description":"Target in °C."}}""",
                SetTemperature),
            Command(directory, client, "home", "Homes the given axes, or all axes.", RiskTier.Confirm,
                """{"axes":{"type":"array","items":{"type":"string","enum":["x","y","z"]},"description":"Axes to home; all when omitted."}}""",
                Home),
            Command(directory, client, "move", "Moves one axis relative to its current position.", RiskTier.Confirm,
                """{"axis":{"type":"string","enum":["x","y","z"]},"distance":{"type":"number","description":"Distance in mm; negative moves back."}}""",
                Move),
            Command(directory, client, "edit_queue", "Changes the job queue: add files, remove jobs, clear it, pause or start it, or move a job to the front.", RiskTier.Confirm,
                """{"action":{"type":"string","enum":["add","remove","clear","pause","start","jump"]},"files":{"type":"array","items":{"type":"string"},"description":"Files to add."},"job_ids":{"type":"array","items":{"type":"string"},"description":"Jobs to remove."},"job_id":{"type":"string","description":"Job to move to the front."}}""",
                EditQueue),
            Command(directory, client, "run_macro", "Runs one of the allowed Klipper macros (no arguments).", RiskTier.Confirm,
                new JsonObject { ["name"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. macros.Select(macro => (JsonNode)macro)]) } }.ToJsonString(),
                arguments => Text(arguments, "name") is { } name ? new PrinterCommand.RunMacro(name) : PrinterErrors.Invalid("name is required."),
                available: macros.Length > 0),
        ];
    }

    internal static string Status(PrinterState state)
    {
        StringBuilder text = new StringBuilder(state.Printer).Append(": ");
        if (state.KlippyState != "ready")
        {
            text.Append(CultureInfo.InvariantCulture, $"Klipper is {state.KlippyState}");
            if (state.StateMessage is { } reason)
                text.Append(CultureInfo.InvariantCulture, $" ({reason.Trim()})");
            text.Append(". ");
        }

        text.Append(state.PrintState);
        if (state.PrintState is "printing" or "paused" && state.Filename is { } file)
        {
            text.Append(CultureInfo.InvariantCulture, $" {file}, {state.Progress * 100:0}%");
            if (state.CurrentLayer is { } layer && state.TotalLayers is { } layers)
                text.Append(CultureInfo.InvariantCulture, $", layer {layer}/{layers}");
            if (state.EtaSeconds is { } eta)
                text.Append(", ").Append(Duration(eta)).Append(" left");
        }

        if (state.Message is { } message && state.PrintState == "error")
            text.Append(CultureInfo.InvariantCulture, $" ({message})");

        text.Append(CultureInfo.InvariantCulture, $". Temperatures: hotend {state.Extruder.Temperature:0}/{state.Extruder.Target:0} °C, bed {state.Bed.Temperature:0}/{state.Bed.Target:0} °C.");
        return text.ToString();
    }

    private static string Duration(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalMinutes < 90
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(span.TotalMinutes)} min")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours} h {span.Minutes} min");
    }

    private static Result<PrinterCommand> SetTemperature(JsonElement arguments)
    {
        PrinterHeater? heater = Text(arguments, "heater") switch
        {
            "extruder" or "hotend" => PrinterHeater.Extruder,
            "bed" or "heater_bed" => PrinterHeater.Bed,
            _ => null,
        };
        if (heater is null)
            return PrinterErrors.Invalid("heater must be extruder or bed.");

        return Number(arguments, "target") is { } target ? new PrinterCommand.SetTemperature(heater.Value, target) : PrinterErrors.Invalid("target is required.");
    }

    private static Result<PrinterCommand> Home(JsonElement arguments)
    {
        var axes = new List<PrinterAxis>();
        if (arguments.TryGetProperty("axes", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in list.EnumerateArray())
            {
                if (Axis(item.ValueKind == JsonValueKind.String ? item.GetString() : null) is not { } axis)
                    return PrinterErrors.Invalid("axes may only contain x, y and z.");
                axes.Add(axis);
            }
        }

        return new PrinterCommand.Home(axes);
    }

    private static Result<PrinterCommand> Move(JsonElement arguments)
    {
        if (Axis(Text(arguments, "axis")) is not { } axis)
            return PrinterErrors.Invalid("axis must be x, y or z.");

        return Number(arguments, "distance") is { } distance ? new PrinterCommand.Move(axis, distance) : PrinterErrors.Invalid("distance is required.");
    }

    private static Result<PrinterCommand> EditQueue(JsonElement arguments) => Text(arguments, "action") switch
    {
        "add" => new PrinterCommand.QueueAdd(Texts(arguments, "files")),
        "remove" => new PrinterCommand.QueueRemove(Texts(arguments, "job_ids")),
        "clear" => new PrinterCommand.QueueClear(),
        "pause" => new PrinterCommand.QueuePause(),
        "start" => new PrinterCommand.QueueStart(),
        "jump" => Text(arguments, "job_id") is { } id ? new PrinterCommand.QueueJump(id) : PrinterErrors.Invalid("job_id is required."),
        _ => PrinterErrors.Invalid("action must be add, remove, clear, pause, start or jump."),
    };

    private static PrinterAxis? Axis(string? text) => text?.ToLowerInvariant() switch
    {
        "x" => PrinterAxis.X,
        "y" => PrinterAxis.Y,
        "z" => PrinterAxis.Z,
        _ => null,
    };

    private static PrinterTool Read(IPrinterDirectory directory, string name, string description, string properties, Func<PrinterInfo, JsonElement, CancellationToken, Task<Result<ToolOutput>>> run) =>
        new PrinterTool(directory, name, description, RiskTier.Safe, properties, null, run, true);

    private static PrinterTool Command(IPrinterDirectory directory, IPrinterClient client, string name, string description, RiskTier tier, string properties, Func<JsonElement, Result<PrinterCommand>> parse, bool available = true) =>
        new PrinterTool(directory, name, description, tier, properties, parse, async (printer, arguments, ct) =>
        {
            PrinterCommand command = parse(arguments).Value;
            Result sent = await client.SendAsync(printer.Name, command, ct);
            return sent.IsFailure ? sent.Error : new ToolOutput($"Done: {command.Describe()} on {printer.Name}.");
        }, available);

    private static ToolOutput Json(object value) => new(JsonSerializer.Serialize(value, JsonSerializerOptions.Web));

    private static string? Text(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static double? Number(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static int? Integer(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : null;

    private static string[] Texts(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : [];

    private sealed class PrinterTool(
        IPrinterDirectory directory,
        string name,
        string description,
        RiskTier tier,
        string properties,
        Func<JsonElement, Result<PrinterCommand>>? parse,
        Func<PrinterInfo, JsonElement, CancellationToken, Task<Result<ToolOutput>>> run,
        bool available) : ITool
    {
        public ToolDescriptor Descriptor { get; } = new()
        {
            Id = new ToolId("printer", name),
            Description = description,
            ParametersSchema = Schema(directory, properties),
            DefaultTier = tier,
        };

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) => ValueTask.FromResult(available);

        public Result Validate(JsonElement arguments)
        {
            Result<PrinterInfo> printer = directory.Resolve(Text(arguments, "printer"));
            if (printer.IsFailure)
                return printer.Error;
            if (parse is null)
                return Result.Success();

            Result<PrinterCommand> command = parse(arguments);
            return command.IsFailure ? command.Error : PrinterCommandRules.Validate(command.Value, printer.Value);
        }

        public string Summarize(JsonElement arguments, string language)
        {
            string printer = directory.Resolve(Text(arguments, "printer")) is { IsSuccess: true } resolved ? resolved.Value.Name : Text(arguments, "printer") ?? "the printer";
            return parse?.Invoke(arguments) is { IsSuccess: true } command ? $"{command.Value.Describe()} on {printer}" : $"{Descriptor.Id} on {printer}";
        }

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result valid = Validate(invocation.Arguments);
            if (valid.IsFailure)
                return valid.Error;

            return await run(directory.Resolve(Text(invocation.Arguments, "printer")).Value, invocation.Arguments, cancellationToken);
        }

        private static JsonElement Schema(IPrinterDirectory directory, string properties)
        {
            JsonObject schema = JsonNode.Parse(properties)!.AsObject();
            schema["printer"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = directory.Printers.Count == 1
                    ? $"Printer name (default: {directory.Printers[0].Name})."
                    : $"Printer name, one of: {string.Join(", ", directory.Printers.Select(printer => printer.Name))}.",
            };
            return JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object", ["properties"] = schema });
        }
    }
}
