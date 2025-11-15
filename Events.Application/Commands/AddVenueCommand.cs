using MediatR;

namespace Events.Application.Commands;

public record AddVenueCommand() : IRequest;