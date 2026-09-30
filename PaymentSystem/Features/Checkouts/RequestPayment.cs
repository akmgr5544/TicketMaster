using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Shared.Messaging;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentSystem.Features.Checkouts;

public static class RequestPayment
{
    public sealed record Command(long BookingId, Guid BuyerId, Guid SellerId, decimal Amount, string Currency)
        : IRequest<Result<Response>>, ITransactionalRequest;

    // AlreadyRequested: a redelivery, answered with the checkout the first delivery created.
    public sealed record Response(Guid CheckoutId, bool AlreadyRequested);

    internal sealed class Handler(PaymentDbContext context, IIntegrationEventPublisher publisher)
        : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (await ExistingCheckoutAsync(request.BookingId, cancellationToken) is { } existing)
                return new Response(existing, AlreadyRequested: true);

            PaymentEvent checkout;
            try
            {
                checkout = PaymentEvent.Create(Guid.CreateVersion7(), request.BookingId, request.BuyerId,
                    [new PaymentOrderLine(request.SellerId, request.Amount, request.Currency)]);
            }
            catch (PaymentDomainException exception)
            {
                return Error.BadRequest("payment_request_refused", exception.Message);
            }

            context.PaymentEvents.Add(checkout);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A concurrent delivery of the same booking committed between the check above and this
                // insert. EF saved inside a savepoint, so the transaction is still usable for the re-read.
                context.ChangeTracker.Clear();
                if (await ExistingCheckoutAsync(request.BookingId, cancellationToken) is { } winner)
                    return new Response(winner, AlreadyRequested: true);
                throw;
            }

            // In the transaction that inserted the checkout, so there is never a checkout without its timer; the
            // redelivery and race paths above return before it, so there is never a second one.
            await publisher.ScheduleAsync(new CheckoutExpiryDue(checkout.CheckoutId), ExpireCheckout.After,
                cancellationToken);
            return new Response(checkout.CheckoutId, AlreadyRequested: false);
        }

        private Task<Guid?> ExistingCheckoutAsync(long bookingId, CancellationToken cancellationToken) =>
            context.PaymentEvents
                .Where(e => e.BookingId == bookingId)
                .Select(e => (Guid?)e.CheckoutId)
                .SingleOrDefaultAsync(cancellationToken);
    }
}

public sealed class PaymentRequestedConsumer(ISender sender, IIntegrationEventPublisher publisher)
{
    public async Task Consume(PaymentRequestedIntegrationEvent message, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RequestPayment.Command(message.BookingId, message.BuyerId,
            message.SellerId, message.Amount, message.Currency), cancellationToken);
        if (result.IsSuccess)
            return;

        // A request the domain refuses fails the same way on every retry, so it is acknowledged rather than
        // thrown back at the queue — and the booking is told, because nothing else releases its seats.
        // Published after the command's transaction rolled back, so it is its own unit of work.
        await publisher.PublishAsync(new BookingPaymentFailedIntegrationEvent(message.BookingId), cancellationToken);
    }
}
