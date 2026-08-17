using MediatR;

namespace Events.Application.Commands;

public record DeleteVenueCommand(string Id) : IRequest;
