namespace Bookings.Application.Dtos;

public record BookingDto(long Id, string Status, DateTime CreatedAt, long[] TicketIds, BookingHistoryDto[] History);

public record BookingHistoryDto(string Status, int TicketsCount);
