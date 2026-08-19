using MediatR;

namespace Events.Application.Commands;

/// <summary>Returns the id of the created performer so the caller can address it.</summary>
public record AddPerformerCommand(string Name, string Description) : IRequest<string>;
