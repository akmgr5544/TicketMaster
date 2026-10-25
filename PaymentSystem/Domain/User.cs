namespace PaymentSystem.Domain;

public class User
{
    public Guid UserId { get; private set; }
    public string Email { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // EF Core shadowed constructor
    private User()
    {
    }

    private User(Guid userId, string email)
    {
        UserId = userId;
        Email = email;
        CreatedAt = DateTime.UtcNow;
    }

    public static User Create(Guid userId, string email)
        => new(userId, email);

    public void UpdateEmail(string newEmail)
    {
        Email = newEmail;
    }
}