using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Marshal.Api.Tests.Architecture;

/// <summary>
/// Marshal never sends mail: no assembly of the solution may reference an SMTP type (MailKit's <c>SmtpClient</c>,
/// <c>System.Net.Mail</c>, anything named Smtp). The scan covers every project output next to the tests and fails
/// when a project under <c>src/</c> is missing, so new projects are checked too.
/// </summary>
public sealed class NoMailSendTests
{
    [Fact]
    public void NoAssembly_ReferencesAnSmtpType()
    {
        var references = SolutionAssemblies().ToDictionary(AssemblyName, TypeReferences);

        List<string> smtp = [.. references.SelectMany(pair => pair.Value.Where(IsSmtp).Select(type => $"{pair.Key}: {type}"))];

        Assert.Empty(smtp);
        // The scan sees real references: the IMAP provider uses MailKit.
        Assert.Contains("MailKit.Net.Imap.ImapClient", references["Marshal.Integrations.Mail"]);
    }

    [Fact]
    public void Scan_CoversEverySourceProject()
    {
        HashSet<string> scanned = [.. SolutionAssemblies().Select(AssemblyName)];
        IEnumerable<string> projects = Directory.GetFiles(Path.Combine(RepositoryRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            // The executor host is a separate service with its own image; nothing in Marshal references it.
            .Where(name => name != "Marshal.ExecutorHost")
            .Select(name => name == "Marshal.Api" ? "marshal" : name);

        Assert.All(projects, project => Assert.Contains(project, scanned));
    }

    private static bool IsSmtp(string type) =>
        type.Contains("smtp", StringComparison.OrdinalIgnoreCase) || type.StartsWith("System.Net.Mail.", StringComparison.Ordinal);

    // Every project output next to the tests (the host references all src projects); tests and fakes excluded.
    private static List<string> SolutionAssemblies() =>
        [.. Directory.GetFiles(AppContext.BaseDirectory, "*.dll")
            .Where(path => Path.GetFileName(path) is var file && (file == "marshal.dll" || file.StartsWith("Marshal.", StringComparison.Ordinal)))
            .Where(path => !path.EndsWith(".Tests.dll", StringComparison.Ordinal) && !path.EndsWith("Marshal.Testing.dll", StringComparison.Ordinal))];

    private static string AssemblyName(string path) => Path.GetFileNameWithoutExtension(path);

    private static List<string> TypeReferences(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        return [.. metadata.TypeReferences.Select(handle =>
        {
            TypeReference type = metadata.GetTypeReference(handle);
            return $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";
        })];
    }

    private static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "Marshal.sln")))
            directory = Path.GetDirectoryName(directory);
        return directory ?? throw new InvalidOperationException("Marshal.sln not found.");
    }
}
