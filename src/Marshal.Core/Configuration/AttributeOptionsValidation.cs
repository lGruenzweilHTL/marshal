using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Configuration;

/// <summary>
/// Data annotation validation that reports each attribute's own message. The messages name the full key
/// (<c>agent.maxSteps must be between 1 and 50.</c>); <c>ValidateDataAnnotations</c> wraps them in its own text,
/// which hides the key from startup reporting.
/// </summary>
public static class AttributeOptionsValidation
{
    public static OptionsBuilder<T> ValidateAttributes<T>(this OptionsBuilder<T> builder)
        where T : class
    {
        builder.Services.AddSingleton<IValidateOptions<T>>(new AttributeOptionsValidator<T>(builder.Name));
        return builder;
    }

    private sealed class AttributeOptionsValidator<T>(string? name) : IValidateOptions<T>
        where T : class
    {
        public ValidateOptionsResult Validate(string? optionsName, T options)
        {
            if (name is not null && name != optionsName)
                return ValidateOptionsResult.Skip;

            var results = new List<ValidationResult>();
            if (Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true))
                return ValidateOptionsResult.Success;

            return ValidateOptionsResult.Fail(results.Select(result => result.ErrorMessage ?? $"{typeof(T).Name} is invalid."));
        }
    }
}
