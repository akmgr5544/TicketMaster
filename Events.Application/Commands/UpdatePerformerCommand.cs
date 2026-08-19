using MediatR;

namespace Events.Application.Commands;

public record UpdatePerformerCommand(string Id,
    string Name,
    string Description) : IRequest;
