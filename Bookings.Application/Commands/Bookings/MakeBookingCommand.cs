using Bookings.Domain.Abstractions;
using MediatR;

namespace Bookings.Application.Commands.Bookings;

/// <summary>
/// Returns the id of the booking it created, so the endpoint can answer 201 with a location the
/// caller can read back.
/// </summary>
public record MakeBookingCommand(string UserId,
    string EventId,
    long[] Tickets) : IRequest<long>, ITransactionalRequest;
