using System.Drawing;

namespace Events.Application.Dtos;

public record VenueDto(string Id,
    string Name,
    string Address,
    Point Location);