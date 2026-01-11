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
    }

    public long Id { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string PhoneNumber { get; set; }

    public string RefreshToken
    {
        get;
        set
        {
            field = value;
            RefreshTokenExpires = DateTime.UtcNow.AddDays(1);
        }
    }

    public DateTime RefreshTokenExpires { get; set; }
}