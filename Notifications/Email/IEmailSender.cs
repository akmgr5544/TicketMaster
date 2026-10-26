namespace Notifications.Email;

public interface IEmailSender
{
    // Throws EmailDeliveryException when the mail server cannot take the message; the send is then retried.
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}

public sealed class EmailDeliveryException(string message, Exception inner) : Exception(message, inner);
