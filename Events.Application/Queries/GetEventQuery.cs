using Events.Application.Dtos;
using MediatR;

namespace Events.Application.Queries;

public record GetEventQuery(string Id) : IRequest<EventDto>;
