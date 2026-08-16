using System.Text.Json;
using System.Text.Json.Serialization;
using Events.Domain.ValueObjects;

namespace Events.Cosmos.Serialization;

/// <summary>
/// Maps <see cref="GeoLocation"/> to and from a GeoJSON Point, which is the shape Cosmos indexes
/// and the shape ST_DISTANCE expects.
/// <para>
/// GeoJSON orders coordinates <c>[longitude, latitude]</c> — the reverse of how they are written
/// and spoken. Swapping them produces coordinates that are silently valid but in the wrong place,
/// so this converter is the single point where the order is decided.
/// </para>
/// </summary>
internal sealed class GeoLocationConverter : JsonConverter<GeoLocation>
{
    private const string PointType = "Point";

    public override GeoLocation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected a GeoJSON object for {nameof(GeoLocation)}, found {reader.TokenType}");

        string? type = null;
        double[]? coordinates = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "type":
                    type = reader.GetString();
                    break;
                case "coordinates":
                    coordinates = ReadCoordinates(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (type is not PointType)
            throw new JsonException($"Expected a GeoJSON {PointType}, found '{type}'");

        if (coordinates is not { Length: >= 2 })
            throw new JsonException("A GeoJSON Point needs at least two coordinates");

        return new GeoLocation(latitude: coordinates[1], longitude: coordinates[0]);
    }

    public override void Write(Utf8JsonWriter writer, GeoLocation value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", PointType);
        writer.WritePropertyName("coordinates");
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Longitude);
        writer.WriteNumberValue(value.Latitude);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static double[] ReadCoordinates(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected a coordinate array, found {reader.TokenType}");

        var coordinates = new List<double>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            coordinates.Add(reader.GetDouble());

        return [..coordinates];
    }
}
