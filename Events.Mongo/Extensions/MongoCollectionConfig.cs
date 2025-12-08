using Events.Mongo.Configurations;

namespace Events.Mongo.Extensions;

internal static class MongoCollectionConfig
{
    public static void AddMongoCollectionConfig()
    {
        EventConfiguration.EventConfig();
        PerformerConfiguration.PerformerConfig();
        VenueConfiguration.VenueConfig();
    }
}