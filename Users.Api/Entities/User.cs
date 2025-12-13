namespace Users.Api.Entities;

public class User
{
    public User(string userName, string email, string password, string firstName, string lastName, string phoneNumber)
    {
        UserName = userName;
        Email = email;
        Password = password;
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;
    }

    public long Id { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string PhoneNumber { get; set; }
}