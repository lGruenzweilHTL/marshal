using System.Text.Json;

namespace Marshal.Api.Tests.Http;

/// <summary>
/// Parses a <c>text/event-stream</c> body.
/// </summary>
internal static class Sse
{
    public static List<(string Event, JsonElement Data)> Parse(string body)
    {
        var events = new List<(string, JsonElement)>();
        foreach (string block in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string name = "message";
            string data = "";
            foreach (string line in block.Split('\n'))
            {
                if (line.StartsWith("event:", StringComparison.Ordinal))
                    name = line[6..].Trim();
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                    data += line[5..].Trim();
            }

            events.Add((name, JsonDocument.Parse(data).RootElement.Clone()));
        }

        return events;
    }
}
