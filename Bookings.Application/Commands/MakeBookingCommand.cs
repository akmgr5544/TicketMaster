using Bookings.Domain.Enums;
using MediatR;

namespace Bookings.Application.Commands;

public record MakeBookingCommand(string UserId,
    string EventId,
    BookingStatus Status,
    long[] Tickets) : IRequest;