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
    }

    public long Id { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string PhoneNumber { get; set; }

    public string RefreshToken { get; set; }

    public DateTime RefreshTokenExpires { get; set; }

    // Self-registration always yields Customer; Admin is granted only by the startup seeder. Drives the
    // role claim in the JWT and the introspection response the gateway propagates downstream.
    public UserRole Role { get; set; }
}