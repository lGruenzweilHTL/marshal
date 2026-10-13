using System.Reflection;
using System.Text.Json;

namespace Marshal.Demo;

/// <summary>
/// The demo content embedded in this assembly under <c>Resources/</c> (home, mail, feeds, notes).
/// </summary>
internal static class DemoResources
{
    private const string Prefix = "demo/";
    private static readonly Assembly _assembly = typeof(DemoResources).Assembly;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Paths below <paramref name="folder"/>, relative to it, with '/' separators.</summary>
    public static IEnumerable<string> List(string folder) =>
        _assembly.GetManifestResourceNames()
            .Select(name => name.Replace('\\', '/'))
            .Where(name => name.StartsWith($"{Prefix}{folder}/", StringComparison.Ordinal))
            .Select(name => name[($"{Prefix}{folder}/".Length)..])
            .Order(StringComparer.Ordinal);

    public static string Text(string path)
    {
        using Stream stream = Open(path);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static T Read<T>(string path)
    {
        using Stream stream = Open(path);
        return JsonSerializer.Deserialize<T>(stream, Json) ?? throw new InvalidDataException($"Demo resource {path} is empty.");
    }

    private static Stream Open(string path)
    {
        string wanted = Prefix + path;
        string? name = _assembly.GetManifestResourceNames().FirstOrDefault(candidate => candidate.Replace('\\', '/') == wanted);
        return name is null
            ? throw new InvalidOperationException($"Demo resource {path} is missing.")
            : _assembly.GetManifestResourceStream(name)!;
    }
}
