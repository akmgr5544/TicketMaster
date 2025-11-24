using System.Drawing;
using MongoDB.Bson;

namespace Events.Domain.Entities;

public class Venue
{
    public ObjectId Id { get; set; }
    public string Name { get; set; }
    public string Address { get; set; }
    public Point Location { get; set; }
}