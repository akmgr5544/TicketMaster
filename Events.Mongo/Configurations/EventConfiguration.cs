using Events.Domain.Entities;
using MongoDB.Bson.Serialization;

namespace Events.Mongo.Configurations;

public static class EventConfiguration
{
    public static void EventConfig()
    {
        BsonClassMap.RegisterClassMap<Event>(map =>
        {

        });
    }
}