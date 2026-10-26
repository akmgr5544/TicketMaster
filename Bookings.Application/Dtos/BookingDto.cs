namespace Bookings.Application.Dtos;

public record BookingDto(long Id, string Status, DateTime CreatedAt, long[] TicketIds, BookingHistoryDto[] History,
    BookingRefundDto[] Refunds);

public record BookingHistoryDto(string Status, int TicketsCount);

public record BookingRefundDto(Guid Id, string Status, long[] TicketIds);
