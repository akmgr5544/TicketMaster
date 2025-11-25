using System.Drawing;
using MediatR;

namespace Events.Application.Commands;

public record AddVenueCommand(string Id,
    string Name,
    string Address,
    Point Location) : IRequest;