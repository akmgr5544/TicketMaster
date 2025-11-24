using Events.Application.Dtos;
using MediatR;

namespace Events.Application.Commands;

public abstract record CreateEventCommand(DateTime StartDate,
    string Venue,
    List<string> Performers) : IRequest;