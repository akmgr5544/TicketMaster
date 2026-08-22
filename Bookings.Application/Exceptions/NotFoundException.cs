namespace Bookings.Application.Exceptions;

public sealed class NotFoundException : BookingsApplicationException
{
    public NotFoundException(string message) : base(message)
    {
    }
    
    public NotFoundException(string entity, string id)
        : base($"{entity} with id '{id}' was not found")
    {
    }
}
