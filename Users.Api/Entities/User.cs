namespace Users.Api.Entities;

public class User
{
    public User(string userName,
        string email,
        string firstName,
        string lastName,
        string phoneNumber)
    {
        UserName = userName;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;

        PasswordHash = string.Empty;
        RefreshToken = string.Empty;
        RefreshTokenExpires = DateTime.MinValue;
        Role = UserRole.Customer;

        Id = Guid.CreateVersion7();
    }

    private User()
    {
    }

    public Guid Id { get; set; }
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public string RefreshToken { get; set; } = null!;
    public DateTime RefreshTokenExpires { get; set; }
    public UserRole Role { get; set; }
}
