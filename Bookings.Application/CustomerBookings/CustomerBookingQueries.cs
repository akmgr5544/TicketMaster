using Bookings.Domain.Abstractions;
using MediatR;

namespace Bookings.Application.CustomerBookings;

/// <summary>
/// A caller reading and cancelling their own bookings.
/// <para>
/// Every request here carries the caller's id and is scoped by it. That scoping is the authorization
/// check — the gateway establishes <i>who</i> you are, and whether a particular booking is yours is a
/// question only this service can answer, so it answers it here rather than trusting the caller to
/// ask about their own.
/// </para>
/// <para>
/// Commands and their handlers share this namespace because <c>ColocationTest</c> requires it.
/// </para>
/// </summary>
public record GetBookingQuery(long BookingId, string UserId) : IRequest<BookingDto>;

/// <summary>
/// Ordered by id descending, newest first — <c>Booking</c> has no timestamp of any kind, so the key
/// is the only proxy for age available. Paged by offset, which Postgres handles cheaply, unlike the
/// cursor paging the Cosmos-backed Events service needs.
/// </summary>
public record ListBookingsQuery(string UserId, int Page, int PageSize) : IRequest<BookingDto[]>;

/// <summary>
/// A customer cancelling a booking they have not paid for. Distinct from
/// <c>ReleaseUnpaidBookingCommand</c>, which the payment service's failure message drives and which
/// deliberately has no owner check — that one is not reachable from an endpoint.
/// </summary>
public record CancelBookingCommand(long BookingId, string UserId) : IRequest, ITransactionalRequest;
