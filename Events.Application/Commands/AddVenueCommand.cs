using MediatR;

namespace Events.Application.Commands;

public record AddVenueCommand(string Name,
    string Address,
    double Latitude,
    double Longitude,
    string[] Seats) : IRequest;
