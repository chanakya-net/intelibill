using System.Text.Json;
using System.Text.Json.Serialization;

namespace HsnFinder.Api.Models.Agent;

public sealed class HsnStringOrArrayConverter : JsonConverter<string[]>
{
    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new[] { reader.GetString() ?? string.Empty };
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var values = new List<string>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return values.ToArray();
                }

                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException("Expected string elements in HSN array.");
                }

                values.Add(reader.GetString() ?? string.Empty);
            }
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        throw new JsonException("Expected HSN value as a string or an array of strings.");
    }

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }
}
