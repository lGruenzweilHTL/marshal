using System.ComponentModel;
using System.Globalization;

namespace Marshal.Abstractions;

/// <summary>
/// Converts a strongly typed id from and to its string form (used by frameworks that key by string, e.g. Identity).
/// </summary>
/// <typeparam name="TId">The id type.</typeparam>
public sealed class GuidIdTypeConverter<TId> : TypeConverter
    where TId : struct, IGuidId<TId>
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text ? TId.Parse(text, CultureInfo.InvariantCulture) : base.ConvertFrom(context, culture, value);
}
