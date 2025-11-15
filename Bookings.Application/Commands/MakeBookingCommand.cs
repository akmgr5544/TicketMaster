using MediatR;

namespace Bookings.Application.Commands;

public record MakeBookingCommand() : IRequest;