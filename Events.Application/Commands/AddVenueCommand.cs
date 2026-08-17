using MediatR;

namespace Events.Application.Commands;

/// <summary>Returns the id of the created venue so the caller can address it.</summary>
public record AddVenueCommand(string Name,
    string Address,
    double Latitude,
    double Longitude,
    string[] Seats) : IRequest<string>;
