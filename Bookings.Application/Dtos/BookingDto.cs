namespace Bookings.Application.Dtos;

public record BookingDto(long Id, string Status, DateTime CreatedAt, long[] TicketIds, BookingHistoryDto[] History,
    BookingRefundDto[] Refunds);

public record BookingHistoryDto(string Status, int TicketsCount);

// Amount and currency are null for the whole-booking refund a relocation asks for: everything still paid.
public record BookingRefundDto(Guid Id, string Status, decimal? Amount, string? Currency, long[] TicketIds);
