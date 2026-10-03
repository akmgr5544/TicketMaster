using Bookings.Application.Dtos;
using Bookings.Domain.Abstractions;
using MediatR;

namespace Bookings.Application.Queries;

public record GetBookingQuery(long BookingId, Guid UserId) : IRequest<BookingDto>;

public record ListBookingsQuery(Guid UserId, int Page, int PageSize) : IRequest<PagedResult<BookingDto>>;

// No ticket ids: the whole booking. Some: only those seats, which only a paid booking allows.
public record CancelBookingCommand(long BookingId, Guid UserId, long[]? TicketIds = null) : IRequest, ITransactionalRequest;
