using MediatR;

namespace Events.Application.Commands;

public record UpdateVenueCommand(string Id,
    string Name,
    string Address,
    double Latitude,
    double Longitude) : IRequest;
