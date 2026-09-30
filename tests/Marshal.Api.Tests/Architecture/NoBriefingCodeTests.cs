using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Marshal.Api.Tests.Architecture;

/// <summary>
/// The daily briefing is only configuration (a template, skills and a named task). No Marshal assembly may define a
/// briefing type or member, and no briefing route, setting or key may appear as a string: text without spaces that
/// names the briefing. Prose that uses it as an example (a tool description) is allowed.
/// </summary>
public sealed class NoBriefingCodeTests
{
    [Fact]
    public void NoAssembly_DefinesBriefingTypesMembersRoutesOrSettings()
    {
        List<string> found = [];
        foreach (string path in NoMailSendTests.SolutionAssemblies())
        {
            using FileStream stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader metadata = pe.GetMetadataReader();
            string name = NoMailSendTests.AssemblyName(path);

            found.AddRange(Names(metadata).Where(Mentions).Select(member => $"{name} defines {member}"));
            found.AddRange(UserStrings(metadata)
                .Where(text => Mentions(text) && !text.Any(char.IsWhiteSpace))
                .Select(text => $"{name} names \"{text}\""));
        }

        Assert.Empty(found);
    }

    private static bool Mentions(string text) => text.Contains("briefing", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Names(MetadataReader metadata)
    {
        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            string typeName = $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";
            yield return typeName;
            foreach (MethodDefinitionHandle method in type.GetMethods())
                yield return $"{typeName}.{metadata.GetString(metadata.GetMethodDefinition(method).Name)}";
            foreach (PropertyDefinitionHandle property in type.GetProperties())
                yield return $"{typeName}.{metadata.GetString(metadata.GetPropertyDefinition(property).Name)}";
            foreach (FieldDefinitionHandle field in type.GetFields())
                yield return $"{typeName}.{metadata.GetString(metadata.GetFieldDefinition(field).Name)}";
        }
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
