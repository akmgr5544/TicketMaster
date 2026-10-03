namespace Events.Api.Requests;

public record UpdateVenueRequest(string Name, string Address, double Latitude, double Longitude);
