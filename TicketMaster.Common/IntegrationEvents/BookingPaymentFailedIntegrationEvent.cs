namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// The payment for <paramref name="BookingId"/> failed, was declined, or never arrived in time. The
/// booking is void and the seats it held go back on sale.
/// <para>
/// This is the only thing that releases a booked seat whose payment never came. Reservations expire
/// on their own in Redis, but a booking has already replaced that hold with a durable one — so if this
/// event is never published, those seats stay held indefinitely.
/// </para>
/// </summary>
public record BookingPaymentFailedIntegrationEvent(long BookingId);
