using Marshal.Abstractions.Suggestions;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Suggestions;

/// <summary>Fails startup with the first invalid <c>suggestions</c> key.</summary>
public sealed class SuggestionOptionsValidator : IValidateOptions<SuggestionOptions>
{
    public ValidateOptionsResult Validate(string? name, SuggestionOptions options) =>
        options.Validate() is { } error ? ValidateOptionsResult.Fail(error) : ValidateOptionsResult.Success;
}
