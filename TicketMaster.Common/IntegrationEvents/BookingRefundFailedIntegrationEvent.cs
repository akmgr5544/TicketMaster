namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// A refund for the booking failed: the PSP refused it, or took it back after reporting it done. The money is still
/// with the seller and a person has to return it. Published by PaymentSystem, consumed by Notifications so the buyer
/// is not left believing the refund is on its way. <c>RefundId</c> follows <see cref="BookingRefundedIntegrationEvent"/>.
/// </summary>
public record BookingRefundFailedIntegrationEvent(long BookingId, Guid? RefundId = null);
