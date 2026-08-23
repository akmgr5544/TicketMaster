using Bookings.Domain.Abstractions;
using MediatR;

namespace Bookings.Application.Commands;

public record ConfirmBookingPaymentCommand(long BookingId) : IRequest, ITransactionalRequest;

public record ReleaseUnpaidBookingCommand(long BookingId) : IRequest, ITransactionalRequest;
