using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.HomeAssistant.Calendar;

/// <summary>The <c>calendar.*</c> tools (a built-in source). They read, create and update; there is no delete.</summary>
public sealed class CalendarToolSource(ICalendarProvider provider, IUserProfileProvider profiles) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = CalendarTools.Create(provider, profiles);

    public string Name => "calendar";

    public bool IsBuiltIn => true;


    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [new("calendar", "Calendars: events, appointments, meetings and free time")];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
