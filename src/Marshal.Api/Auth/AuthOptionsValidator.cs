using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Auth;

/// <summary>
/// Validates <c>auth.providers</c>; messages name the offending key.
/// </summary>
internal sealed partial class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var errors = new List<string>();
        for (int index = 0; index < options.Providers.Count; index++)
        {
            LoginProviderOptions provider = options.Providers[index];
            string path = $"auth.providers[{index}]";
            if (!NamePattern().IsMatch(provider.Name))
                errors.Add($"{path}.name must be lowercase letters, digits or '-'.");

            switch (provider.Type)
            {
                case LoginProviderOptions.LocalType:
                    break;
                case LoginProviderOptions.OidcType:
                    ValidateOidc(provider, path, errors);
                    break;
                default:
                    errors.Add($"{path}.type must be 'oidc' or 'local'.");
                    break;
            }
        }

        foreach (string duplicate in options.Providers.GroupBy(provider => provider.Name).Where(group => group.Count() > 1).Select(group => group.Key))
            errors.Add($"auth.providers uses the name '{duplicate}' more than once.");
        if (options.Providers.Count(provider => provider.Type == LoginProviderOptions.LocalType) > 1)
            errors.Add("auth.providers allows only one 'local' provider.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateOidc(LoginProviderOptions provider, string path, List<string> errors)
    {
        if (provider.Authority is null || !provider.Authority.IsAbsoluteUri)
            errors.Add($"{path}.authority must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(provider.ClientId))
            errors.Add($"{path}.clientId is required.");
        if (string.IsNullOrWhiteSpace(provider.ClientCredentialSecret))
            errors.Add($"{path}.clientCredentialSecret is required.");
        if (provider.RoleMappings.Count == 0)
            errors.Add($"{path}.roleMappings needs at least one group.");
        if (provider.RoleMappings.Values.Any(role => role is not (Roles.Admin or Roles.User)))
            errors.Add($"{path}.roleMappings values must be 'admin' or 'user'.");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex NamePattern();
}
