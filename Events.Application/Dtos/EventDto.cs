namespace Events.Application.Dtos;

public record EventDto(DateTime StartDate,
    string Venue,
    List<string> Performers);