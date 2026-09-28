using System.Collections;
using System.Reflection;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Secrets;

namespace Marshal.Core.Secrets;

/// <summary>
/// Fills <c>[Secret]</c> options properties from <see cref="ISecretStore"/> and reports unresolved references.
/// A value already present on such a property came from configuration and is rejected.
/// </summary>
public sealed class SecretBinder(ISecretStore store)
{
    public async Task<SecretBindingReport> BindAsync(object options, string path, CancellationToken ct)
    {
        var missingRequired = new List<string>();
        var missingOptional = new List<string>();
        var boundFromConfiguration = new List<string>();
        await VisitAsync(options, path, missingRequired, missingOptional, boundFromConfiguration, ct);
        return new SecretBindingReport(missingRequired, missingOptional, boundFromConfiguration);
    }

    private async Task VisitAsync(object? node, string path, List<string> missingRequired, List<string> missingOptional, List<string> bound, CancellationToken ct)
    {
        switch (node)
        {
            case null or string:
                return;
            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                    await VisitAsync(entry.Value, Join(path, entry.Key.ToString() ?? ""), missingRequired, missingOptional, bound, ct);

                return;
            case IEnumerable items:
                int index = 0;
                foreach (object? item in items)
                    await VisitAsync(item, $"{path}[{index++}]", missingRequired, missingOptional, bound, ct);

                return;
        }

        Type type = node.GetType();
        if (type.IsPrimitive || type.IsEnum || type.IsValueType || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            return;

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;

            string propertyPath = Join(path, JsonNamingPolicy.CamelCase.ConvertName(property.Name));
            if (property.GetCustomAttribute<SecretAttribute>() is { } secret)
            {
                await BindPropertyAsync(node, property, secret, path, propertyPath, missingRequired, missingOptional, bound, ct);
                continue;
            }

            await VisitAsync(property.GetValue(node), propertyPath, missingRequired, missingOptional, bound, ct);
        }
    }

    private async Task BindPropertyAsync(
        object node, PropertyInfo property, SecretAttribute secret, string parentPath, string propertyPath,
        List<string> missingRequired, List<string> missingOptional, List<string> bound, CancellationToken ct)
    {
        if (property.GetValue(node) is not null)
        {
            property.SetValue(node, null);
            bound.Add(propertyPath);
            return;
        }

        PropertyInfo reference = node.GetType().GetProperty(secret.ReferenceProperty)
            ?? throw new InvalidOperationException($"[Secret] on {node.GetType().Name}.{property.Name} names missing property {secret.ReferenceProperty}.");
        if (reference.GetValue(node) is not string name || name.Length == 0)
            return;

        Result<string> value = SecretName.IsValid(name)
            ? await store.GetAsync(SecretName.Create(name).Value, ct)
            : new Error("secret.invalid_name", "invalid", ErrorType.Validation);
        if (value.IsSuccess)
        {
            property.SetValue(node, value.Value);
            return;
        }

        string description = $"{Join(parentPath, JsonNamingPolicy.CamelCase.ConvertName(reference.Name))} ({name})";
        (secret.Required ? missingRequired : missingOptional).Add(description);
    }

    private static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";
}
