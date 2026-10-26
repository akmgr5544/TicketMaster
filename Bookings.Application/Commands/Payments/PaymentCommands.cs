using Bookings.Domain.Abstractions;
using MediatR;

namespace Bookings.Application.Commands.Payments;

public record ConfirmBookingCommand(long BookingId) : IRequest, ITransactionalRequest;

public record ReleaseUnpaidBookingCommand(long BookingId) : IRequest, ITransactionalRequest;

public record CompleteRefundCommand(long BookingId, Guid? RefundId = null) : IRequest, ITransactionalRequest;

public record FailRefundCommand(long BookingId, Guid? RefundId = null) : IRequest, ITransactionalRequest;
