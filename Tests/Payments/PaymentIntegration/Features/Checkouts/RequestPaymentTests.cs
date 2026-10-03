using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Features.Checkouts;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentIntegration.Features.Checkouts;

public sealed class RequestPaymentTests(PaymentsFixture fixture) : MessagingTest(fixture)
{
    private static PaymentRequestedIntegrationEvent Request(long bookingId = 4242, decimal amount = 125.50m,
        string currency = "EUR", Guid? buyerId = null, Guid? sellerId = null) =>
        new(bookingId, buyerId ?? Guid.NewGuid(), sellerId ?? Guid.NewGuid(), amount, currency);

    private async Task ConsumeAsync(PaymentRequestedIntegrationEvent message)
    {
        await using var scope = NewScope();
        var consumer = new PaymentRequestedConsumer(
            scope.ServiceProvider.GetRequiredService<ISender>(),
            scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>());
        await consumer.Consume(message, CancellationToken.None);
    }

    private async Task CancelAsync(long bookingId)
    {
        await using var scope = NewScope();
        await new BookingCancelledConsumer(scope.ServiceProvider.GetRequiredService<ISender>())
            .Consume(new BookingCancelledIntegrationEvent(bookingId), CancellationToken.None);
    }

    // The cancellation overtook this request. Bookings has already released the seats, so it needs no
    // BookingPaymentFailed — and there must be no checkout a buyer could still pay.
    [Fact]
    public async Task ARequestForABookingAlreadyCancelled_OpensNoCheckout_AndPublishesNothing()
    {
        var message = Request();
        await CancelAsync(message.BookingId);

        await ConsumeAsync(message);

        Assert.Equal(0, await ReadAsync(c => c.PaymentEvents.CountAsync()));
        Assert.Empty(Outbox.Published);
        Assert.Empty(Outbox.Scheduled);
    }

    [Fact]
    public async Task ARequestForABookingAlreadyCancelled_IsAnExpectedFailure_NamedForTheCancellation()
    {
        var message = Request();
        await CancelAsync(message.BookingId);

        var result = await SendAsync(new RequestPayment.Command(message.BookingId, message.BuyerId, message.SellerId,
            message.Amount, message.Currency));

        Assert.False(result.IsSuccess);
        Assert.Equal(RequestPayment.BookingCancelledCode, result.Error!.Code);
    }

    // Both handlers claim the booking's key. The cancellation's claim is uncommitted, so the request's check
    // sees nothing; its own claim insert must wait on the key rather than open a checkout beside it.
    [Fact]
    public async Task ARequestRacingACancellation_WaitsOnTheClaim_AndOpensNoCheckout()
    {
        var message = Request();
        await using var cancelScope = NewScope();
        var cancelContext = cancelScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await using var cancelTransaction = await cancelContext.Database.BeginTransactionAsync();
        cancelContext.BookingClaims.Add(BookingClaim.Cancelled(message.BookingId));
        await cancelContext.SaveChangesAsync();

        var request = SendAsync(new RequestPayment.Command(message.BookingId, message.BuyerId, message.SellerId,
            message.Amount, message.Currency));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(request.IsCompleted, "The request should wait on the cancellation's uncommitted claim.");
        await cancelTransaction.CommitAsync();
        var result = await request;

        Assert.Equal(RequestPayment.BookingCancelledCode, result.Error!.Code);
        Assert.Equal(0, await ReadAsync(c => c.PaymentEvents.CountAsync()));
        Assert.Empty(Outbox.Published);
        Assert.Empty(Outbox.Scheduled);
    }

    [Fact]
    public async Task Consume_CreatesACheckoutWithOneUnstartedOrderForTheSeller()
    {
        var message = Request();

        await ConsumeAsync(message);

        var checkout = await ReadAsync(c => c.PaymentEvents.SingleAsync());
        Assert.Equal(message.BookingId, checkout.BookingId);
        Assert.Equal(message.BuyerId, checkout.BuyerId);
        Assert.False(checkout.IsPaymentDone);
        var order = Assert.Single(checkout.PaymentOrders);
        Assert.Equal(message.SellerId, order.MerchantId);
        Assert.Equal(message.BuyerId, order.BuyerId);
        Assert.Equal(message.Amount, order.Amount);
        Assert.Equal(message.Currency, order.Currency);
        Assert.Equal(PaymentOrderStatus.NotStarted, order.Status);
        Assert.Empty(Outbox.Published);
    }

    [Fact]
    public async Task Redelivery_IsANoOp_AndAnswersWithTheFirstCheckout()
    {
        var message = Request();
        var command = new RequestPayment.Command(message.BookingId, message.BuyerId, message.SellerId,
            message.Amount, message.Currency);

        var first = await SendAsync(command);
        var second = await SendAsync(command);
        await ConsumeAsync(message);

        Assert.True(first.IsSuccess);
        Assert.False(first.Value!.AlreadyRequested);
        Assert.True(second.IsSuccess);
        Assert.True(second.Value!.AlreadyRequested);
        Assert.Equal(first.Value.CheckoutId, second.Value.CheckoutId);
        Assert.Equal(1, await ReadAsync(c => c.PaymentEvents.CountAsync()));
        Assert.Equal(1, await ReadAsync(c => c.Set<PaymentOrder>().CountAsync()));
        Assert.Empty(Outbox.Published);
    }

    [Fact]
    public async Task Consume_SchedulesTheCheckoutsExpiry_FifteenMinutesOut_InTheTransactionThatInsertedIt()
    {
        await ConsumeAsync(Request());

        var checkout = await ReadAsync(c => c.PaymentEvents.SingleAsync());
        var scheduled = Assert.Single(Outbox.Scheduled);
        Assert.Equal(new CheckoutExpiryDue(checkout.CheckoutId), scheduled.Message);
        Assert.Equal(TimeSpan.FromMinutes(15), scheduled.Delay);
        Assert.NotNull(scheduled.TransactionId);
    }

    [Fact]
    public async Task Redelivery_SchedulesNoSecondExpiry()
    {
        var message = Request();

        await ConsumeAsync(message);
        await ConsumeAsync(message);
        await ConsumeAsync(message with { Amount = 1m });

        Assert.Single(Outbox.Scheduled);
    }

    [Fact]
    public async Task RedeliveryWithDifferentFigures_KeepsTheFirstCheckoutUnchanged()
    {
        var message = Request(amount: 10m);
        await ConsumeAsync(message);

        await ConsumeAsync(message with { Amount = 99m, SellerId = Guid.NewGuid() });

        var order = Assert.Single(await ReadAsync(c => c.Set<PaymentOrder>().ToListAsync()));
        Assert.Equal(10m, order.Amount);
        Assert.Equal(message.SellerId, order.MerchantId);
    }

    [Fact]
    public async Task ConcurrentDelivery_LosesOnTheClaim_AndSucceedsWithTheWinnersCheckout()
    {
        var message = Request();
        var winner = CheckoutSeed.Create(Guid.CreateVersion7(), message.BookingId, message.BuyerId,
            [new OrderLine(message.SellerId, message.Amount, message.Currency)]);

        // The winner inserts first but holds its transaction open, so the loser's pre-check sees nothing and
        // its insert waits on the booking's key until the winner commits.
        await using var winnerScope = NewScope();
        var winnerContext = winnerScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await using var winnerTransaction = await winnerContext.Database.BeginTransactionAsync();
        winnerContext.BookingClaims.Add(BookingClaim.Requested(message.BookingId));
        winnerContext.PaymentEvents.Add(winner);
        await winnerContext.SaveChangesAsync();

        var loser = SendAsync(new RequestPayment.Command(message.BookingId, message.BuyerId, message.SellerId,
            message.Amount, message.Currency));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(loser.IsCompleted, "The loser should be blocked on the winner's uncommitted insert.");
        await winnerTransaction.CommitAsync();

        var result = await loser;
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.AlreadyRequested);
        Assert.Equal(winner.CheckoutId, result.Value.CheckoutId);
        Assert.Equal(1, await ReadAsync(c => c.PaymentEvents.CountAsync()));
        // The timer belongs to whoever inserted the checkout; the loser inserted nothing.
        Assert.Empty(Outbox.Scheduled);
    }

    public static TheoryData<string, PaymentRequestedIntegrationEvent> Refused => new()
    {
        { "zero amount", Request(amount: 0m) },
        { "negative amount", Request(amount: -5m) },
        { "sub-cent amount", Request(amount: 10.001m) },
        { "lower-case currency", Request(currency: "eur") },
        { "unknown-length currency", Request(currency: "EURO") },
        { "no seller", Request(sellerId: Guid.Empty) },
        { "no buyer", Request(buyerId: Guid.Empty) },
        { "no booking", Request(bookingId: 0) },
        { "buyer is the seller", SelfPaying() }
    };

    private static PaymentRequestedIntegrationEvent SelfPaying()
    {
        var party = Guid.NewGuid();
        return Request(buyerId: party, sellerId: party);
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task RequestTheDomainRefuses_SavesNothing_AndTellsBookingsThePaymentFailed(string _,
        PaymentRequestedIntegrationEvent message)
    {
        await ConsumeAsync(message);

        Assert.Equal(0, await ReadAsync(c => c.PaymentEvents.CountAsync()));
        Assert.Equal(0, await ReadAsync(c => c.Set<PaymentOrder>().CountAsync()));
        var failed = Assert.Single(Outbox.OfType<BookingPaymentFailedIntegrationEvent>());
        Assert.Equal(message.BookingId, failed.BookingId);
        Assert.Single(Outbox.Published);
        Assert.Empty(Outbox.Scheduled);
    }

    [Fact]
    public async Task RequestTheDomainRefuses_IsAnExpectedFailure_NotAnException()
    {
        var result = await SendAsync(new RequestPayment.Command(7, Guid.NewGuid(), Guid.NewGuid(), 0m, "USD"));

        Assert.False(result.IsSuccess);
        Assert.Equal("payment_request_refused", result.Error!.Code);
        // The command alone decides nothing about messaging; telling Bookings is the consumer's job.
        Assert.Empty(Outbox.Published);
    }
}
