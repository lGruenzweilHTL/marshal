using System.Collections;
using System.Text.RegularExpressions;
using Marshal.Api.Configuration;

namespace Marshal.Api.Tests.Architecture;

/// <summary>
/// Startup names the file and key of every invalid option by reading the key path at the start of the validation
/// message (<c>embedding.model is required.</c>). Every options validation message in Marshal's sources must start
/// with a full key path, or with an interpolated one (<c>{path}.model is required.</c>).
/// </summary>
public sealed partial class ValidationMessageKeyTests
{
    [Fact]
    public void ValidationMessages_StartWithKeyPath()
    {
        string source = Path.Combine(NoMailSendTests.RepositoryRoot(), "src");
        List<string> found = [];
        foreach (string file in Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            // The executor host is a separate service with its own startup.
            if (file.Contains("Marshal.ExecutorHost", StringComparison.Ordinal) || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            string text = File.ReadAllText(file);
            IEnumerable<Match> messages = ValidateCall().Matches(text).Concat(AttributeMessage().Matches(text));
            if (text.Contains("IValidateOptions<", StringComparison.Ordinal))
                messages = messages.Concat(ValidatorFailure().Matches(text));

            found.AddRange(messages
                .Select(match => match.Groups["message"].Value)
                .Where(message => !message.StartsWith('{') && ConfigurationErrors.Locate(message, new Hashtable()).Key is null)
                .Select(message => $"{Path.GetRelativePath(source, file)}: \"{message}\""));
        }

        Assert.Empty(found);
    }

    [GeneratedRegex(@"\.Validate\(options\s*=>.*?,\s*\$?""(?<message>(?:[^""\\]|\\.)*)""\s*\)", RegexOptions.Singleline)]
    private static partial Regex ValidateCall();

    [GeneratedRegex(@"ErrorMessage\s*=\s*""(?<message>(?:[^""\\]|\\.)*)""")]
    private static partial Regex AttributeMessage();

    [GeneratedRegex(@"\.Add\(\$?""(?<message>(?:[^""\\]|\\.)*)""")]
    private static partial Regex ValidatorFailure();
}
