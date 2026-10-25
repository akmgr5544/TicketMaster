using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentSystem.Features.Checkouts;

// Bookings placed a booking: open a checkout for it, with one payment order for the seller.
public sealed class PaymentRequestedHandler(ILogger<PaymentRequestedHandler> logger)
{
    public static readonly TimeSpan CheckoutLifetime = TimeSpan.FromMinutes(15);

    public async Task<OutgoingMessages> Handle(PaymentRequestedIntegrationEvent message, PaymentDbContext context,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();

        // A redelivery: the checkout was created the first time.
        if (await context.PaymentEvents.AnyAsync(e => e.BookingId == message.BookingId, cancellationToken))
            return outgoing;

        // The cancellation got here first. Nothing is sent: Bookings released the seats when it cancelled.
        if (await context.BookingClaims.AnyAsync(c => c.BookingId == message.BookingId && c.Status == BookingClaimStatus.Cancelled, cancellationToken))
        {
            logger.LogInformation("Payment for booking {BookingId} was not opened: the booking was already cancelled.", message.BookingId);
            return outgoing;
        }

        try
        {
            var checkout = PaymentEvent.Create(message.BookingId, message.BuyerId, message.Currency);
            checkout.AddPaymentOrder(message.SellerId, message.Amount);
            context.PaymentEvents.Add(checkout);
            // Saved with the checkout: a cancellation racing this request fails on the same key, and its retry finds
            // the checkout to cancel.
            context.BookingClaims.Add(BookingClaim.Requested(message.BookingId));

            // A buyer who never pays must not hold the seats forever.
            outgoing.Add(new CheckoutExpiryDue(message.BookingId).DelayedFor(CheckoutLifetime));
        }
        // Retrying cannot fix a request the domain refuses; failing the payment lets Bookings release the seats.
        catch (PaymentDomainException exception)
        {
            logger.LogWarning("Payment for booking {BookingId} was refused: {Reason}", message.BookingId, exception.Message);
            outgoing.Add(new BookingPaymentFailedIntegrationEvent(message.BookingId));
        }

        return outgoing;
    }
}
