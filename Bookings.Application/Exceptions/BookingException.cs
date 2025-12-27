namespace Bookings.Application.Exceptions;

public class BookingException : Exception
{
    public BookingException(string message) : base(message)
    {
        
    }
}