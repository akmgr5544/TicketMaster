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

        // Identity assigned by the app (not the DB). Version 7 is time-ordered, so inserts stay
        // index-friendly rather than scattering like a random GUID. The wire identity is the string
        // form (JWT subject, gateway headers); the store keeps it as a native uuid.
        Id = Guid.CreateVersion7();
    }

    // EF rehydrates through this: a load sets every column, so it must not run the public constructor
    // and mint a throwaway id on each read.
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

    // Customer on self-registration; Admin comes from the first-ever registration or the admin-only
    // role endpoint. Drives the role claim in the JWT and the introspection response.
    public UserRole Role { get; set; }
}
