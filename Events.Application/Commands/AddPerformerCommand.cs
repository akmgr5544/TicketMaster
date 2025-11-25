using MediatR;

namespace Events.Application.Commands;

public record AddPerformerCommand(string Name, string Description) : IRequest;