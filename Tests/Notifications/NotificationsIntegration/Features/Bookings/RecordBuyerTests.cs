using Microsoft.EntityFrameworkCore;
using Notifications.Features.Bookings;
using NotificationsIntegration.Fixtures;
using TicketMaster.Common.IntegrationEvents;

namespace NotificationsIntegration.Features.Bookings;

public sealed class RecordBuyerTests(NotificationsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task A_payment_request_remembers_who_bought_the_booking()
    {
        var message = new PaymentRequestedIntegrationEvent(NewBookingId(), Guid.NewGuid(), Guid.NewGuid(), 50m, "USD");

        await HandleAsync<PaymentRequestedHandler>((handler, context) => handler.Handle(message, context, default));

        var buyer = await ReadAsync(c => c.BookingBuyers.SingleAsync(b => b.BookingId == message.BookingId));
        Assert.Equal(message.BuyerId, buyer.BuyerId);
    }

    [Fact]
    public async Task A_redelivered_payment_request_adds_nothing()
    {
        var message = new PaymentRequestedIntegrationEvent(NewBookingId(), Guid.NewGuid(), Guid.NewGuid(), 50m, "USD");

        await HandleAsync<PaymentRequestedHandler>((handler, context) => handler.Handle(message, context, default));
        await HandleAsync<PaymentRequestedHandler>((handler, context) => handler.Handle(message, context, default));

        Assert.Equal(1, await ReadAsync(c => c.BookingBuyers.CountAsync(b => b.BookingId == message.BookingId)));
    }
}
