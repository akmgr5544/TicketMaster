namespace Events.Application.Commands;

public record PriceTierRequest(string Name, decimal Price, List<string> Seats);
