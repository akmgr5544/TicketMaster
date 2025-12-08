using Events.Domain.Entities;
using MongoDB.Bson.Serialization;

namespace Events.Mongo.Configurations;

public static class PerformerConfiguration
{
    public static void PerformerConfig()
    {
        BsonClassMap.RegisterClassMap<Performer>(map => { map.AutoMap(); });
    }
}