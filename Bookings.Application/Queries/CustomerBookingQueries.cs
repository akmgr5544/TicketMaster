using Bookings.Application.Dtos;
using Bookings.Domain.Abstractions;
using MediatR;

namespace Bookings.Application.Queries;

public record GetBookingQuery(long BookingId, string UserId) : IRequest<BookingDto>;

public record ListBookingsQuery(string UserId, int Page, int PageSize) : IRequest<BookingDto[]>;

public record CancelBookingCommand(long BookingId, string UserId) : IRequest, ITransactionalRequest;
