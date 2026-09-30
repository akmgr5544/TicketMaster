using System.Security.Cryptography;
using System.Text;

namespace Bookings.Application.Services;

// A placeholder until real pricing exists: Bookings owns no price and no seller, yet PaymentRequested
// has to carry both. A flat per-ticket price, and a seller derived from the event id so every booking for
// one event pays the same merchant. PaymentSystem refuses a payment whose seller is the buyer; a hash of
// an event id colliding with a user's Guid is not a practical concern.
public static class PaymentPricing
{
    public const string Currency = "USD";
    public const decimal PerTicket = 50m;

    public static decimal AmountFor(int ticketCount) => PerTicket * ticketCount;

    public static Guid SellerFor(string eventId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(eventId)).AsSpan(0, 16));
}
