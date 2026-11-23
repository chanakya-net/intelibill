using System.Text.Json;
using System.Text.Json.Serialization;
using Intelibill.Application.Common.Normalization;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

/// <summary>Opaque until the receipt commits. Unknown versions and malformed claims remain evidence.</summary>
[JsonConverter(typeof(FrozenLicenceEvidence.Converter))]
public sealed class FrozenLicenceEvidence(JsonElement value)
{
    internal int? Version => Field("version") is { ValueKind: JsonValueKind.Number } version && version.TryGetInt32(out var n) ? n : null;
    internal string? Text(string name) => Field(name) is { ValueKind: JsonValueKind.String } field ? field.GetString() : null;
    internal Guid? Id(string name) => Guid.TryParseExact(Text(name), "D", out var id) ? id : null;
    private JsonElement? Field(string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) ? field : null;
    public override string ToString() => "Frozen offline licence evidence";

    internal bool IsStorable
    {
        get
        {
            try { return Storable(value); }
            catch (InvalidOperationException) { return false; } // An escaped unpaired surrogate cannot be decoded.
        }
    }

    private static bool Storable(JsonElement node) => node.ValueKind switch
    {
        JsonValueKind.Object => StorableObject(node),
        JsonValueKind.Array => node.EnumerateArray().All(Storable),
        JsonValueKind.String => StorableText.IsStorable(node.GetString()),
        _ => true,
    };

    private static bool StorableObject(JsonElement node)
    {
        // jsonb collapses duplicate keys, so it cannot preserve such an envelope.
        var names = new HashSet<string>(StringComparer.Ordinal);
        return node.EnumerateObject().All(p => names.Add(p.Name) && StorableText.IsStorable(p.Name) && Storable(p.Value));
    }

    public sealed class Converter : JsonConverter<FrozenLicenceEvidence>
    {
        public override FrozenLicenceEvidence Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonElement.ParseValue(ref reader));

        public override void Write(Utf8JsonWriter writer, FrozenLicenceEvidence evidence, JsonSerializerOptions options) =>
            evidence.WriteTo(writer);
    }

    // jsonb may reorder properties. Stable ordering keeps replay and a device resend on the same hash.
    private void WriteTo(Utf8JsonWriter writer) => WriteSorted(writer, value);

    private static void WriteSorted(Utf8JsonWriter writer, JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in node.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteSorted(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in node.EnumerateArray()) WriteSorted(writer, item);
            writer.WriteEndArray();
        }
        else node.WriteTo(writer);
    }
}
