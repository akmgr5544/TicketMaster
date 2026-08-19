using MediatR;

namespace Events.Application.Commands;

public record DeletePerformerCommand(string Id) : IRequest;
