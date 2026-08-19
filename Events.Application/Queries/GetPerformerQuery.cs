using Events.Application.Dtos;
using MediatR;

namespace Events.Application.Queries;

public record GetPerformerQuery(string Id) : IRequest<PerformerDto>;
