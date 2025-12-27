using MediatR;

namespace Bookings.Application.Abstractions;

public record IdentifiedCommand<T, TR>(T Command, Guid Id) : IRequest<TR>
    where T : IRequest<TR>;