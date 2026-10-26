namespace Notifications.Domain;

// A user's address, from UserRegistered. A user who registered before the sync existed has none.
public class Recipient
{
    public Guid UserId { get; private set; }
    public string Email { get; private set; } = null!;

    private Recipient() { }

    public Recipient(Guid userId, string email)
    {
        UserId = userId;
        Email = email;
    }
}
