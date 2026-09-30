using Bookings.Application.Commands.Bookings;
using Bookings.Application.Queries;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Sql;
using BookingIntegration.Fixtures;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketMaster.Common.IntegrationEvents;

namespace BookingIntegration.Mechanics;

// The real outbox publisher on the real host: what Bookings stages inside a transaction reaches the broker
// once that transaction commits, and never if it rolls back. The host's database is not reset between
// tests, so each one uses an event id of its own and waits only for messages about its own booking.
[Collection(BookingsHostCollection.Name)]
public sealed class OutboundRelayTests
{
    // Generous: the durable sender goes through Postgres before the broker, and the probe's inbox after it.
    private static readonly TimeSpan Relay = TimeSpan.FromSeconds(60);

    // Long enough for a message that was going to be sent to arrive; nothing in the relay is slower.
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(5);

    private readonly BookingsHostFixture _fixture;
    private readonly Seed _seed;

    public OutboundRelayTests(BookingsHostFixture fixture)
    {
        _fixture = fixture;
        _seed = new Seed(fixture.Services);
    }

    [Fact]
    public async Task Making_a_booking_relays_PaymentRequested()
    {
        var (eventId, ids) = await ReservedTicketsAsync("A1", "A2");

        var bookingId = await SendAsync(new MakeBookingCommand(TestUsers.Owner, eventId, ids));

        var request = await _fixture.Received.WaitForAsync<PaymentRequestedIntegrationEvent>(
            m => m.BookingId == bookingId, Relay);
        Assert.Equal(TestUsers.Owner, request.BuyerId);
        Assert.Equal(100m, request.Amount);
        Assert.Equal("USD", request.Currency);
    }

    [Fact]
    public async Task Cancelling_a_booking_relays_BookingCancelled()
    {
        var bookingId = await BookedAsync();

        await SendAsync(new CancelBookingCommand(bookingId, TestUsers.Owner));

        var cancelled = await _fixture.Received.WaitForAsync<BookingCancelledIntegrationEvent>(
            m => m.BookingId == bookingId, Relay);
        Assert.Equal(bookingId, cancelled.BookingId);
    }

    // The consumer path: Wolverine's inbox and EF middleware own this scope, not TransactionBehavior alone.
    [Fact]
    public async Task A_BookingPaymentFailed_off_the_broker_cancels_and_relays_BookingCancelled()
    {
        var bookingId = await BookedAsync();

        await _fixture.Payments.PublishAsync(new BookingPaymentFailedIntegrationEvent(bookingId));

        await _fixture.Received.WaitForAsync<BookingCancelledIntegrationEvent>(m => m.BookingId == bookingId, Relay);
        var stored = await ReadAsync(context => context.Bookings.SingleAsync(b => b.Id == bookingId));
        Assert.Equal(BookingStatus.Cancelled, stored.Status);
    }

    [Fact]
    public async Task A_rolled_back_booking_takes_its_PaymentRequested_with_it()
    {
        var (eventId, ids) = await ReservedTicketsAsync("A1");
        long bookingId;

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                // TransactionBehavior defers to the transaction already open, as it does under Wolverine.
                bookingId = await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new MakeBookingCommand(TestUsers.Owner, eventId, ids));
                await transaction.RollbackAsync();
            }

            await CommitSomethingElseAsync(context, eventId);
        }

        Assert.Equal(0, await ReadAsync(context => context.Bookings.CountAsync(b => b.Id == bookingId)));
        await Task.Delay(Silence);
        Assert.Empty(_fixture.Received.For<PaymentRequestedIntegrationEvent>(m => m.BookingId == bookingId));
    }

    [Fact]
    public async Task A_rolled_back_cancellation_takes_its_BookingCancelled_with_it()
    {
        var bookingId = await BookedAsync();

        await using (var scope = _fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new CancelBookingCommand(bookingId, TestUsers.Owner));
                await transaction.RollbackAsync();
            }

            await CommitSomethingElseAsync(context, "evt-unrelated");
        }

        var stored = await ReadAsync(context => context.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId));
        Assert.Equal(BookingStatus.Booked, stored.Status);
        await Task.Delay(Silence);
        Assert.Empty(_fixture.Received.For<BookingCancelledIntegrationEvent>(m => m.BookingId == bookingId));
    }

    // The same scope commits afterwards: a publisher that kept the rolled-back message for the next commit
    // would send it now.
    private static async Task CommitSomethingElseAsync(BookingDomainContext context, string eventId)
    {
        context.ChangeTracker.Clear();
        await using var next = await context.Database.BeginTransactionAsync();
        context.Tickets.Add(new Ticket("Z9", "venue-unrelated", eventId, Seed.Soon));
        await context.SaveChangesAsync();
        await next.CommitAsync();
    }

    private async Task<(string EventId, long[] Ids)> ReservedTicketsAsync(params string[] seats)
    {
        var eventId = NewEventId();
        var ids = (await _seed.TicketsAsync(eventId, seats)).Select(t => t.Id).ToArray();
        await _seed.ReservationAsync(TestUsers.Owner, eventId, ids);
        return (eventId, ids);
    }

    private async Task<long> BookedAsync()
    {
        var tickets = await _seed.TicketsAsync(NewEventId(), "A1");
        return (await _seed.BookingAsync(TestUsers.Owner, tickets[0].Id)).Id;
    }

    private async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private async Task SendAsync(IRequest request)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private async Task<T> ReadAsync<T>(Func<BookingDomainContext, Task<T>> read)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<BookingDomainContext>());
    }

    private static string NewEventId() => $"evt-{Guid.NewGuid():N}";
}
