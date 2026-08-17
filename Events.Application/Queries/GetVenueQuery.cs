using Events.Application.Dtos;
using MediatR;

namespace Events.Application.Queries;

public record GetVenueQuery(string Id) : IRequest<VenueDto>;
