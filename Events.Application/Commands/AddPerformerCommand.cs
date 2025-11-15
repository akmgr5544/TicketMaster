using MediatR;

namespace Events.Application.Commands;

public record AddPerformerCommand() : IRequest;