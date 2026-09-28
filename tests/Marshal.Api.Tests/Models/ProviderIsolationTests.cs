using System.Xml.Linq;

namespace Marshal.Api.Tests.Models;

public sealed class ProviderIsolationTests
{
    private static readonly string[] _providerPackages = ["OllamaSharp", "Anthropic", "Microsoft.Extensions.AI.OpenAI"];

    [Fact]
    public void ProviderPackages_ReferencedOnlyByApi()
    {
        string root = RepositoryRoot();
        List<string> offenders = [];

        foreach (string project in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(root, "Directory.Build.props", SearchOption.AllDirectories)))
        {
            if (Path.GetFileName(project) == "Marshal.Api.csproj")
                continue;

            IEnumerable<string> references = XDocument.Load(project).Descendants("PackageReference")
                .Select(element => (string?)element.Attribute("Include") ?? "");
            offenders.AddRange(references.Where(_providerPackages.Contains).Select(package => $"{Path.GetFileName(project)} -> {package}"));
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void ApiProject_ReferencesAllProviders()
    {
        string api = Path.Combine(RepositoryRoot(), "src", "Marshal.Api", "Marshal.Api.csproj");

        List<string> references = [.. XDocument.Load(api).Descendants("PackageReference").Select(element => (string?)element.Attribute("Include") ?? "")];

        Assert.All(_providerPackages, package => Assert.Contains(package, references));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marshal.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
