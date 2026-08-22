using Bookings.Domain.Abstractions;
using MediatR;

namespace Bookings.Application.Payments;

/// <summary>
/// Commands that settle a booking once the payment service says what happened to its money.
/// <para>
/// Commands and their handlers share this namespace because <c>ColocationTest</c> requires it.
/// </para>
/// </summary>
public record ConfirmBookingPaymentCommand(long BookingId) : IRequest, ITransactionalRequest;

/// <summary>
/// The payment did not happen, so the booking is void and its seats go back on sale.
/// </summary>
public record ReleaseUnpaidBookingCommand(long BookingId) : IRequest, ITransactionalRequest;
