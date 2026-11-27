using System.Text.Json;
using System.Text.Json.Serialization;
using Intelibill.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Intelibill.Infrastructure.Data.Json;

/// <summary>
/// The stored form of issued documents and frozen seller profiles: camel-case JSON with enum names,
/// so a stored copy stays readable if an enum's numbering ever changes. Rows are written once.
/// </summary>
internal static class InvoiceDocumentJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static ValueConverter<T?, string?> Converter<T>()
        where T : class =>
        new(
            value => value == null ? null : JsonSerializer.Serialize(value, Options),
            json => json == null ? null : JsonSerializer.Deserialize<T>(json, Options));

    /// <summary>Compares by stored form: the records hold lists, whose equality is by reference.</summary>
    internal static ValueComparer<T?> Comparer<T>()
        where T : class =>
        new(
            (left, right) => Serialize(left) == Serialize(right),
            value => Serialize(value).GetHashCode(StringComparison.Ordinal),
            value => value == null ? null : JsonSerializer.Deserialize<T>(Serialize(value), Options));

    private static string Serialize<T>(T? value)
        where T : class =>
        value == null ? string.Empty : JsonSerializer.Serialize(value, Options);
}
