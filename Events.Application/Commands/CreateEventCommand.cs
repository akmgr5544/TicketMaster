using MediatR;

namespace Events.Application.Commands;

public record CreateEventCommand() : IRequest;