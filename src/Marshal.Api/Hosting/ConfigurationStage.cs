using System.Collections;
using Marshal.Abstractions;
using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Skills;
using Marshal.Abstractions.Templates;
using Marshal.Api.Configuration;
using Marshal.Core.Configuration;
using Marshal.Core.Guard;
using Marshal.Core.Satellites;
using Marshal.Core.Secrets;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Hosting;

/// <summary>
/// First startup stage: every check that needs neither the database nor a resolved secret. Runs after the app is
/// built and before the host starts (hosted services read options values while they are constructed), and collects
/// every error instead of stopping at the first one. The OpenAPI export builds the app without starting it and skips it.
/// </summary>
internal static class ConfigurationStage
{
    /// <summary>Throws <see cref="StartupFailedException"/> listing every configuration error.</summary>
    public static void Ensure(WebApplication app, IDictionary environment)
    {
        IReadOnlyList<StartupError> errors = Check(app.Services, environment);
        if (errors.Count > 0)
            throw new StartupFailedException(errors, StartupReport.IsVerbose(app.Configuration));
    }

    public static IReadOnlyList<StartupError> Check(IServiceProvider services, IDictionary environment)
    {
        List<StartupError> errors = [.. Options(services, environment)];

        // Reading an invalid options value throws, so secrets set in configuration are only checked on valid options.
        if (errors.Count == 0)
            errors.AddRange(ConfiguredSecrets(services, environment));

        errors.AddRange(MainFileSecrets(services));
        errors.AddRange(ConfigFile<RiskRulesFile>(services));
        errors.AddRange(ConfigFile<SatellitesFile>(services));
        errors.AddRange(CatalogSecrets(services));
        return errors;
    }

    // IStartupValidator validates every options type registered with ValidateOnStart and aggregates the failures.
    private static IEnumerable<StartupError> Options(IServiceProvider services, IDictionary environment)
    {
        try
        {
            services.GetService<IStartupValidator>()?.Validate();
            return [];
        }
        catch (OptionsValidationException failure)
        {
            return Locate(failure, environment);
        }
        catch (AggregateException aggregate) when (aggregate.InnerExceptions.All(inner => inner is OptionsValidationException))
        {
            return [.. aggregate.InnerExceptions.Cast<OptionsValidationException>().SelectMany(failure => Locate(failure, environment))];
        }
    }

    private static IEnumerable<StartupError> Locate(OptionsValidationException failure, IDictionary environment) =>
        failure.Failures.Select(message => ConfigurationErrors.Locate(message, environment));

    private static IEnumerable<StartupError> MainFileSecrets(IServiceProvider services)
    {
        string path = Path.Combine(services.GetRequiredService<IOptions<PathsOptions>>().Value.Config, MarshalConfiguration.MainFile);
        if (!File.Exists(path))
            return [];

        Result<IReadOnlyList<ConfigSecretFinding>> scan = services.GetRequiredService<IConfigSecretScanner>().Scan(MarshalConfiguration.MainFile, File.ReadAllText(path));
        if (scan.IsFailure)
            return [FromFileMessage(MarshalConfiguration.MainFile, scan.Error.Message)];

        return scan.Value.Select(finding => new StartupError(finding.File, finding.KeyPath, finding.Problem));
    }

    private static IEnumerable<StartupError> ConfigFile<T>(IServiceProvider services)
        where T : class, new()
    {
        Result<YamlConfigFile<T>> loaded = services.GetRequiredService<Result<YamlConfigFile<T>>>();
        return loaded.IsSuccess ? [] : [FromFileMessage(null, loaded.Error.Message)];
    }

    // Invalid server, skill and template files are skipped at runtime; only a literal secret in one stops startup.
    private static IEnumerable<StartupError> CatalogSecrets(IServiceProvider services) =>
        services.GetRequiredService<IMcpServerCatalog>().Errors
            .Concat(services.GetRequiredService<ISkillCatalog>().Errors)
            .Concat(services.GetRequiredService<ITemplateCatalog>().Errors)
            .Where(error => error.LiteralSecret)
            .Select(error => FromFileMessage(error.File, error.Message));

    private static IEnumerable<StartupError> ConfiguredSecrets(IServiceProvider services, IDictionary environment) =>
        SecretBoundOptions.All(services)
            .SelectMany(bound => SecretBinder.FindConfigured(bound.Options, bound.Path))
            .Select(path => ConfigurationErrors.Locate($"{path} must not be set in configuration. Store the value as a secret and reference it by name.", environment));

    // Config file errors start with "<file>: "; the file becomes the source.
    private static StartupError FromFileMessage(string? file, string message)
    {
        int separator = message.IndexOf(": ", StringComparison.Ordinal);
        string prefix = separator < 0 ? "" : message[..separator];
        if (separator > 0 && !prefix.Contains(' ', StringComparison.Ordinal) && (file is null || prefix == file))
            return new StartupError(prefix, null, message[(separator + 2)..]);

        return new StartupError(file ?? MarshalConfiguration.MainFile, null, message);
    }
}
