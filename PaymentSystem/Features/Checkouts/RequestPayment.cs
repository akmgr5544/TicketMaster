using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Messaging;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentSystem.Features.Checkouts;

public static class RequestPayment
{
    public const string BookingCancelledCode = "booking_cancelled";

    public sealed record Command(long BookingId, Guid BuyerId, Guid SellerId, decimal Amount, string Currency)
        : IRequest<Result<Response>>, ITransactionalRequest;

    // AlreadyRequested: a redelivery, answered with the checkout the first delivery created.
    public sealed record Response(Guid CheckoutId, bool AlreadyRequested);

    internal sealed class Handler(PaymentDbContext context, IIntegrationEventPublisher publisher)
        : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (await StoredAnswerAsync(request.BookingId, cancellationToken) is { } stored)
                return stored;

            PaymentEvent checkout;
            BookingClaim claim;
            try
            {
                checkout = PaymentEvent.Create(Guid.CreateVersion7(), request.BookingId, request.BuyerId);
                checkout.AddOrder(request.SellerId, request.Amount, request.Currency);
                claim = BookingClaim.Requested(request.BookingId);
            }
            catch (PaymentDomainException exception)
            {
                return Error.BadRequest("payment_request_refused", exception.Message);
            }

            context.BookingClaims.Add(claim);
            context.PaymentEvents.Add(checkout);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another delivery or a cancellation claimed the booking after the check above; this insert
                // waited for it to commit. EF saved inside a savepoint, so the transaction is still usable.
                context.ChangeTracker.Clear();
                if (await StoredAnswerAsync(request.BookingId, cancellationToken) is { } winner)
                    return winner;
                throw;
            }

            // In the transaction that inserted the checkout, so there is never a checkout without its timer; the
            // redelivery and race paths above return before it, so there is never a second one.
            await publisher.ScheduleAsync(new CheckoutExpiryDue(checkout.CheckoutId), ExpireCheckout.After,
                cancellationToken);
            return new Response(checkout.CheckoutId, AlreadyRequested: false);
        }

        // The checkout is looked for before the claim: one created before claims existed has none.
        private async Task<Result<Response>?> StoredAnswerAsync(long bookingId, CancellationToken cancellationToken)
        {
            var existing = await context.PaymentEvents
                .Where(e => e.BookingId == bookingId)
                .Select(e => (Guid?)e.CheckoutId)
                .SingleOrDefaultAsync(cancellationToken);
            if (existing is { } checkoutId)
                return new Response(checkoutId, AlreadyRequested: true);

            if (await context.BookingClaims.AnyAsync(
                    c => c.BookingId == bookingId && c.Status == BookingClaimStatus.Cancelled, cancellationToken))
                return Error.Conflict(BookingCancelledCode,
                    $"Booking {bookingId} was cancelled before its payment was requested.");

            return null;
        }
    }
}

public sealed class PaymentRequestedConsumer(ISender sender, IIntegrationEventPublisher publisher)
{
    public async Task Consume(PaymentRequestedIntegrationEvent message, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RequestPayment.Command(message.BookingId, message.BuyerId,
            message.SellerId, message.Amount, message.Currency), cancellationToken);
        // A cancelled booking already released its seats in Bookings; there is nothing to tell it.
        if (result.IsSuccess || result.Error!.Code == RequestPayment.BookingCancelledCode)
            return;

        // A request the domain refuses fails the same way on every retry, so it is acknowledged rather than
        // thrown back at the queue — and the booking is told, because nothing else releases its seats.
        // Published after the command's transaction rolled back, so it is its own unit of work.
        await publisher.PublishAsync(new BookingPaymentFailedIntegrationEvent(message.BookingId), cancellationToken);
    }
}
