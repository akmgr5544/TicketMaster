using System.Drawing;

namespace Events.Domain.Entities;

public class Venue
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Address { get; set; }
    public Point Location { get; set; }
}