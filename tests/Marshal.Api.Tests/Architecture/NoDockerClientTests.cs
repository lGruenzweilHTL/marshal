using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Marshal.Api.Tests.Architecture;

/// <summary>
/// Marshal never accesses a Docker socket: coding sessions run on the separate executor host, reached over HTTP.
/// No Marshal assembly may reference a Docker client (or the executor host itself) or name the Docker socket, and the
/// example compose file mounts no socket into the Marshal container.
/// </summary>
public sealed class NoDockerClientTests
{
    [Fact]
    public void NoAssembly_ReferencesADockerClientOrTheExecutorHost()
    {
        List<string> found = [];
        foreach (string path in NoMailSendTests.SolutionAssemblies())
        {
            using FileStream stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader metadata = pe.GetMetadataReader();
            string name = NoMailSendTests.AssemblyName(path);

            found.AddRange(metadata.AssemblyReferences
                .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
                .Where(reference => reference.Contains("docker", StringComparison.OrdinalIgnoreCase) || reference.Contains("ExecutorHost", StringComparison.OrdinalIgnoreCase) || reference == "marshal-executor-host")
                .Select(reference => $"{name} references {reference}"));
            found.AddRange(NoMailSendTests.TypeReferences(path)
                .Where(type => type.StartsWith("Docker.", StringComparison.Ordinal))
                .Select(type => $"{name} uses {type}"));
            found.AddRange(UserStrings(metadata)
                .Where(text => text.Contains("docker.sock", StringComparison.OrdinalIgnoreCase))
                .Select(text => $"{name} names the Docker socket: {text}"));
        }

        Assert.Empty(found);
    }

    [Fact]
    public void ComposeFile_MountsNoDockerSocket()
    {
        string compose = File.ReadAllText(Path.Combine(NoMailSendTests.RepositoryRoot(), "deploy", "docker-compose.yml"));

        Assert.DoesNotContain("docker.sock", compose, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> UserStrings(MetadataReader metadata)
    {
        if (metadata.GetHeapSize(HeapIndex.UserString) <= 1)
            yield break;

        UserStringHandle handle = MetadataTokens.UserStringHandle(1);
        while (!handle.IsNil)
        {
            yield return metadata.GetUserString(handle);
            handle = metadata.GetNextHandle(handle);
        }
    }
}
