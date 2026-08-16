using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Events.Cosmos.Serialization;

/// <summary>
/// The one serializer configuration the CosmosClient is built with. Exposed so tests exercise the
/// same options the service does rather than an approximation of them.
/// </summary>
public static class CosmosJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        // Cosmos requires the identity property to be named exactly "id"; camelCase gets us there
        // from Id without an attribute in the domain.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { DomainBinding.Apply }
        },
        Converters = { new GeoLocationConverter() }
    };
}
