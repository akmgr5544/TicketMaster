using Events.Application.Dtos;
using MediatR;

namespace Events.Application.Commands;

public record CreateEventCommand(DateTime StartDate,
    string Venue,
    List<string> Performers) : IRequest;