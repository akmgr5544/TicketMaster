using MongoDB.Bson;

namespace Events.Domain.Entities;

public class Performer
{
    public Performer(string name, string description)
    {
        Name = name;
        Description = description;
    }
    public ObjectId Id { get; set; }
    public string Name { get; init; }
    public string Description { get; init; }
}