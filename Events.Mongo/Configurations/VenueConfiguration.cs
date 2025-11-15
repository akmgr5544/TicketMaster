using Events.Domain.Entities;
using MongoDB.Bson.Serialization;

namespace Events.Mongo.Configurations;

public static class VenueConfiguration
{
    public static void VenueConfig()
    {
        BsonClassMap.RegisterClassMap<Venue>(map =>
        {

        });
    }
}