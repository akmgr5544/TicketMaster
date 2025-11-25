using System.Drawing;
using MongoDB.Bson;

namespace Events.Domain.Entities;

public class Venue
{
    public Venue(string name, string address, Point location)
    {
        Name = name;
        Address = address;
        Location = location;
    }
    public ObjectId Id { get; set; }
    public string Name { get; set; }
    public string Address { get; set; }
    public Point Location { get; set; }
}