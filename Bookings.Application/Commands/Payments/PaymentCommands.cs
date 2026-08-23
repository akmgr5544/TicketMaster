using Bookings.Domain.Abstractions;
using MediatR;

namespace Bookings.Application.Commands.Payments;

public record ConfirmBookingCommand(long BookingId) : IRequest, ITransactionalRequest;

public record ReleaseUnpaidBookingCommand(long BookingId) : IRequest, ITransactionalRequest;
