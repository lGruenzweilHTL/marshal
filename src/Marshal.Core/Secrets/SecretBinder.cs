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
    /// <summary>
    /// Paths of <c>[Secret]</c> properties that already hold a value, i.e. came from configuration. Needs no secret
    /// store, so startup checks it together with the other configuration errors.
    /// </summary>
    public static IReadOnlyList<string> FindConfigured(object options, string path) =>
        [.. SecretProperties(options, path).Where(found => found.Property.GetValue(found.Node) is not null).Select(found => found.Path)];

    public async Task<SecretBindingReport> BindAsync(object options, string path, CancellationToken ct)
    {
        var missingRequired = new List<string>();
        var missingOptional = new List<string>();
        var boundFromConfiguration = new List<string>();
        foreach (SecretProperty found in SecretProperties(options, path))
            await BindPropertyAsync(found, missingRequired, missingOptional, boundFromConfiguration, ct);

        return new SecretBindingReport(missingRequired, missingOptional, boundFromConfiguration);
    }

    private static List<SecretProperty> SecretProperties(object options, string path)
    {
        var found = new List<SecretProperty>();
        Visit(options, path, found);
        return found;
    }

    private static void Visit(object? node, string path, List<SecretProperty> found)
    {
        switch (node)
        {
            case null or string:
                return;
            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                    Visit(entry.Value, Join(path, entry.Key.ToString() ?? ""), found);

                return;
            case IEnumerable items:
                int index = 0;
                foreach (object? item in items)
                    Visit(item, $"{path}[{index++}]", found);

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
                found.Add(new SecretProperty(node, property, secret, path, propertyPath));
                continue;
            }

            Visit(property.GetValue(node), propertyPath, found);
        }
    }

    private async Task BindPropertyAsync(
        SecretProperty found, List<string> missingRequired, List<string> missingOptional, List<string> bound, CancellationToken ct)
    {
        (object node, PropertyInfo property, SecretAttribute secret, string parentPath, string propertyPath) = found;
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

    private sealed record SecretProperty(object Node, PropertyInfo Property, SecretAttribute Secret, string ParentPath, string Path);
}
